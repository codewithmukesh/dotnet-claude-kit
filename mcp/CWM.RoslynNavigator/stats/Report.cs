namespace CWM.RoslynNavigator.Stats;

internal sealed record ToolRow(string Tool, int Calls, int Saved, long MedianMs, int ZeroResultPct);

internal sealed record RepoRow(
    string Name,
    string Path,
    int Calls,
    int Saved,
    int RepoTokens,
    double? AdoptionRate,
    int Sessions,
    long MeasuredTokens,
    bool HasTelemetry);

/// <summary>
/// Methodology, carried with the numbers. A savings figure without its assumptions is not
/// a claim anyone should accept, including us.
/// </summary>
internal sealed record Methodology(
    string Baseline,
    double BytesPerToken,
    bool DivisorCalibrated,
    string SessionDedup,
    string AdoptionBias,
    double InputRatePerMillion);

internal sealed record Report(
    int Repos,
    int ReposWithTelemetry,
    int TotalCalls,
    int SavedTokens,
    int ResponseTokens,
    int BaselineTokens,
    int ZeroResultCalls,
    int NotReadyCalls,
    int ErrorCalls,
    long MeasuredInputTokens,
    long MeasuredOutputTokens,
    long MeasuredCacheReadTokens,
    int Sessions,
    double? AggregateAdoption,
    decimal SavedCostUsd,
    List<ToolRow> Tools,
    List<RepoRow> RepoRows,
    List<string> DeadTools,
    Methodology Method)
{
    /// <summary>Whether any transcript was found at all, regardless of MCP usage.</summary>
    public bool HasTranscripts => Sessions > 0;

    /// <summary>
    /// Whether the server was invoked even once in any indexed session. Distinguishes "no
    /// data" from "plenty of data, and the server was never used" — very different findings
    /// that a single empty report would conflate.
    /// </summary>
    public bool HasAnyUsage => TotalCalls > 0 || RepoRows.Count > 0;
}

internal static class ReportBuilder
{
    /// <summary>
    /// Every tool the server ships. Anything here with no calls anywhere is dead weight —
    /// either the description is failing to attract the work it should, or the tool should
    /// not exist. That is the leaderboard's most actionable output.
    /// </summary>
    private static readonly string[] AllTools =
    [
        "find_symbol", "find_references", "find_implementations", "find_callers",
        "find_overrides", "find_dead_code", "get_type_hierarchy", "get_public_api",
        "get_symbol_detail", "get_project_graph", "get_dependency_graph", "get_diagnostics",
        "get_test_coverage_map", "detect_antipatterns", "detect_circular_dependencies",
        "get_symbol_source", "get_file_outline", "get_nuget_packages", "get_endpoint_map",
        "get_di_registrations", "resolve_stack_trace", "analyze_change_impact",
        "get_token_savings"
    ];

