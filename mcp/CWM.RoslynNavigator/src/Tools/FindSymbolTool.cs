using System.ComponentModel;
using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using ModelContextProtocol.Server;

namespace CWM.RoslynNavigator.Tools;

[McpServerToolType]
public static class FindSymbolTool
{
    [McpServerTool(Name = "find_symbol"), Description("Find where a type, method, or property is defined in the solution. Returns file path, line number, namespace, and an IsGenerated flag marking source-generator or tooling output.")]
    public static async Task<string> ExecuteAsync(
        WorkspaceManager workspace,
        [Description("Symbol name. Bare ('CreateAsync'), type-qualified ('OrderService.CreateAsync'), or fully qualified ('MyApp.Orders.OrderService.CreateAsync'). Qualifying narrows results when a name is reused across types.")] string name,
        [Description("Filter by kind: 'type', 'class', 'interface', 'struct', 'enum', 'record', 'method', 'property', 'field', or 'any'")] string kind = "any",
        [Description("Maximum results to return. TotalFound in the response reports the full count; re-query with a higher value if it exceeds Count.")] int maxResults = 50,
        CancellationToken ct = default)
    {
        var notReady = await workspace.EnsureReadyOrStatusAsync(ct);
        if (notReady is not null) return notReady;

        var symbols = await SymbolResolver.FindSymbolsByNameAsync(workspace, name, kind, ct);

        var generated = new GeneratedCodeIndex(workspace);

        var all = symbols
            .Select(s =>
            {
                var location = SymbolResolver.GetLocation(s);
                return new SymbolLocation(
                    Name: s.Name,
                    Kind: SymbolResolver.GetKindString(s),
                    File: location is { } loc ? workspace.ToRelativePath(loc.File) : "unknown",
                    Line: location?.Line ?? 0,
                    Namespace: s.ContainingNamespace?.ToDisplayString() ?? "global",
                    IsGenerated: generated.IsGenerated(s, ct));
            }).ToList();

        var page = Paging.Apply(all, maxResults);

        return JsonSerializer.Serialize(new SymbolSearchResult(
            page.Items, page.Count, page.TotalFound, page.Truncated, page.Limit));
    }
}
