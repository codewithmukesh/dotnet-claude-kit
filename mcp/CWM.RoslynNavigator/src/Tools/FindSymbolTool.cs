using System.ComponentModel;
using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;

namespace CWM.RoslynNavigator.Tools;

[McpServerToolType]
public static class FindSymbolTool
{
    [McpServerTool(Name = "find_symbol"), Description("Find where a type, method, or property is defined in the solution. Returns file path, line number, namespace, containing type, and an IsGenerated flag marking source-generator or tooling output. Exact name by default; set match to 'prefix', 'camelhump' ('GBIA' or 'GetByAsync' finds GetByIdAsync), or 'substring' when you only know part of the name — fuzzy results are ranked exact > prefix > camelhump > substring, types before members, and each carries its Match tier.")]
    public static async Task<string> ExecuteAsync(
        WorkspaceManager workspace,
        [Description("Symbol name. Bare ('CreateAsync'), type-qualified ('OrderService.CreateAsync'), or fully qualified ('MyApp.Orders.OrderService.CreateAsync'). Qualifying narrows results when a name is reused across types; with a fuzzy match the qualifier still matches exactly and only the last segment is fuzzy.")] string name,
        [Description("Filter by kind: 'type', 'class', 'interface', 'struct', 'enum', 'record', 'method', 'property', 'field', or 'any'")] string kind = "any",
        [Description("Maximum results to return. TotalFound in the response reports the full count; re-query with a higher value if it exceeds Count.")] int maxResults = 50,
        [Description("Name matching: 'exact' (default, case-sensitive), 'prefix', 'camelhump', or 'substring' (case-insensitive). Each looser mode includes the tighter ones.")] string match = "exact",
        CancellationToken ct = default)
    {
        var notReady = await workspace.EnsureReadyOrStatusAsync(ct);
        if (notReady is not null) return notReady;

        if (!SymbolMatcher.TryParseMode(match, out var mode))
        {
            return JsonSerializer.Serialize(new ErrorResponse(ErrorCodes.InvalidArgument,
                $"Unknown match '{match}'. Use 'exact', 'prefix', 'camelhump', or 'substring'."));
        }

        var generated = new GeneratedCodeIndex(workspace);

        List<SymbolLocation> all;
        if (mode == MatchMode.Exact)
        {
            var symbols = await SymbolResolver.FindSymbolsByNameAsync(workspace, name, kind, ct);
            all = [.. symbols.Select(s => ToLocation(workspace, generated, s, null, ct))];
        }
        else
        {
            var ranked = await SymbolResolver.SearchAsync(workspace, name, mode, kind, ct);
            all = [.. ranked.Select(r => ToLocation(workspace, generated, r.Symbol, TierName(r.Tier), ct))];
        }

        var page = Paging.Apply(all, maxResults);

        return JsonSerializer.Serialize(new SymbolSearchResult(
            page.Items, page.Count, page.TotalFound, page.Truncated, page.Limit));
    }

    private static SymbolLocation ToLocation(
        WorkspaceManager workspace, GeneratedCodeIndex generated, ISymbol symbol, string? match, CancellationToken ct)
    {
        var location = SymbolResolver.GetLocation(symbol);
        return new SymbolLocation(
            Name: symbol.Name,
            Kind: SymbolResolver.GetKindString(symbol),
            File: location is { } loc ? workspace.ToRelativePath(loc.File) : "unknown",
            Line: location?.Line ?? 0,
            Namespace: symbol.ContainingNamespace?.ToDisplayString() ?? "global",
            IsGenerated: generated.IsGenerated(symbol, ct),
            Match: match,
            ContainingType: symbol.ContainingType?.Name);
    }

    private static string TierName(MatchTier tier) => tier switch
    {
        MatchTier.Exact => "exact",
        MatchTier.Prefix => "prefix",
        MatchTier.CamelHump => "camelhump",
        _ => "substring"
    };
}
