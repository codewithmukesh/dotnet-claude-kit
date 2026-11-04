using System.ComponentModel;
using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;

namespace CWM.RoslynNavigator.Tools;

[McpServerToolType]
public static class FindSymbolTool
{
    [McpServerTool(Name = "find_symbol"), Description("Locate where a type or member is declared: file, line, kind, namespace, containing type, and a SymbolId for other tools. Exact name by default; set match for partial names.")]
    public static async Task<string> ExecuteAsync(
        WorkspaceManager workspace,
        [Description("Name, Type.Member, fully qualified name, or SymbolId. With a fuzzy match only the last segment is fuzzy.")] string name,
        [Description("any, type, class, interface, struct, enum, record, method, property, or field")] string kind = "any",
        int maxResults = 50,
        int? offset = null,
        [Description("exact (case-sensitive), prefix, camelhump (GBIA finds GetByIdAsync), or substring. Fuzzy modes ignore case and rank tighter matches first.")] string match = "exact",
        CancellationToken ct = default)
    {
        var notReady = await workspace.EnsureReadyOrStatusAsync(ct);
        if (notReady is not null) return notReady;

        if (!SymbolMatcher.TryParseMode(match, out var mode))
        {
            return ToolJson.Serialize(new ErrorResponse(ErrorCodes.InvalidArgument,
                $"Unknown match '{match}'. Use 'exact', 'prefix', 'camelhump', or 'substring'."));
        }

        var generated = new GeneratedCodeIndex(workspace);
        var flavors = new TargetFrameworkFlavors(workspace.GetSolution()!);

        IEnumerable<(SymbolLocation, ProjectId?)> found;
        if (mode == MatchMode.Exact)
        {
            var symbols = await SymbolResolver.FindSymbolsByNameAsync(workspace, name, kind, ct);
            found = symbols.Select(s => (ToLocation(workspace, generated, s, null, ct), flavors.ProjectOf(s)));
        }
        else
        {
            var ranked = await SymbolResolver.SearchAsync(workspace, name, mode, kind, ct);
            found = ranked.Select(r => (ToLocation(workspace, generated, r.Symbol, TierName(r.Tier), ct), flavors.ProjectOf(r.Symbol)));
        }

        // A multi-targeted project declares each symbol once per target framework.
        var all = flavors.Merge(
            found,
            s => (s.File, s.Line, s.Kind, s.Name, s.ContainingType),
            (s, tfms) => s with { TargetFrameworks = tfms });

        var page = Paging.Apply(all, maxResults, offset ?? 0);

        return Paging.Fit(page, p => ToolJson.Serialize(new SymbolSearchResult(
            p.Items, p.Count, p.TotalFound, p.Truncated, p.Limit, p.Offset, p.NextOffset)));
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
            ContainingType: symbol.ContainingType?.Name,
            SymbolId: SymbolResolver.GetSymbolId(symbol));
    }

    private static string TierName(MatchTier tier) => tier switch
    {
        MatchTier.Exact => "exact",
        MatchTier.Prefix => "prefix",
        MatchTier.CamelHump => "camelhump",
        _ => "substring"
    };
}
