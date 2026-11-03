using System.ComponentModel;
using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using ModelContextProtocol.Server;

namespace CWM.RoslynNavigator.Tools;

[McpServerToolType]
public static class GetSymbolAtTool
{
    [McpServerTool(Name = "get_symbol_at"), Description("Resolve the symbol at a position in a file: what an identifier on a given line actually binds to. Pass the identifier's text instead of a column — counting columns is error-prone — or a 1-based column; with neither, the first identifier on the line that binds to a symbol is used. Returns kind, full signature, containing type, a stable SymbolId (pass it as the name to find_references, find_callers, get_symbol_detail, etc. to target exactly this symbol, overload included), the definition location, and for locals, parameters and expressions the declared type and nullability plus the compiler's expression type and null-state.")]
    public static async Task<string> ExecuteAsync(
        WorkspaceManager workspace,
        [Description("File path; a suffix such as 'Orders/OrderService.cs' is enough ('./' and backslashes accepted)")] string file,
        [Description("1-based line number")] int line,
        [Description("Identifier text on that line to resolve, e.g. 'GetByIdAsync' (preferred over column)")] string? text = null,
        [Description("1-based column, used when text is not given")] int? column = null,
        [Description("Which occurrence of text on the line, when it appears more than once (1-based)")] int occurrence = 1,
        CancellationToken ct = default)
    {
        var notReady = await workspace.EnsureReadyOrStatusAsync(ct);
        if (notReady is not null) return notReady;

        var solution = workspace.GetSolution();
        if (solution is null)
            return Error(ErrorCodes.InvalidArgument, "No solution is loaded.");

        var documents = DocumentResolver.FindDocuments(solution, file);
        if (documents.Count == 0)
            return Error(ErrorCodes.FileNotFound, $"File '{file}' is not part of any project in the solution.");

        // One file compiled by several target frameworks is still one file.
        var paths = documents.Select(d => d.FilePath!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (paths.Count > 1)
        {
            return JsonSerializer.Serialize(new ErrorResponse(ErrorCodes.AmbiguousMatch,
                $"'{file}' matches {paths.Count} files. Pass a longer path.",
                [.. paths.Take(10).Select(p => new SymbolCandidate(workspace.ToRelativePath(p), "file", workspace.ToRelativePath(p), 0))]));
        }

        var document = documents[0];
        var sourceText = await document.GetTextAsync(ct);
        if (line < 1 || line > sourceText.Lines.Count)
            return Error(ErrorCodes.InvalidArgument, $"Line {line} is outside '{file}' (1-{sourceText.Lines.Count}).");

        var root = await document.GetSyntaxRootAsync(ct);
        var semanticModel = await document.GetSemanticModelAsync(ct);
        if (root is null || semanticModel is null)
            return Error(ErrorCodes.NoSource, $"'{file}' has no syntax tree.");

        var textLine = sourceText.Lines[line - 1];
        var token = FindToken(root, semanticModel, textLine, text, column, Math.Max(1, occurrence), ct);
        if (token is not { } found)
        {
            return Error(ErrorCodes.SymbolNotFound, text is not null
                ? $"'{text}' does not occur {(occurrence > 1 ? $"{occurrence} times " : "")}on line {line} of '{file}'. Line reads: {textLine.ToString().Trim()}"
                : $"No identifier on line {line} of '{file}' binds to a symbol. Line reads: {textLine.ToString().Trim()}");
        }

        var symbol = await ResolveSymbolAsync(document, semanticModel, found, ct);
        if (symbol is null)
        {
            return Error(ErrorCodes.SymbolNotFound,
                $"'{found.Text}' at {line}:{found.GetLocation().GetLineSpan().StartLinePosition.Character + 1} does not bind to a symbol (keyword, literal, or unresolved name).");
        }

        return JsonSerializer.Serialize(Describe(workspace, semanticModel, found, symbol, ct));
    }

    /// <summary>
    /// Picks the token to resolve. Text: the n-th token on the line whose text equals it, else
    /// the token at the n-th raw occurrence. Column: the token at that position. Neither: the
    /// first identifier on the line that binds to something.
    /// </summary>
    private static SyntaxToken? FindToken(
        SyntaxNode root, SemanticModel model, TextLine textLine, string? text, int? column, int occurrence, CancellationToken ct)
    {
        var lineTokens = root.DescendantTokens(textLine.Span)
            .Where(t => textLine.Span.Contains(t.Span.Start))
            .ToList();

        if (!string.IsNullOrWhiteSpace(text))
        {
            var needle = text.Trim();
            var exact = lineTokens.Where(t => t.ValueText == needle || t.Text == needle).ToList();
            if (exact.Count >= occurrence) return exact[occurrence - 1];

            var lineString = textLine.ToString();
            var index = -1;
            for (var n = 0; n < occurrence; n++)
            {
                index = lineString.IndexOf(needle, index + 1, StringComparison.Ordinal);
                if (index < 0) return null;
            }
            return root.FindToken(textLine.Start + index);
        }

        if (column is { } col)
        {
            var position = textLine.Start + Math.Clamp(col - 1, 0, Math.Max(0, textLine.Span.Length - 1));
            return root.FindToken(position);
        }

        foreach (var candidate in lineTokens)
        {
            if (!candidate.IsKind(SyntaxKind.IdentifierToken) || candidate.Parent is null) continue;

            if (model.GetDeclaredSymbol(candidate.Parent, ct) is not null
                || model.GetSymbolInfo(candidate.Parent, ct).Symbol is not null)
            {
                return candidate;
            }
        }

        return null;
    }

    private static async Task<ISymbol?> ResolveSymbolAsync(
        Document document, SemanticModel model, SyntaxToken token, CancellationToken ct)
    {
        var symbol = await SymbolFinder.FindSymbolAtPositionAsync(document, token.SpanStart, ct);
        if (symbol is not null) return symbol;

        if (token.Parent is not { } parent) return null;

        if (model.GetDeclaredSymbol(parent, ct) is { } declared) return declared;

        // Overload resolution failed: the candidates are still what the name refers to.
        var info = model.GetSymbolInfo(parent, ct);
        return info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
    }

    private static SymbolAtResult Describe(
        WorkspaceManager workspace, SemanticModel model, SyntaxToken token, ISymbol symbol, CancellationToken ct)
    {
        var tokenPosition = token.GetLocation().GetLineSpan().StartLinePosition;
        var isDeclaration = token.Parent is { } parent
            && SymbolEqualityComparer.Default.Equals(model.GetDeclaredSymbol(parent, ct), symbol);

        var (declaredType, declaredNullability) = symbol switch
        {
            ILocalSymbol local => (local.Type, local.NullableAnnotation),
            IParameterSymbol parameter => (parameter.Type, parameter.NullableAnnotation),
            IFieldSymbol field => (field.Type, field.NullableAnnotation),
            IPropertySymbol property => (property.Type, property.NullableAnnotation),
            IEventSymbol @event => (@event.Type, @event.NullableAnnotation),
            IMethodSymbol method => (method.ReturnType, method.ReturnNullableAnnotation),
            _ => ((ITypeSymbol?)null, NullableAnnotation.None)
        };

        string? expressionType = null, expressionNullability = null, convertedType = null;
        if (!isDeclaration && ExpressionAt(token) is { } expression)
        {
            var typeInfo = model.GetTypeInfo(expression, ct);
            if (typeInfo.Type is { } type)
            {
                expressionType = type.ToDisplayString();
                expressionNullability = FlowState(typeInfo.Nullability.FlowState);
                if (typeInfo.ConvertedType is { } converted
                    && !SymbolEqualityComparer.IncludeNullability.Equals(converted, type))
                {
                    convertedType = converted.ToDisplayString();
                }
            }
        }

        var definition = symbol.OriginalDefinition;
        var location = SymbolResolver.GetLocation(definition);
        var inSource = location is not null;

        return new SymbolAtResult(
            Token: token.Text,
            Line: tokenPosition.Line + 1,
            Column: tokenPosition.Character + 1,
            Name: symbol.Name,
            Kind: SymbolResolver.GetKindString(symbol),
            Signature: symbol.ToDisplayString(),
            ContainingType: symbol.ContainingType?.ToDisplayString(),
            Namespace: symbol.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : null,
            SymbolId: SymbolResolver.GetSymbolId(symbol),
            IsDeclaration: isDeclaration,
            DeclaredType: declaredType?.ToDisplayString(),
            DeclaredNullability: declaredType is null ? null : Annotation(declaredNullability),
            ExpressionType: expressionType,
            ExpressionNullability: expressionNullability,
            ConvertedType: convertedType,
            DefinitionFile: location is { } loc ? workspace.ToRelativePath(loc.File) : "metadata",
            DefinitionLine: location?.Line ?? 0,
            IsGenerated: inSource && new GeneratedCodeIndex(workspace).IsGenerated(definition, ct),
            Assembly: inSource ? null : definition.ContainingAssembly?.Name);
    }

    /// <summary>
    /// The expression whose type answers "what is this identifier here": the identifier
    /// itself, or the member access / conditional access it names the right side of.
    /// </summary>
    private static ExpressionSyntax? ExpressionAt(SyntaxToken token)
    {
        if (token.Parent is not ExpressionSyntax expression) return null;

        return expression.Parent switch
        {
            MemberAccessExpressionSyntax access when access.Name == expression => access,
            MemberBindingExpressionSyntax binding => binding,
            _ => expression
        };
    }

    private static string Annotation(NullableAnnotation annotation) => annotation switch
    {
        NullableAnnotation.Annotated => "nullable",
        NullableAnnotation.NotAnnotated => "non-nullable",
        _ => "oblivious"
    };

    private static string? FlowState(NullableFlowState state) => state switch
    {
        NullableFlowState.MaybeNull => "maybe-null",
        NullableFlowState.NotNull => "not-null",
        _ => null
    };

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new ErrorResponse(code, message));
}
