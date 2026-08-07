using System.ComponentModel;
using System.Text;
using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ModelContextProtocol.Server;

namespace CWM.RoslynNavigator.Tools;

[McpServerToolType]
public static class GetSymbolSourceTool
{
    private const int MinChars = 200;
    private const int MaxChars = 20_000;

    [McpServerTool(Name = "get_symbol_source"), Description("Get the exact source of ONE symbol without reading the whole file. Members (methods, properties) return their full source including doc comment and attributes. Types return a signatures-only skeleton unless includeBodies is set. Output is capped by maxChars with a Truncated flag. Accepts dotted names like 'OrderService.GetOrderAsync'.")]
    public static async Task<string> ExecuteAsync(
        WorkspaceManager workspace,
        [Description("The symbol name. Dotted form 'ContainingType.Member' disambiguates members.")] string symbolName,
        [Description("Optional: containing type name for methods/properties")] string? containingType = null,
        [Description("Optional: file path to disambiguate")] string? file = null,
        [Description("Optional: line number to disambiguate")] int? line = null,
        [Description("For types only: include full member bodies instead of the signatures-only skeleton")] bool includeBodies = false,
        [Description("Maximum characters of source to return (200-20000)")] int maxChars = 8000,
        CancellationToken ct = default)
    {
        var notReady = await workspace.EnsureReadyOrStatusAsync(ct);
        if (notReady is not null) return notReady;

        // Qualified names resolve natively in SymbolResolver. An explicit containingType
        // argument is folded into the same qualified form, and only falls back to the bare
        // name when that yields nothing (e.g. symbolName is itself the type).
        var lookupName = containingType is not null && !symbolName.Contains('.')
            ? $"{containingType}.{symbolName}"
            : symbolName;

        var resolved = await SymbolResolver.ResolveOrErrorAsync(workspace, lookupName, file, line, ct: ct);
        if (resolved.Failed) return resolved.Error;

        var symbol = resolved.Symbol;

        var syntaxRef = symbol.DeclaringSyntaxReferences.FirstOrDefault();
        if (syntaxRef is null)
            return JsonSerializer.Serialize(new ErrorResponse(ErrorCodes.NoSource,
                $"'{symbolName}' has no source in this solution (metadata symbol)."));

        var node = (await syntaxRef.GetSyntaxAsync(ct)).FirstAncestorOrSelf<MemberDeclarationSyntax>();
        if (node is null)
            return JsonSerializer.Serialize(new ErrorResponse(ErrorCodes.NoSource,
                $"'{symbolName}' does not map to a member declaration."));

        maxChars = Math.Clamp(maxChars, MinChars, MaxChars);

        var source = symbol is INamedTypeSymbol && node is BaseTypeDeclarationSyntax typeDecl && !includeBodies
            ? BuildTypeSkeleton(typeDecl)
            : GetFullSource(node, ct);

        var truncated = source.Length > maxChars;
        if (truncated)
            source = source[..maxChars];

        var lineSpan = node.GetLocation().GetLineSpan();
        var filePath = node.SyntaxTree.FilePath is { } p ? workspace.ToRelativePath(p) : "unknown";

        return JsonSerializer.Serialize(new SymbolSourceResult(
            Name: symbol.Name,
            Kind: SymbolResolver.GetKindString(symbol),
            File: filePath,
            StartLine: lineSpan.StartLinePosition.Line + 1,
            EndLine: lineSpan.EndLinePosition.Line + 1,
            Source: source,
            Truncated: truncated));
    }

    /// <summary>
    /// Full source of a member declaration including its leading doc comment and attributes
    /// (leading trivia), with leading blank lines trimmed.
    /// </summary>
    private static string GetFullSource(MemberDeclarationSyntax node, CancellationToken ct)
    {
        var text = node.SyntaxTree.GetText(ct).ToString(node.FullSpan);
        return text.TrimStart('\r', '\n').TrimEnd();
    }

    /// <summary>
    /// Signatures-only skeleton for a type: header plus one line per member, no bodies —
    /// keeps god-classes inside the token budget.
    /// </summary>
    private static string BuildTypeSkeleton(BaseTypeDeclarationSyntax typeDecl)
    {
        var sb = new StringBuilder();
        sb.AppendLine(SyntaxSignatures.GetTypeHeader(typeDecl));
        sb.AppendLine("{");

        IEnumerable<MemberDeclarationSyntax> members = typeDecl switch
        {
            TypeDeclarationSyntax t => t.Members,
            EnumDeclarationSyntax e => e.Members,
            _ => []
        };

        foreach (var member in members)
        {
            var signature = SyntaxSignatures.GetMemberSignature(member);
            var suffix = member is BaseTypeDeclarationSyntax ? " { /* nested type */ }" : ";";
            sb.Append("    ").Append(signature).AppendLine(suffix);
        }

        sb.Append('}');
        return sb.ToString();
    }
}
