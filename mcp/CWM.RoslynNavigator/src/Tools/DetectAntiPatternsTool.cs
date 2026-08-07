using System.ComponentModel;
using System.Text.Json;
using CWM.RoslynNavigator.Analyzers;
using CWM.RoslynNavigator.Responses;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;

namespace CWM.RoslynNavigator.Tools;

[McpServerToolType]
public static class DetectAntiPatternsTool
{
    private static readonly IAntiPatternDetector[] Detectors =
    [
        // Syntax detectors (fast, no compilation needed)
        new AsyncVoidDetector(),
        new HttpClientInstantiationDetector(),
        new DateTimeDirectUseDetector(),
        new BroadCatchDetector(),
        new PragmaWithoutRestoreDetector(),
        // Semantic detectors (require SemanticModel)
        new SyncOverAsyncDetector(),
        new LoggingInterpolationDetector(),
        new MissingCancellationTokenDetector(),
        new EfCoreNoTrackingDetector()
    ];

    [McpServerTool(Name = "detect_antipatterns"), Description("Detect .NET anti-patterns in source code using Roslyn analysis. Finds async void, sync-over-async, new HttpClient(), DateTime.Now, broad catch, logging interpolation, missing pragma restore, missing CancellationToken, and EF Core queries without AsNoTracking. Generated code is never reported; test and migration code is excluded by default. Each finding carries a confidence level — grade on 'high' only, treat 'medium' as review items. The 'summary' field gives complete per-detector counts even when the violation list is truncated, so there is no need to sample. Suppression rules can be declared in .cwm-navigator.json or inline with '// cwm:ignore APXXX — reason'.")]
    public static async Task<string> ExecuteAsync(
        WorkspaceManager workspace,
        [Description("Filter to file (partial match on file path)")] string? file = null,
        [Description("Filter to project name")] string? projectFilter = null,
        [Description("Minimum severity: 'warning' (default) or 'error'")] string severity = "warning",
        [Description("Maximum results to return")] int maxResults = 100,
        [Description("Source scope: 'production' (default), or 'all' to include test and migration code")] string scope = "production",
        [Description("Minimum confidence: 'medium' (default, returns all) or 'high' for graded findings only")] string confidence = "medium",
        CancellationToken ct = default)
    {
        var notReady = await workspace.EnsureReadyOrStatusAsync(ct);
        if (notReady is not null) return notReady;

        var solution = workspace.GetSolution();
        if (solution is null)
            return JsonSerializer.Serialize(new AntiPatternsResult(
                [], 0, 0, false, Math.Max(1, maxResults)));

        var minSeverity = severity.Equals("error", StringComparison.OrdinalIgnoreCase)
            ? AntiPatternSeverity.Error
            : AntiPatternSeverity.Warning;

        var minConfidence = confidence.Equals("high", StringComparison.OrdinalIgnoreCase)
            ? AntiPatternConfidence.High
            : AntiPatternConfidence.Medium;

        var includeAllKinds = scope.Equals("all", StringComparison.OrdinalIgnoreCase);
        var suppressions = SuppressionSet.Load(workspace.SolutionDirectory);

        var accumulator = new ScanAccumulator();
        var projects = GetFilteredProjects(solution, projectFilter);

        foreach (var project in projects)
        {
            ct.ThrowIfCancellationRequested();

            var compilation = await workspace.GetCompilationAsync(project.Id, ct);
            if (compilation is null)
                continue;

            var isTestProject = SourceClassifier.IsTestProject(project, compilation);
            var trees = GetFilteredTrees(compilation, file);

            foreach (var tree in trees)
            {
                ct.ThrowIfCancellationRequested();

                var relativePath = workspace.ToRelativePath(tree.FilePath ?? "");
                var kind = SourceClassifier.Classify(tree, relativePath, isTestProject, ct);
                accumulator.CountFile(kind);

                // Generated code is never a finding — nobody hand-wrote it.
                if (kind == SourceKind.Generated)
                    continue;

                if (!includeAllKinds && kind != SourceKind.Production)
                    continue;

                var applicable = Detectors.Where(d => d.AppliesTo.HasFlag(kind)).ToArray();
                if (applicable.Length == 0)
                    continue;

                // One semantic model and one parsed root shared by every detector on this tree.
                var semanticModel = applicable.Any(d => d.RequiresSemanticModel)
                    ? compilation.GetSemanticModel(tree)
                    : null;

                var context = new DetectionContext(tree, semanticModel, kind, ct);

                foreach (var detector in applicable)
                {
                    foreach (var violation in detector.Detect(context))
                    {
                        if (violation.Severity < minSeverity)
                            continue;

                        accumulator.Add(
                            violation,
                            kind,
                            workspace.ToRelativePath(violation.File),
                            suppressions,
                            context);
                    }
                }
            }
        }

        return JsonSerializer.Serialize(accumulator.Build(maxResults, minConfidence, suppressions));
    }

