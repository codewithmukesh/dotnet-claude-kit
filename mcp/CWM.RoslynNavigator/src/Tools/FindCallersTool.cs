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
    [McpServerTool(Name = "find_callers"), Description("Find all methods that call a specific method. Useful for impact analysis and understanding dependencies. Includes callers that reach it through dispatch: Via is 'direct', 'interface' (called through an interface member it implements — the usual DI case), 'base' (through a base member it overrides), or 'implementation' (through an implementation/override of it), with ViaSymbol naming the member actually called. Each caller carries IsGenerated so generated call sites can be skipped.")]
    public static async Task<string> ExecuteAsync(
        WorkspaceManager workspace,
        [Description("Method to find callers for. Bare, type-qualified ('OrderService.CreateAsync'), or fully qualified. Qualify when the name is reused across types. Also accepts a SymbolId from find_symbol/get_symbol_at, which names exactly one symbol (overload included).")] string methodName,
        [Description("Optional: containing class name to disambiguate")] string? className = null,
        [Description("Maximum results to return. TotalFound in the response reports the full count; re-query with a higher value if it exceeds Count.")] int maxResults = 50,
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

        var callers = await SymbolFinder.FindCallersAsync(symbol, solution, ct);

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

        return Serialize(Paging.Apply(all, maxResults));
    }

    private static string Serialize(Paging.Page<CallerInfo> page) =>
        JsonSerializer.Serialize(new CallersResult(
            page.Items, page.Count, page.TotalFound, page.Truncated, page.Limit));
}
