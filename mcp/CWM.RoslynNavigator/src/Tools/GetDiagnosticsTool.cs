using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ModelContextProtocol.Server;

namespace CWM.RoslynNavigator.Tools;

[McpServerToolType]
public static class GetDiagnosticsTool
{
    [McpServerTool(Name = "get_diagnostics"), Description("Compiler errors and warnings for a file, project, or the solution, errors first, with per-severity totals. NuGet analyzers are not run.")]
    public static async Task<string> ExecuteAsync(
        WorkspaceManager workspace,
        [Description("file, project, or solution")] string scope = "solution",
        [Description("File or project; required unless scope is solution")] string? path = null,
        [Description("error, warning, or all")] string severityFilter = "all",
        int maxResults = 100,
        int offset = 0,
        CancellationToken ct = default)
    {
        var notReady = await workspace.EnsureReadyOrStatusAsync(ct);
        if (notReady is not null) return notReady;

        var invalid = ArgumentChecks.Choice("scope", scope, "file", "project", "solution")
            ?? ArgumentChecks.Choice("severityFilter", severityFilter, "error", "warning", "all");
        if (invalid is not null) return invalid;

        var solution = workspace.GetSolution();
        if (solution is null)
            return ToolJson.Serialize(new DiagnosticsResult(
                [], 0, 0, 0, 0, 0, false, Math.Max(1, maxResults)));

        HashSet<string>? fileScopePaths = null;

        if (scope.Equals("file", StringComparison.OrdinalIgnoreCase) && path is not null)
        {
            var documents = DocumentResolver.FindDocuments(solution, path);
            if (documents.Count == 0)
                return ToolJson.Serialize(new ErrorResponse(ErrorCodes.FileNotFound,
                    $"File '{path}' is not part of any project in the solution."));

            fileScopePaths = documents.Select(d => d.FilePath!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        if (scope.Equals("project", StringComparison.OrdinalIgnoreCase)
            && ArgumentChecks.UnknownProject(solution, path, p =>
                p.Name.Equals(path, StringComparison.OrdinalIgnoreCase)
                || p.FilePath?.EndsWith(path!, StringComparison.OrdinalIgnoreCase) == true) is { } unknownProject)
            return unknownProject;

        var matched = new List<(Diagnostic Diagnostic, DiagnosticInfo Info)>();

        var diagnostics = scope.ToLowerInvariant() switch
        {
            "file" when path is not null => await GetFileDiagnosticsAsync(workspace, solution, path, ct),
            "file" => AllOf(await workspace.GetAllCompilationsAsync(ct), ct),
            "project" => AllOf(await GetCompilationsForProject(workspace, solution, path, ct), ct),
            _ => AllOf(await workspace.GetAllCompilationsAsync(ct), ct)
        };

        foreach (var diag in diagnostics)
        {
            if (!MatchesSeverityFilter(diag.Severity, severityFilter))
                continue;

            if (fileScopePaths is not null)
            {
                var diagPath = diag.Location.SourceTree?.FilePath;
                if (diagPath is null || !fileScopePaths.Contains(diagPath))
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
        // A file compiled once per target framework reports the same diagnostic once per
        // framework; count it once.
        matched = Paging.Distinct(matched, m => (m.Info.Id, m.Info.File, m.Info.Line, m.Info.Message));

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

        var page = Paging.Apply(ordered, maxResults, offset);

        return Paging.Fit(page, p => ToolJson.Serialize(new DiagnosticsResult(
            p.Items, p.Count, p.TotalFound, errors, warnings, info, p.Truncated, p.Limit, p.Offset, p.NextOffset)));
    }

    private static readonly ConditionalWeakTable<Compilation, IReadOnlyList<Diagnostic>> CompilationDiagnostics = new();

    /// <summary>
    /// A compilation's full diagnostics, computed once per compilation instance. A compilation
    /// never changes (an edit produces a new one), so a repeated question about the same
    /// snapshot is answered without compiling every method body again.
    /// </summary>
    private static IReadOnlyList<Diagnostic> DiagnosticsOf(Compilation compilation, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (CompilationDiagnostics.TryGetValue(compilation, out var cached))
            return cached;

        var computed = compilation.GetDiagnostics(ct);
        return CompilationDiagnostics.GetValue(compilation, _ => computed);
    }

    private static IEnumerable<Diagnostic> AllOf(IReadOnlyList<Compilation> compilations, CancellationToken ct) =>
        compilations.SelectMany(c => DiagnosticsOf(c, ct));

    /// <summary>
    /// Diagnostics for every document matching <paramref name="path"/>. A file's own semantic
    /// model reports its syntax, declaration and method-body diagnostics without compiling the
    /// rest of its project — identical to the project's diagnostics for that file except for
    /// the few the compiler can only report once the whole compilation is done (see
    /// <see cref="NeedsCompilationDiagnostics"/>); those files use the compilation's.
    /// </summary>
    private static async Task<List<Diagnostic>> GetFileDiagnosticsAsync(
        WorkspaceManager workspace, Solution solution, string path, CancellationToken ct)
    {
        var diagnostics = new List<Diagnostic>();

        // Every matched file's project, once each: a bare name like 'Program.cs' can live in several.
        foreach (var document in DocumentResolver.FindDocuments(solution, path))
        {
            var compilation = await workspace.GetCompilationAsync(document.Project.Id, ct);
            if (compilation is null || await document.GetSyntaxTreeAsync(ct) is not { } tree)
                continue;

            if (NeedsCompilationDiagnostics(await tree.GetRootAsync(ct)))
            {
                diagnostics.AddRange(DiagnosticsOf(compilation, ct).Where(d => d.Location.SourceTree == tree));
                continue;
            }

            diagnostics.AddRange(compilation.GetSemanticModel(tree).GetDiagnostics(cancellationToken: ct));
        }

        return diagnostics;
    }

    /// <summary>
    /// Whether a file can carry a diagnostic only the finished compilation reports: unused or
    /// unassigned fields and events (CS0169, CS0414, CS0649, CS0067 — whether a field is ever
    /// read depends on every method body), or a duplicate entry point (CS0017). Only
    /// non-public, non-protected field and event declarations, Main methods, and top-level
    /// statements qualify; checked on syntax alone.
    /// </summary>
    internal static bool NeedsCompilationDiagnostics(SyntaxNode root) =>
        root.DescendantNodes(n => n is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax or TypeDeclarationSyntax)
            .Any(node => node switch
            {
                GlobalStatementSyntax => true,
                MethodDeclarationSyntax method => method.Identifier.ValueText == "Main",
                BaseFieldDeclarationSyntax field => !field.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword) || m.IsKind(SyntaxKind.ProtectedKeyword))
                    && !field.Modifiers.Any(m => m.IsKind(SyntaxKind.ConstKeyword)),
                _ => false
            });
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
