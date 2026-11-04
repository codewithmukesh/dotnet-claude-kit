using System.ComponentModel;
using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using ModelContextProtocol.Server;

namespace CWM.RoslynNavigator.Tools;

[McpServerToolType]
public static class GetWorkspaceStatusTool
{
    [McpServerTool(Name = "get_workspace_status"), Description("What is loaded and how healthy: solutions, project and document counts, warm-up progress, reload state, and projects that loaded incompletely. Healthy=false means other tools may miss results. Call when a result looks suspiciously empty. Works while loading.")]
    public static async Task<string> ExecuteAsync(
        WorkspaceManager workspace,
        [Description("Cap per list")] int maxResults = 50,
        CancellationToken ct = default)
    {
        // Triggers roots discovery / error retry like every tool, but a not-ready workspace is
        // part of the answer here rather than a reason to stop.
        await workspace.EnsureReadyOrStatusAsync(ct);

        var solution = workspace.GetSolution();
        var problems = workspace.GetProjectProblems();
        var diagnostics = workspace.Health.GetDiagnostics(solution);

        var problemPage = Paging.Apply(
            problems
                .Select(p => new ProjectHealthInfo(
                    p.Name,
                    p.Path is { } path ? workspace.ToRelativePath(path) : "unknown",
                    p.Documents,
                    [.. p.Issues]))
                .ToList(),
            maxResults);

        var diagnosticPage = Paging.Apply(
            diagnostics
                .OrderBy(d => d.Kind == "failure" ? 0 : 1)
                .Select(d => new LoadDiagnosticInfo(
                    d.Kind,
                    d.Message,
                    d.ProjectPath is { } path ? workspace.ToRelativePath(path) : null))
                .ToList(),
            maxResults);

        var solutions = workspace.LoadedSolutions
            .Select(s => new SolutionSummary(s.Name, workspace.ToRelativePath(s.Path), s.ProjectPaths.Count, s.Error))
            .ToList();

        // A failed reload leaves answers coming from a snapshot that no longer matches disk.
        var lastReloadError = workspace.LastReloadError;
        var healthy = workspace.State == WorkspaceState.Ready
            && lastReloadError is null
            && problems.Count == 0
            && !diagnostics.Any(d => d.Kind == "failure");

        return ToolJson.Serialize(new WorkspaceStatusResult(
            State: workspace.State.ToString(),
            Message: workspace.GetStatusMessage(),
            Healthy: healthy,
            SnapshotVersion: workspace.SnapshotVersion,
            LoadCount: workspace.LoadCount,
            Solutions: solutions,
            ProjectCount: solution?.ProjectIds.Count ?? 0,
            DocumentCount: solution?.Projects.Sum(p => p.DocumentIds.Count) ?? 0,
            WarmUp: workspace.Health.GetWarmUp(),
            IncompleteProjects: problemPage.Items,
            Count: problemPage.Count,
            TotalFound: problemPage.TotalFound,
            LoadDiagnostics: diagnosticPage.Items,
            LoadDiagnosticCount: diagnosticPage.TotalFound,
            Truncated: problemPage.Truncated || diagnosticPage.Truncated,
            Limit: problemPage.Limit,
            Reloading: workspace.IsReloading,
            ReloadReason: workspace.ReloadReason,
            LastReloadError: lastReloadError));
    }
}
