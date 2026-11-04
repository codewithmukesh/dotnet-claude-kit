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

    [McpServerTool(Name = "detect_antipatterns"), Description("Find .NET anti-patterns: async void, sync-over-async, new HttpClient(), DateTime.Now, broad catch, interpolated logging, missing pragma restore, missing CancellationToken, EF queries without AsNoTracking. Summary counts every finding even when truncated. Confidence is omitted when high; treat medium as review items. Suppress with // cwm:ignore APxxx or .cwm-navigator.json.")]
    public static async Task<string> ExecuteAsync(
        WorkspaceManager workspace,
        [Description("Path substring filter")] string? file = null,
        string? projectFilter = null,
        [Description("Minimum: warning or error")] string severity = "warning",
        int maxResults = 100,
        int? offset = null,
        [Description("production, or all to include test and migration code")] string scope = "production",
        [Description("Minimum: medium (all) or high")] string confidence = "medium",
        CancellationToken ct = default)
    {
        var notReady = await workspace.EnsureReadyOrStatusAsync(ct);
        if (notReady is not null) return notReady;

        var invalid = ArgumentChecks.Choice("severity", severity, "warning", "error")
            ?? ArgumentChecks.Choice("scope", scope, "production", "all")
            ?? ArgumentChecks.Choice("confidence", confidence, "medium", "high");
        if (invalid is not null) return invalid;

        var solution = workspace.GetSolution();
        if (solution is null)
            return ToolJson.Serialize(new AntiPatternsResult(
                [], 0, 0, false, Math.Max(1, maxResults)));

        if (ArgumentChecks.UnknownProject(solution, projectFilter) is { } unknown)
            return unknown;

        var minSeverity = severity.Equals("error", StringComparison.OrdinalIgnoreCase)
            ? AntiPatternSeverity.Error
            : AntiPatternSeverity.Warning;

        var minConfidence = confidence.Equals("high", StringComparison.OrdinalIgnoreCase)
            ? AntiPatternConfidence.High
            : AntiPatternConfidence.Medium;

        var includeAllKinds = scope.Equals("all", StringComparison.OrdinalIgnoreCase);
        var suppressions = SuppressionSet.Load(workspace.SolutionDirectory);

        var accumulator = new ScanAccumulator();
        var seenFiles = new Dictionary<string, HashSet<(string, int, string, string)>>(StringComparer.OrdinalIgnoreCase);
        var projects = GetFilteredProjects(solution, projectFilter).ToList();

        // A multi-targeted csproj loads once per framework. A file without preprocessor
        // directives has the same syntax in every flavor, so it is scanned in one — the
        // newest framework, whose wider API surface gives the semantic detectors the most to
        // match (a CancellationToken overload missing from netstandard2.0, say). Files with
        // #if are scanned in every flavor and merged below.
        var representative = RepresentativeFlavors(projects);
        var plan = new List<(Compilation Compilation, SyntaxTree Tree, bool IsTest)>();

        foreach (var project in projects)
        {
            ct.ThrowIfCancellationRequested();

            var compilation = await workspace.GetCompilationAsync(project.Id, ct);
            if (compilation is null)
                continue;

            var isTestProject = SourceClassifier.IsTestProject(project);
            var skipPlainFiles = representative.TryGetValue(project.FilePath ?? "", out var chosen) && chosen != project.Id;

            foreach (var tree in GetFilteredTrees(compilation, file))
            {
                if (skipPlainFiles && !(await tree.GetRootAsync(ct)).ContainsDirectives)
                    continue;

                plan.Add((compilation, tree, isTestProject));
            }
        }

        // Every planned tree is scanned in parallel (detectors are stateless; each tree gets
        // its own DetectionContext), then replayed into the accumulator in plan order so
        // counts, ordering and truncation match a sequential scan exactly. One pass over all
        // projects rather than one per project: most projects in a large solution hold a
        // handful of files, too few to keep the cores busy.
        var scans = new TreeScan[plan.Count];

        Parallel.For(0, plan.Count, new ParallelOptions { CancellationToken = ct }, index =>
        {
            var (compilation, tree, isTestProject) = plan[index];
            scans[index] = ScanTree(
                compilation, tree, isTestProject, includeAllKinds, minSeverity, suppressions, workspace, ct);
        });

        for (var i = 0; i < scans.Length; i++)
        {
            // A file scanned in several flavors is counted once and a finding reported once; a
            // later flavor adds only what the earlier ones did not find (code under #if).
            var path = plan[i].Tree.FilePath ?? "";
            var firstSeen = !seenFiles.TryGetValue(path, out var known);
            if (known is null)
            {
                seenFiles[path] = known = [];
                accumulator.CountFile(scans[i].Kind);
            }

            foreach (var finding in scans[i].Findings)
            {
                var v = finding.Violation;
                if (known.Add((v.Id, v.Line, v.Message, v.Snippet)) || firstSeen)
                    accumulator.Add(v, scans[i].Kind, finding.RelativePath, finding.Suppressed);
            }
        }
        var start = Math.Max(0, offset ?? 0);
        return Paging.FitCount(
            Math.Max(1, maxResults),
            limit => ToolJson.Serialize(accumulator.Build(limit, start, minConfidence, suppressions)));
    }

    private readonly record struct Finding(AntiPatternViolation Violation, string RelativePath, bool Suppressed);


    private sealed record TreeScan(SourceKind Kind, List<Finding> Findings);

    private static TreeScan ScanTree(
        Compilation compilation,
        SyntaxTree tree,
        bool isTestProject,
        bool includeAllKinds,
        AntiPatternSeverity minSeverity,
        SuppressionSet suppressions,
        WorkspaceManager workspace,
        CancellationToken ct)
    {
        var relativePath = workspace.ToRelativePath(tree.FilePath ?? "");
        var kind = SourceClassifier.Classify(tree, relativePath, isTestProject, ct);
        var findings = new List<Finding>();

        // Generated code is never a finding — nobody hand-wrote it.
        if (kind == SourceKind.Generated)
            return new TreeScan(kind, findings);

        if (!includeAllKinds && kind != SourceKind.Production)
            return new TreeScan(kind, findings);

        var applicable = Detectors.Where(d => d.AppliesTo.HasFlag(kind)).ToArray();
        if (applicable.Length == 0)
            return new TreeScan(kind, findings);

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

                var violationPath = workspace.ToRelativePath(violation.File);
                var suppressed = suppressions.IsDisabled(violation.Id)
                    || suppressions.PathSuppressionReason(violation.Id, violationPath) is not null
                    || context.IsSuppressedAt(violation.Id, violation.Line);

                findings.Add(new Finding(violation, violationPath, suppressed));
            }
        }

        return new TreeScan(kind, findings);
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
            bool suppressed)
        {
            var counts = Bucket(violation.Id);

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
            int offset,
            AntiPatternConfidence minConfidence,
            SuppressionSet suppressions)
        {
            var eligible = _kept
                .Where(entry => entry.Violation.Confidence >= minConfidence)
                .ToList();

            var limit = Math.Max(1, maxResults);

            var page = eligible
                .OrderByDescending(entry => entry.Violation.Confidence)
                .ThenByDescending(entry => entry.Violation.Severity)
                .ThenBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Violation.Line)
                .Skip(offset)
                .Take(limit)
                .ToList();

            // Severity, message, and suggestion repeat on every finding of a detector; state the
            // most common of each once per id and keep a finding's own only where it differs.
            var rules = page
                .GroupBy(entry => entry.Violation.Id, StringComparer.Ordinal)
                .Select(g => new AntiPatternRule(
                    g.Key,
                    MostCommon(g.Select(e => SeverityName(e.Violation.Severity))),
                    MostCommon(g.Select(e => e.Violation.Message)),
                    MostCommon(g.Select(e => e.Violation.Suggestion))))
                .OrderBy(r => r.Id, StringComparer.Ordinal)
                .ToList();
            var ruleById = rules.ToDictionary(r => r.Id, StringComparer.Ordinal);

            var violations = page
                .Select(entry =>
                {
                    var v = entry.Violation;
                    var rule = ruleById[v.Id];
                    var severity = SeverityName(v.Severity);
                    return new AntiPatternInfo(
                        v.Id,
                        entry.RelativePath,
                        v.Line,
                        v.Snippet,
                        v.Member,
                        Message: v.Message == rule.Message ? null : v.Message,
                        Suggestion: v.Suggestion == rule.Suggestion ? null : v.Suggestion,
                        Severity: severity == rule.Severity ? null : severity,
                        Confidence: v.Confidence == AntiPatternConfidence.High ? null : "medium",
                        SourceKind: entry.Kind == SourceKind.Production ? null : entry.Kind.ToString().ToLowerInvariant());
                })
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

            var start = Math.Min(offset, eligible.Count);
            var end = start + violations.Count;
            return new AntiPatternsResult(
                violations, violations.Count, eligible.Count,
                end < eligible.Count, limit, summary, rules.Count > 0 ? rules : null,
                start, end < eligible.Count ? end : null);
        }

        private static string SeverityName(AntiPatternSeverity severity) => severity.ToString().ToLowerInvariant();

        private static string MostCommon(IEnumerable<string> values) =>
            values.GroupBy(v => v, StringComparer.Ordinal)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.Ordinal)
                .First().Key;
    }

    /// <summary>
    /// For each multi-targeted csproj among <paramref name="projects"/>, the flavor with the
    /// newest target framework. Single-target projects are absent.
    /// </summary>
    private static Dictionary<string, ProjectId> RepresentativeFlavors(IReadOnlyList<Project> projects) =>
        projects
            .Where(p => p.FilePath is not null && GetProjectGraphTool.ExtractTfmFromProjectName(p.Name) is not null)
            .GroupBy(p => p.FilePath!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToDictionary(
                g => g.Key,
                g => g.MaxBy(p => FrameworkRank(GetProjectGraphTool.ExtractTfmFromProjectName(p.Name)!))!.Id,
                StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Orders target frameworks oldest to newest: .NET Framework, .NET Standard, .NET Core,
    /// then .NET 5+, each by version; an OS-specific flavor (net10.0-android) ranks just
    /// below its plain framework.
    /// </summary>
    internal static (int Family, Version Version, int Plain) FrameworkRank(string tfm)
    {
        var dash = tfm.IndexOf('-');
        var plain = dash < 0 ? 1 : 0;
        var name = dash < 0 ? tfm : tfm[..dash];

        var (family, digits) = name switch
        {
            _ when name.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase) => (1, name["netstandard".Length..]),
            _ when name.StartsWith("netcoreapp", StringComparison.OrdinalIgnoreCase) => (2, name["netcoreapp".Length..]),
            _ when name.StartsWith("net", StringComparison.OrdinalIgnoreCase) && name.Contains('.') => (3, name["net".Length..]),
            _ when name.StartsWith("net", StringComparison.OrdinalIgnoreCase) => (0, string.Join('.', name["net".Length..].ToCharArray())),
            _ => (-1, "0"),
        };

        return (family, Version.TryParse(digits.Contains('.') ? digits : digits + ".0", out var version) ? version : new Version(0, 0), plain);
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