    public static Report Build(
        IReadOnlyList<RepoTelemetry> telemetry,
        IReadOnlyList<TranscriptStats> transcripts,
        double bytesPerToken,
        bool calibrated,
        decimal inputRatePerMillion)
    {
        var allCalls = telemetry.SelectMany(t => t.Calls).ToList();

        var tools = allCalls
            .GroupBy(c => c.Tool, StringComparer.Ordinal)
            .Select(g => new ToolRow(
                g.Key,
                g.Count(),
                g.Sum(c => c.Saved),
                Median(g.Select(c => c.ElapsedMs)),
                Percent(g.Count(c => c.Results == 0), g.Count())))
            .OrderByDescending(t => t.Saved)
            .ThenByDescending(t => t.Calls)
            .ToList();

        // A tool is only dead if nothing anywhere called it — in telemetry or transcripts.
        var seen = new HashSet<string>(tools.Select(t => t.Tool), StringComparer.Ordinal);
        foreach (var stat in transcripts)
            foreach (var tool in stat.RoslynByTool.Keys)
                seen.Add(tool);

        var deadTools = AllTools.Where(t => !seen.Contains(t)).OrderBy(t => t, StringComparer.Ordinal).ToList();

        var repoRows = BuildRepoRows(telemetry, transcripts);

        // Aggregate adoption is computed over calls, not as a mean of per-repo rates, so a
        // repo with three calls does not weigh the same as one with three thousand.
        var configured = transcripts.Where(t => t.RoslynCalls > 0).ToList();
        var roslynCalls = configured.Sum(t => t.RoslynCalls);
        var csharpCalls = configured.Sum(t => t.CSharpFileCalls);
        double? adoption = roslynCalls == 0 ? null : (double)roslynCalls / (roslynCalls + csharpCalls);

        var saved = allCalls.Sum(c => c.Saved);

        return new Report(
            Repos: repoRows.Count,
            ReposWithTelemetry: telemetry.Count,
            TotalCalls: allCalls.Count,
            SavedTokens: saved,
            ResponseTokens: allCalls.Sum(c => c.ResponseTokens),
            BaselineTokens: allCalls.Sum(c => c.BaselineTokens),
            ZeroResultCalls: allCalls.Count(c => c.State == "ok" && c.Results == 0),
            NotReadyCalls: allCalls.Count(c => c.State == "loading"),
            ErrorCalls: allCalls.Count(c => c.State == "error"),
            MeasuredInputTokens: transcripts.Sum(t => t.InputTokens),
            MeasuredOutputTokens: transcripts.Sum(t => t.OutputTokens),
            MeasuredCacheReadTokens: transcripts.Sum(t => t.CacheReadTokens),
            Sessions: transcripts.Sum(t => t.Sessions.Count),
            AggregateAdoption: adoption,
            SavedCostUsd: saved / 1_000_000m * inputRatePerMillion,
            Tools: tools,
            RepoRows: repoRows,
            DeadTools: deadTools,
            Method: new Methodology(
                Baseline: "On-disk size of the source files each answer was derived from. Ignores the wrong files a text search would have surfaced first, so it understates.",
                BytesPerToken: bytesPerToken,
                DivisorCalibrated: calibrated,
                SessionDedup: "A file contributes once per session; re-querying it adds nothing.",
                AdoptionBias: "Denominator counts only Read/Grep/Glob calls that demonstrably targeted C#. Ambiguous searches are excluded, which biases adoption upward.",
                InputRatePerMillion: (double)inputRatePerMillion));
    }

    /// <summary>
    /// Joins the two sources per repository. A repo present in only one of them is still
    /// reported, flagged for what it has — dropping it would silently shrink the picture.
    /// </summary>
    private static List<RepoRow> BuildRepoRows(
        IReadOnlyList<RepoTelemetry> telemetry,
        IReadOnlyList<TranscriptStats> transcripts)
    {
        var rows = new List<RepoRow>();
        var matchedTranscripts = new HashSet<TranscriptStats>();

        foreach (var repo in telemetry)
        {
            var match = transcripts.FirstOrDefault(t => RepoPath.SameRepository(t.RepoPath, repo.Info.SolutionPath));
            if (match is not null) matchedTranscripts.Add(match);

            rows.Add(new RepoRow(
                Name: repo.Info.Name,
                Path: repo.Info.SolutionPath,
                Calls: repo.Calls.Count,
                Saved: repo.Saved,
                RepoTokens: repo.Info.TotalSourceTokens,
                AdoptionRate: match?.AdoptionRate,
                Sessions: match?.Sessions.Count ?? 0,
                MeasuredTokens: match is null ? 0 : match.InputTokens + match.OutputTokens,
                HasTelemetry: true));
        }

        // Transcript-only repos: the server was used but telemetry was never enabled there,
        // which is the norm given collection is opt-in.
        foreach (var stat in transcripts)
        {
            if (matchedTranscripts.Contains(stat) || stat.RoslynCalls == 0) continue;

            rows.Add(new RepoRow(
                Name: RepoPath.DisplayName(stat.RepoPath),
                Path: stat.RepoPath,
                Calls: stat.RoslynCalls,
                Saved: 0,
                RepoTokens: 0,
                AdoptionRate: stat.AdoptionRate,
                Sessions: stat.Sessions.Count,
                MeasuredTokens: stat.InputTokens + stat.OutputTokens,
                HasTelemetry: false));
        }

        return rows.OrderByDescending(r => r.Saved).ThenByDescending(r => r.Calls).ToList();
    }

    private static long Median(IEnumerable<long> values)
    {
        var ordered = values.Order().ToList();
        if (ordered.Count == 0) return 0;

        var mid = ordered.Count / 2;
        return ordered.Count % 2 == 1 ? ordered[mid] : (ordered[mid - 1] + ordered[mid]) / 2;
    }

    private static int Percent(int part, int total) =>
        total == 0 ? 0 : (int)Math.Round(part * 100.0 / total, MidpointRounding.AwayFromZero);
}
