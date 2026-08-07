using System.ComponentModel;
using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using ModelContextProtocol.Server;

namespace CWM.RoslynNavigator.Tools;

[McpServerToolType]
public static class FindOverridesTool
{
    [McpServerTool(Name = "find_overrides"), Description("Find all overrides of a virtual or abstract method across the solution. Useful for understanding polymorphic behavior. Each result carries IsGenerated so generated overrides can be skipped.")]
    public static async Task<string> ExecuteAsync(
        WorkspaceManager workspace,
        [Description("The virtual or abstract method name to find overrides for")] string methodName,
        [Description("Optional: containing class name to disambiguate")] string? className = null,
        [Description("Maximum results to return. TotalFound in the response reports the full count; re-query with a higher value if it exceeds Count.")] int maxResults = 50,
        CancellationToken ct = default)
    {
        var notReady = await workspace.EnsureReadyOrStatusAsync(ct);
        if (notReady is not null) return notReady;

        var solution = workspace.GetSolution();
        if (solution is null)
            return Serialize(Paging.Empty<OverrideInfo>(maxResults));

        var lookupName = className is not null && !methodName.Contains('.')
            ? $"{className}.{methodName}"
            : methodName;

        var resolved = await SymbolResolver.ResolveOrErrorAsync(workspace, lookupName, ct: ct);
        if (resolved.Failed) return resolved.Error;

        var symbol = resolved.Symbol;

        var overrides = await SymbolFinder.FindOverridesAsync(symbol, solution, cancellationToken: ct);

        var generated = new GeneratedCodeIndex(workspace);
        var all = new List<OverrideInfo>();

        foreach (var overrideSymbol in overrides)
        {
            var location = SymbolResolver.GetLocation(overrideSymbol);
            if (location.HasValue)
            {
                all.Add(new OverrideInfo(
                    Method: overrideSymbol.Name,
                    ContainingType: overrideSymbol.ContainingType?.Name ?? "unknown",
                    File: workspace.ToRelativePath(location.Value.File),
                    Line: location.Value.Line,
                    IsGenerated: generated.IsGenerated(overrideSymbol, ct)));
            }
        }

        return Serialize(Paging.Apply(all, maxResults));
    }

    private static string Serialize(Paging.Page<OverrideInfo> page) =>
        JsonSerializer.Serialize(new OverridesResult(
            page.Items, page.Count, page.TotalFound, page.Truncated, page.Limit));
}
