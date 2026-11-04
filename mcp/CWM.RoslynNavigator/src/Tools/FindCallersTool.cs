using System.ComponentModel;
using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using ModelContextProtocol.Server;

namespace CWM.RoslynNavigator.Tools;

[McpServerToolType]
public static class FindCallersTool
{
    [McpServerTool(Name = "find_callers"), Description("Methods that call a method, including calls through an interface or base member it implements or overrides (Via: direct, interface, base, implementation; ViaSymbol is the member called). For what a method calls, use get_dependency_graph.")]
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
            return Serialize(Paging.Empty<CallerInfo>(maxResults));

        // A className hint is just a qualifier — fold it in rather than resolving twice.
        var lookupName = SymbolResolver.Qualify(methodName, className);

        var resolved = await SymbolResolver.ResolveOrErrorAsync(workspace, lookupName, ct: ct);
        if (resolved.Failed) return resolved.Error;

        var symbol = resolved.Symbol;

        // From every target-framework flavor of the method (see SymbolFlavors).
        var callers = await SymbolFlavors.SearchAllAsync(symbol, solution,
            flavor => SymbolFinder.FindCallersAsync(flavor, solution, ct), ct);

        var generated = new GeneratedCodeIndex(workspace);
        var all = new List<CallerInfo>();

        // Indirect callers bind to a related member (the interface method this one implements,
        // the base method it overrides, ...) — in DI code that is most of them. Keep them,
        // labelled, and list direct calls first. One entry per caller and route.
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var caller in callers.OrderBy(c => c.IsDirect ? 0 : 1))
        {
            var (via, viaSymbol) = CallerDispatch.Classify(symbol, caller);
            var key = $"{caller.CallingSymbol.ToDisplayString()}|{via}|{viaSymbol}";
            if (!seen.Add(key)) continue;

            var location = SymbolResolver.GetLocation(caller.CallingSymbol);
            if (location.HasValue)
            {
                all.Add(new CallerInfo(
                    Method: caller.CallingSymbol.Name,
                    ContainingType: caller.CallingSymbol.ContainingType?.Name ?? "unknown",
                    File: workspace.ToRelativePath(location.Value.File),
                    Line: location.Value.Line,
                    IsGenerated: generated.IsGenerated(caller.CallingSymbol, ct),
                    Via: via,
                    ViaSymbol: viaSymbol));
            }
        }

        // Callers are already one per symbol and route; this also folds per-framework copies of
        // a caller declared in a multi-targeted project whose display strings differ.
        // Direct callers first, then a stable file/line order so an offset names the same entry every call.
        var ordered = all
            .OrderBy(c => c.Via == "direct" ? 0 : 1)
            .ThenBy(c => c.File, StringComparer.Ordinal)
            .ThenBy(c => c.Line);
        return Serialize(Paging.Apply(ordered, maxResults, c => (c.File, c.Line, c.Method, c.Via, c.ViaSymbol), offset ?? 0));
    }

    private static string Serialize(Paging.Page<CallerInfo> page) =>
        Paging.Fit(page, p => ToolJson.Serialize(new CallersResult(
            p.Items, p.Count, p.TotalFound, p.Truncated, p.Limit, p.Offset, p.NextOffset)));
}
