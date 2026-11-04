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
    [McpServerTool(Name = "find_references"), Description("Every usage of a symbol: file, line, snippet, and kind (invocation, instantiation, inheritance, parameter, type-argument, assignment, implementation, usage). For calling methods only, use find_callers.")]
    public static async Task<string> ExecuteAsync(
        WorkspaceManager workspace,
        [Description("Symbol name or SymbolId")] string symbolName,
        string? file = null,
        int? line = null,
        int maxResults = 500,
        int? offset = null,
        string? format = null,
        CancellationToken ct = default)
    {
        var notReady = await workspace.EnsureReadyOrStatusAsync(ct);
        if (notReady is not null) return notReady;
        if (ListFormat.Validate(format) is { } invalid) return invalid;

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

        // A stable order (file, then position) so an offset names the same entry on every
        // call; Roslyn's own order follows its parallel search.
        unique = [.. unique.OrderBy(f => f.Path, StringComparer.Ordinal).ThenBy(f => f.Position)];

        var page = Paging.Apply(unique, limit, offset ?? 0);
        var results = await BuildEntriesAsync(workspace, solution, page.Items, ct);
        var entries = page.WithItems(results);

        // On a partial page, where the rest lives: counts over the complete result set.
        var byProject = entries.Truncated || entries.Offset > 0
            ? ListFormat.CountBy(unique, f => ListFormat.ProjectName(f.Location.Document.Project))
            : null;

        return ListFormat.Render(
            entries,
            format,
            p => ToolJson.Serialize(new ReferencesResult(
                p.Items, p.Count, p.TotalFound, p.Truncated, p.Limit, p.Offset, p.NextOffset, ByProject: byProject)),
            p => ToolJson.Serialize(new ReferencesResult(
                null, p.Count, p.TotalFound, p.Truncated, p.Limit, p.Offset, p.NextOffset,
                Files: ListFormat.GroupByFile(p.Items, r => r.File, r => $"{r.Line} {r.Kind}{(r.IsGenerated ? " generated" : "")}"),
                ByProject: byProject)));
    }

    /// <summary>Longest snippet kept in a full entry; a minified or generated line can run to kilobytes.</summary>
    private const int MaxSnippet = 160;

    private static async Task<List<RefLocation>> BuildEntriesAsync(
        WorkspaceManager workspace, Solution solution, List<FoundLocation> page, CancellationToken ct)
    {
        var generated = new GeneratedCodeIndex(workspace);
        var textCache = new Dictionary<DocumentId, Microsoft.CodeAnalysis.Text.SourceText>();
        var results = new List<RefLocation>(page.Count);

        foreach (var found in page)
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
                if (snippet.Length > MaxSnippet)
                    snippet = snippet[..MaxSnippet] + "…";
            }

            results.Add(new RefLocation(
                File: workspace.ToRelativePath(lineSpan.Path),
                Line: lineSpan.StartLinePosition.Line + 1,
                Snippet: snippet,
                Kind: ClassifyReferenceKind(location, ct),
                IsGenerated: generated.IsGenerated(location.Location.SourceTree, ct),
                TargetFrameworks: found.TargetFrameworks));
        }

        return results;
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
