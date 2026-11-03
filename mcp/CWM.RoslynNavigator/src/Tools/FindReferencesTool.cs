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
    [McpServerTool(Name = "find_references"), Description("Find all usages of a symbol across the solution. Returns file, line, snippet, and reference kind (usage, inheritance, invocation, instantiation, parameter, type-argument, assignment, implementation). A reference in generated code carries IsGenerated: true (omitted when false) so it can be skipped.")]
    public static async Task<string> ExecuteAsync(
        WorkspaceManager workspace,
        [Description("Symbol to find references for. Bare, type-qualified ('OrderService.CreateAsync'), or fully qualified. Qualify when the name is reused across types. Also accepts a SymbolId from find_symbol/get_symbol_at, which names exactly one symbol (overload included).")] string symbolName,
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
            return ToolJson.Serialize(new ReferencesResult([], 0, 0, false, limit));

        var resolved = await SymbolResolver.ResolveOrErrorAsync(workspace, symbolName, file, line, ct: ct);
        if (resolved.Failed) return resolved.Error;

        var symbol = resolved.Symbol;

        var references = await SymbolFinder.FindReferencesAsync(symbol, solution, ct);

        // One entry per source position. Roslyn reports a location once per project that
        // compiles the file — every target framework of a multi-targeted project — and once
        // per cascaded definition, so the raw count overstates the real usages many times.
        // Positions are collected first (cheap) and snippets built only for the page.
        var flavors = new TargetFrameworkFlavors(solution);
        var unique = flavors.Merge(
            references
                .SelectMany(r => r.Locations)
                .Select(l => (Found: new FoundLocation(l, l.Location.SourceTree?.FilePath ?? "", l.Location.SourceSpan.Start, null), (ProjectId?)l.Document.Project.Id)),
            f => (f.Path, f.Position),
            (f, tfms) => f with { TargetFrameworks = tfms });

        var generated = new GeneratedCodeIndex(workspace);
        var textCache = new Dictionary<DocumentId, Microsoft.CodeAnalysis.Text.SourceText>();
        var results = new List<RefLocation>();

        foreach (var found in unique.Take(limit))
        {
            var location = found.Location;
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
                IsGenerated: generated.IsGenerated(location.Location.SourceTree, ct),
                TargetFrameworks: found.TargetFrameworks));
        }

        return ToolJson.Serialize(new ReferencesResult(
            results, results.Count, unique.Count, unique.Count > results.Count, limit));
    }

    private sealed record FoundLocation(
        Microsoft.CodeAnalysis.FindSymbols.ReferenceLocation Location,
        string Path,
        int Position,
        List<string>? TargetFrameworks);

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
