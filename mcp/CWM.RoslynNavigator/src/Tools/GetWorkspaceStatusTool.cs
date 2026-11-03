using System.ComponentModel;
using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using ModelContextProtocol.Server;

namespace CWM.RoslynNavigator.Tools;

[McpServerToolType]
public static class GetWorkspaceStatusTool
{
    [McpServerTool(Name = "get_workspace_status"), Description("Report what the server has loaded and how completely: workspace state, loaded solutions, project and document counts, background compilation warm-up progress (Compiled of Total), snapshot version, and every project that loaded or compiled incompletely (load failures, no documents, unresolved core library, compilation errors thrown, missing analyzers/generators) plus raw workspace load diagnostics. Healthy=false means answers from other tools may be missing results. Call it when a result looks suspiciously empty, or before trusting dead-code or reference counts on an unfamiliar solution. Works while loading.")]
    public static async Task<string> ExecuteAsync(
        WorkspaceManager workspace,
        [Description("Maximum entries in each of IncompleteProjects and LoadDiagnostics")] int maxResults = 50,
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

        var healthy = workspace.State == WorkspaceState.Ready
            && problems.Count == 0
            && !diagnostics.Any(d => d.Kind == "failure");

        return JsonSerializer.Serialize(new WorkspaceStatusResult(
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
            Limit: problemPage.Limit));
    }
}
