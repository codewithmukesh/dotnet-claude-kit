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
    [McpServerTool(Name = "find_callers"), Description("Find all methods that call a specific method. Useful for impact analysis and understanding dependencies. Each caller carries IsGenerated so generated call sites can be skipped.")]
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
            return Serialize(Paging.Empty<CallerInfo>(maxResults));

        // A className hint is just a qualifier — fold it in rather than resolving twice.
        var lookupName = className is not null && !methodName.Contains('.')
            ? $"{className}.{methodName}"
            : methodName;

        var resolved = await SymbolResolver.ResolveOrErrorAsync(workspace, lookupName, ct: ct);
        if (resolved.Failed) return resolved.Error;

        var symbol = resolved.Symbol;

        var callers = await SymbolFinder.FindCallersAsync(symbol, solution, ct);

        var generated = new GeneratedCodeIndex(workspace);
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
                    Line: location.Value.Line,
                    IsGenerated: generated.IsGenerated(caller.CallingSymbol, ct)));
            }
        }

        return Serialize(Paging.Apply(all, maxResults));
    }

    private static string Serialize(Paging.Page<CallerInfo> page) =>
        JsonSerializer.Serialize(new CallersResult(
            page.Items, page.Count, page.TotalFound, page.Truncated, page.Limit));
}