    /// <summary>
    /// Collects violations and the counts needed for a complete summary, so that a truncated
    /// violation list never hides the true composition of a scan.
    /// </summary>
    private sealed class ScanAccumulator
    {
        private readonly List<(AntiPatternViolation Violation, SourceKind Kind, string RelativePath)> _kept = [];
        private readonly Dictionary<string, int[]> _byId = new(StringComparer.Ordinal);
        private int _scanned;
        private int _production;
        private int _test;
        private int _generated;
        private int _migration;

        private const int High = 0;
        private const int Medium = 1;
        private const int Suppressed = 2;

        public void CountFile(SourceKind kind)
        {
            _scanned++;
            switch (kind)
            {
                case SourceKind.Production: _production++; break;
                case SourceKind.Test: _test++; break;
                case SourceKind.Generated: _generated++; break;
                case SourceKind.Migration: _migration++; break;
            }
        }

        public void Add(
            AntiPatternViolation violation,
            SourceKind kind,
            string relativePath,
            SuppressionSet suppressions,
            DetectionContext context)
        {
            var counts = Bucket(violation.Id);

            var suppressed = suppressions.IsDisabled(violation.Id)
                || suppressions.PathSuppressionReason(violation.Id, relativePath) is not null
                || context.IsSuppressedAt(violation.Id, violation.Line);

            if (suppressed)
            {
                counts[Suppressed]++;
                return;
            }

            counts[violation.Confidence == AntiPatternConfidence.High ? High : Medium]++;
            _kept.Add((violation, kind, relativePath));
        }

        private int[] Bucket(string id)
        {
            if (_byId.TryGetValue(id, out var existing))
                return existing;

            var created = new int[3];
            _byId[id] = created;
            return created;
        }

        public AntiPatternsResult Build(
            int maxResults,
            AntiPatternConfidence minConfidence,
            SuppressionSet suppressions)
        {
            var eligible = _kept
                .Where(entry => entry.Violation.Confidence >= minConfidence)
                .ToList();

            var limit = Math.Max(1, maxResults);

            var violations = eligible
                .OrderByDescending(entry => entry.Violation.Confidence)
                .ThenByDescending(entry => entry.Violation.Severity)
                .ThenBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Violation.Line)
                .Take(limit)
                .Select(entry => new AntiPatternInfo(
                    entry.Violation.Id,
                    entry.Violation.Severity.ToString().ToLowerInvariant(),
                    entry.Violation.Message,
                    entry.RelativePath,
                    entry.Violation.Line,
                    entry.Violation.Snippet,
                    entry.Violation.Suggestion,
                    entry.Violation.Confidence.ToString().ToLowerInvariant(),
                    entry.Violation.Member,
                    entry.Kind.ToString().ToLowerInvariant()))
                .ToList();

            var byId = _byId
                .Select(pair => new AntiPatternIdSummary(
                    pair.Key,
                    pair.Value[High],
                    pair.Value[Medium],
                    pair.Value[Suppressed],
                    pair.Value[High] + pair.Value[Medium] + pair.Value[Suppressed]))
                .OrderByDescending(entry => entry.High)
                .ThenByDescending(entry => entry.Total)
                .ToList();

            var summary = new AntiPatternSummary(
                High: byId.Sum(entry => entry.High),
                Medium: byId.Sum(entry => entry.Medium),
                Suppressed: byId.Sum(entry => entry.Suppressed),
                ById: byId,
                ScannedFiles: _scanned,
                ProductionFiles: _production,
                TestFiles: _test,
                GeneratedFiles: _generated,
                MigrationFiles: _migration,
                SuppressionConfig: suppressions.ConfigPath);

            return new AntiPatternsResult(
                violations, violations.Count, eligible.Count,
                eligible.Count > violations.Count, limit, summary);
        }
    }

    private static IEnumerable<Project> GetFilteredProjects(Solution solution, string? projectFilter)
    {
        if (projectFilter is null)
            return solution.Projects;

        return solution.Projects.Where(p =>
            p.Name.Equals(projectFilter, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<SyntaxTree> GetFilteredTrees(Compilation compilation, string? file)
    {
        if (file is null)
            return compilation.SyntaxTrees;

        return compilation.SyntaxTrees.Where(t =>
            t.FilePath?.Contains(file, StringComparison.OrdinalIgnoreCase) == true);
    }
}
