using System.ComponentModel;
using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using ModelContextProtocol.Server;
using RefLocation = CWM.RoslynNavigator.Responses.ReferenceLocation;

namespace CWM.RoslynNavigator.Tools;

[McpServerToolType]
public static class FindReferencesTool
{
    [McpServerTool(Name = "find_references"), Description("Find all usages of a symbol across the solution. Returns file, line, snippet, and reference kind (usage, inheritance, invocation, instantiation, parameter, type-argument, assignment, implementation). Each result carries IsGenerated so generated call sites can be skipped.")]
    public static async Task<string> ExecuteAsync(
        WorkspaceManager workspace,
        [Description("Symbol to find references for. Bare, type-qualified ('OrderService.CreateAsync'), or fully qualified. Qualify when the name is reused across types.")] string symbolName,
        [Description("Optional: file path to disambiguate (e.g., 'IOrderRepository.cs')")] string? file = null,
        [Description("Optional: line number to disambiguate")] int? line = null,
        [Description("Maximum results to return. TotalFound in the response reports the full count; re-query with a higher value if it exceeds Count.")] int maxResults = 50,
        CancellationToken ct = default)
    {
        var notReady = await workspace.EnsureReadyOrStatusAsync(ct);
        if (notReady is not null) return notReady;

        var limit = Math.Max(1, maxResults);

        var solution = workspace.GetSolution();
        if (solution is null)
            return JsonSerializer.Serialize(new ReferencesResult([], 0, 0, false, limit));

        var resolved = await SymbolResolver.ResolveOrErrorAsync(workspace, symbolName, file, line, ct: ct);
        if (resolved.Failed) return resolved.Error;

        var symbol = resolved.Symbol;

        var references = await SymbolFinder.FindReferencesAsync(symbol, solution, ct);
        var totalFound = references.Sum(r => r.Locations.Count());

        var generated = new GeneratedCodeIndex(workspace);
        var textCache = new Dictionary<DocumentId, Microsoft.CodeAnalysis.Text.SourceText>();
        var results = new List<RefLocation>();
        var capped = false;

        foreach (var reference in references)
        {
            foreach (var location in reference.Locations)
            {
                if (results.Count >= limit) { capped = true; break; }

                var lineSpan = location.Location.GetLineSpan();
                var document = solution.GetDocument(location.Document.Id);
                var snippet = "";
                if (document is not null)
                {
                    if (!textCache.TryGetValue(document.Id, out var text))
                    {
                        text = await document.GetTextAsync(ct);
                        textCache[document.Id] = text;
                    }
                    var refLine = text.Lines[lineSpan.StartLinePosition.Line];
                    snippet = refLine.ToString().Trim();
                }

                results.Add(new RefLocation(
                    File: workspace.ToRelativePath(lineSpan.Path),
                    Line: lineSpan.StartLinePosition.Line + 1,
                    Snippet: snippet,
                    Kind: ClassifyReferenceKind(location, ct),
                    IsGenerated: generated.IsGenerated(location.Location.SourceTree, ct)));
            }
            if (capped) break;
        }

        // Snippets are built lazily as locations stream in, so this tool caps in the loop
        // rather than via Paging — the contract it reports is identical.
        return JsonSerializer.Serialize(new ReferencesResult(
            results, results.Count, totalFound, totalFound > results.Count, limit));
    }

    private static string ClassifyReferenceKind(
        Microsoft.CodeAnalysis.FindSymbols.ReferenceLocation location,
        CancellationToken ct)
    {
        var syntaxTree = location.Location.SourceTree;
        if (syntaxTree is null)
            return "usage";

        var root = syntaxTree.GetRoot(ct);
        var node = root.FindNode(location.Location.SourceSpan, findInsideTrivia: false, getInnermostNodeForTie: true);
        if (node is null)
            return "usage";

        // Walk up to find meaningful parent context
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case BaseListSyntax:
                case SimpleBaseTypeSyntax:
                    return "inheritance";

                case ObjectCreationExpressionSyntax:
                case ImplicitObjectCreationExpressionSyntax:
                    return "instantiation";

                case InvocationExpressionSyntax:
                    return "invocation";

                case ParameterSyntax:
                    return "parameter";

                case TypeArgumentListSyntax:
                    return "type-argument";

                case AssignmentExpressionSyntax assignment
                    when assignment.Left.Span.Contains(node.Span):
                    return "assignment";

                // Stop walking at statement/member boundaries
                case StatementSyntax:
                case MemberDeclarationSyntax:
                    return "usage";
            }
        }

        return "usage";
    }
}
