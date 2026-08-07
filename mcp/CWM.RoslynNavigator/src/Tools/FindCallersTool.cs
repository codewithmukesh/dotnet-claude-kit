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
    [McpServerTool(Name = "find_callers"), Description("Find all methods that call a specific method. Useful for impact analysis and understanding dependencies.")]
    public static async Task<string> ExecuteAsync(
        WorkspaceManager workspace,
        [Description("Method to find callers for. Bare, type-qualified ('OrderService.CreateAsync'), or fully qualified. Qualify when the name is reused across types.")] string methodName,
        [Description("Optional: containing class name to disambiguate")] string? className = null,
        [Description("Maximum results to return. TotalFound in the response reports the full count; re-query with a higher value if it exceeds Count.")] int maxResults = 50,
        CancellationToken ct = default)
    {
        var notReady = await workspace.EnsureReadyOrStatusAsync(ct);
        if (notReady is not null) return notReady;

        var solution = workspace.GetSolution();
        if (solution is null)
            return JsonSerializer.Serialize(new CallersResult([], 0, 0));

        var symbol = await SymbolResolver.ResolveSymbolAsync(workspace, methodName, ct: ct);

        // If className is provided and the resolved symbol isn't in that type, try to find a better match
        if (symbol is not null && className is not null && symbol.ContainingType?.Name != className)
        {
            var allSymbols = await SymbolResolver.FindSymbolsByNameAsync(workspace, methodName, ct: ct);
            symbol = allSymbols.FirstOrDefault(s => s.ContainingType?.Name == className) ?? symbol;
        }

        if (symbol is null)
            return JsonSerializer.Serialize(new CallersResult([], 0, 0));

        var callers = await SymbolFinder.FindCallersAsync(symbol, solution, ct);

        var all = new List<CallerInfo>();
        foreach (var caller in callers)
        {
            if (!caller.IsDirect) continue;

            var location = SymbolResolver.GetLocation(caller.CallingSymbol);
            if (location.HasValue)
            {
                all.Add(new CallerInfo(
                    Method: caller.CallingSymbol.Name,
                    ContainingType: caller.CallingSymbol.ContainingType?.Name ?? "unknown",
                    File: workspace.ToRelativePath(location.Value.File),
                    Line: location.Value.Line));
            }
        }

        var results = all.Take(Math.Max(1, maxResults)).ToList();

        return JsonSerializer.Serialize(new CallersResult(results, results.Count, all.Count));
    }
}
