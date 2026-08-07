using System.ComponentModel;
using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;

namespace CWM.RoslynNavigator.Tools;

[McpServerToolType]
public static class GetDiagnosticsTool
{
    [McpServerTool(Name = "get_diagnostics"), Description("Get compiler diagnostics (errors, warnings) scoped to a file, project, or the entire solution. Does not run NuGet analyzers. Results are ordered errors-first; per-severity totals are always reported.")]
    public static async Task<string> ExecuteAsync(
        WorkspaceManager workspace,
        [Description("Scope: 'file', 'project', or 'solution'")] string scope = "solution",
        [Description("File or project path (required for 'file' and 'project' scopes)")] string? path = null,
        [Description("Severity filter: 'error', 'warning', or 'all' (info and above; hidden diagnostics are always excluded)")] string severityFilter = "all",
        [Description("Maximum diagnostics to return. TotalFound and the per-severity counts in the response report the full picture; re-query with a higher value if it exceeds Count.")] int maxResults = 100,
        CancellationToken ct = default)
    {
        var notReady = await workspace.EnsureReadyOrStatusAsync(ct);
        if (notReady is not null) return notReady;

        var solution = workspace.GetSolution();
        if (solution is null)
            return JsonSerializer.Serialize(new DiagnosticsResult(
                [], 0, 0, 0, 0, 0, false, Math.Max(1, maxResults)));

        var matched = new List<(Diagnostic Diagnostic, DiagnosticInfo Info)>();

        var compilations = scope.ToLowerInvariant() switch
        {
            "file" => await GetCompilationsForFile(workspace, solution, path, ct),
            "project" => await GetCompilationsForProject(workspace, solution, path, ct),
            _ => await workspace.GetAllCompilationsAsync(ct)
        };

        foreach (var compilation in compilations)
        {
            var diags = compilation.GetDiagnostics(ct);

            foreach (var diag in diags)
            {
                if (!MatchesSeverityFilter(diag.Severity, severityFilter))
                    continue;

                if (scope == "file" && path is not null)
                {
                    var diagPath = diag.Location.GetLineSpan().Path;
                    if (diagPath is null || !diagPath.EndsWith(path, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                var lineSpan = diag.Location.GetLineSpan();
                matched.Add((diag, new DiagnosticInfo(
                    Id: diag.Id,
                    Severity: diag.Severity.ToString().ToLowerInvariant(),
                    Message: diag.GetMessage(),
                    File: lineSpan.Path is { } p ? workspace.ToRelativePath(p) : "unknown",
                    Line: lineSpan.StartLinePosition.Line + 1)));
            }
        }

        var errors = matched.Count(m => m.Diagnostic.Severity == DiagnosticSeverity.Error);
        var warnings = matched.Count(m => m.Diagnostic.Severity == DiagnosticSeverity.Warning);
        var info = matched.Count(m => m.Diagnostic.Severity == DiagnosticSeverity.Info);

        // Errors first so truncation never hides the most important diagnostics
        var ordered = matched
            .OrderByDescending(m => m.Diagnostic.Severity)
            .ThenBy(m => m.Info.File, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Info.Line)
            .Select(m => m.Info)
            .ToList();

        var page = Paging.Apply(ordered, maxResults);

        return JsonSerializer.Serialize(new DiagnosticsResult(
            page.Items, page.Count, page.TotalFound, errors, warnings, info, page.Truncated, page.Limit));
    }

    private static async Task<IReadOnlyList<Compilation>> GetCompilationsForFile(
        WorkspaceManager workspace, Solution solution, string? path, CancellationToken ct)
    {
        if (path is null)
            return await workspace.GetAllCompilationsAsync(ct);

        var document = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.FilePath?.EndsWith(path, StringComparison.OrdinalIgnoreCase) == true);

        if (document is null)
            return [];

        var compilation = await workspace.GetCompilationAsync(document.Project.Id, ct);
        return compilation is not null ? [compilation] : [];
    }

    private static async Task<IReadOnlyList<Compilation>> GetCompilationsForProject(
        WorkspaceManager workspace, Solution solution, string? path, CancellationToken ct)
    {
        if (path is null)
            return await workspace.GetAllCompilationsAsync(ct);

        var project = solution.Projects
            .FirstOrDefault(p =>
                p.Name.Equals(path, StringComparison.OrdinalIgnoreCase) ||
                (p.FilePath?.EndsWith(path, StringComparison.OrdinalIgnoreCase) == true));

        if (project is null)
            return [];

        var compilation = await workspace.GetCompilationAsync(project.Id, ct);
        return compilation is not null ? [compilation] : [];
    }

    private static bool MatchesSeverityFilter(DiagnosticSeverity severity, string filter)
    {
        return filter.ToLowerInvariant() switch
        {
            "error" => severity == DiagnosticSeverity.Error,
            "warning" => severity >= DiagnosticSeverity.Warning,
            // 'all' means info and above — hidden diagnostics are compiler bookkeeping
            // (unnecessary usings, etc. surfaced by IDEs) and would flood the response.
            _ => severity >= DiagnosticSeverity.Info
        };
    }
}
