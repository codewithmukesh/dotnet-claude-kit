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
    [McpServerTool(Name = "find_overrides"), Description("Overrides of a virtual or abstract method across the solution.")]
    public static async Task<string> ExecuteAsync(
        WorkspaceManager workspace,
        [Description("Method name or SymbolId")] string methodName,
        string? className = null,
        int maxResults = 200,
        int? offset = null,
        CancellationToken ct = default)
    {
        var notReady = await workspace.EnsureReadyOrStatusAsync(ct);
        if (notReady is not null) return notReady;

        var solution = workspace.GetSolution();
        if (solution is null)
            return Serialize(Paging.Empty<OverrideInfo>(maxResults));

        var lookupName = SymbolResolver.Qualify(methodName, className);

        var resolved = await SymbolResolver.ResolveOrErrorAsync(workspace, lookupName, ct: ct);
        if (resolved.Failed) return resolved.Error;

        var symbol = resolved.Symbol;

        // From every target-framework flavor of the member (see SymbolFlavors).
        var overrides = await SymbolFlavors.SearchAllAsync(symbol, solution,
            flavor => SymbolFinder.FindOverridesAsync(flavor, solution, cancellationToken: ct), ct);

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

        // A multi-targeted project declares each override once per target framework.
        return Serialize(Paging.Apply(
            all.OrderBy(o => o.File, StringComparer.Ordinal).ThenBy(o => o.Line), maxResults, o => (o.ContainingType, o.Method, o.File, o.Line), offset ?? 0));
    }

    private static string Serialize(Paging.Page<OverrideInfo> page) =>
        Paging.Fit(page, p => ToolJson.Serialize(new OverridesResult(
            p.Items, p.Count, p.TotalFound, p.Truncated, p.Limit, p.Offset, p.NextOffset)));
}
