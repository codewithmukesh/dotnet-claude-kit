using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tools;

namespace CWM.RoslynNavigator.Tests.Scale;

/// <summary>
/// The paging contract on a solution large enough to need it: every page fits the response
/// budget, pages chained through NextOffset cover TotalFound exactly once, and the grouped
/// counts (Files, ByProject, ByLifetime) add up to the totals they summarise. Returns failures
/// rather than asserting, so one scale run reports every broken tool at once.
/// </summary>
internal static class ResponseContract
{
    public static async Task<List<string>> CheckAsync(WorkspaceManager workspace, int projects, CancellationToken ct)
    {
        var failures = new List<string>();

        // find_references: ~26 references per project, so many pages.
        await WalkAsync<ReferencesResult>(
            "find_references", failures,
            offset => FindReferencesTool.ExecuteAsync(workspace, "IWidgetHandler", offset: offset, ct: ct),
            r => (r.Count, r.TotalFound, r.Offset, r.NextOffset, r.References?.Count ?? r.Files!.Sum(f => f.Entries.Count)),
            r => r.ByProject is { } byProject && byProject.Sum(p => p.Count) != r.TotalFound
                ? $"ByProject sums to {byProject.Sum(p => p.Count)}, TotalFound {r.TotalFound}"
                : null);

        // get_endpoint_map: two endpoints per controller, found only through WidgetControllerBase.
        var routes = new HashSet<string>(StringComparer.Ordinal);
        var endpointTotal = await WalkAsync<EndpointMapResult>(
            "get_endpoint_map", failures,
            offset => GetEndpointMapTool.ExecuteAsync(workspace, offset: offset, ct: ct),
            r =>
            {
                foreach (var entry in r.Files?.SelectMany(f => f.Entries) ?? r.Endpoints!.Select(e => $"{e.Line} {e.Method} {e.Route}"))
                    routes.Add(entry);
                return (r.Count, r.TotalFound, r.Offset, r.NextOffset, r.Endpoints?.Count ?? r.Files!.Sum(f => f.Entries.Count));
            });
        if (endpointTotal != 2 * projects)
            failures.Add($"get_endpoint_map: TotalFound {endpointTotal}, expected {2 * projects}");
        if (!routes.Any(r => r.Contains(" GET Widget001/{id}", StringComparison.Ordinal)))
            failures.Add("get_endpoint_map: no 'GET Widget001/{id}' (base controller route not inherited)");

        // get_di_registrations: two per project.
        var registrations = await WalkAsync<DiRegistrationsResult>(
            "get_di_registrations", failures,
            offset => GetDiRegistrationsTool.ExecuteAsync(workspace, offset: offset, ct: ct),
            r => (r.Count, r.TotalFound, r.Offset, r.NextOffset, r.Registrations?.Count ?? r.Files!.Sum(f => f.Entries.Count)),
            r => r.ByLifetime!.Sum(l => l.Count) != r.TotalFound ? "ByLifetime does not add up to TotalFound" : null);
        if (registrations != 2 * projects)
            failures.Add($"get_di_registrations: TotalFound {registrations}, expected {2 * projects}");

        // get_project_graph: one line or entry per project, Shared included.
        var graph = await WalkAsync<ProjectGraphResult>(
            "get_project_graph", failures,
            offset => GetProjectGraphTool.ExecuteAsync(workspace, offset: offset, ct: ct),
            r => (r.Count, r.TotalFound, r.Offset, r.NextOffset, r.Projects?.Count ?? r.Edges!.Count));
        if (graph != projects + 1)
            failures.Add($"get_project_graph: TotalFound {graph}, expected {projects + 1}");

        return failures;
    }

    /// <summary>
    /// Follows NextOffset from 0. Checks each page's size, that it starts where the previous
    /// one said, that its entries match Count, and <paramref name="extra"/>; returns TotalFound.
    /// </summary>
    private static async Task<int> WalkAsync<T>(
        string tool,
        List<string> failures,
        Func<int?, Task<string>> call,
        Func<T, (int Count, int TotalFound, int Offset, int? NextOffset, int Entries)> describe,
        Func<T, string?>? extra = null)
    {
        int? offset = 0;
        int covered = 0, total = 0, pages = 0;

        while (offset is { } expected && pages++ < 1_000)
        {
            var json = await call(expected);
            if (json.Length > ResponseBudget.MaxChars)
                failures.Add($"{tool}: page at offset {expected} is {json.Length} chars");

            var page = JsonSerializer.Deserialize<T>(json)!;
            var (count, totalFound, pageOffset, next, entries) = describe(page);
            total = totalFound;

            if (pageOffset != expected)
                failures.Add($"{tool}: asked for offset {expected}, page reports {pageOffset}");
            if (entries != count)
                failures.Add($"{tool}: page at {expected} lists {entries} entries but Count is {count}");
            if (next is { } n && n != expected + count)
                failures.Add($"{tool}: NextOffset {n} after offset {expected} + Count {count}");
            if (count == 0 && next is not null)
                failures.Add($"{tool}: empty page at {expected} still has NextOffset");
            if (extra?.Invoke(page) is { } problem)
                failures.Add($"{tool}: {problem}");

            covered += count;
            offset = next;
        }

        if (covered != total)
            failures.Add($"{tool}: pages covered {covered} entries, TotalFound {total}");

        return total;
    }
}
