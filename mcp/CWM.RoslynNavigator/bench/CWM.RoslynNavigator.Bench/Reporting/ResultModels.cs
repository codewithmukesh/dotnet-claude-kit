using CWM.RoslynNavigator.Bench.Server;

namespace CWM.RoslynNavigator.Bench.Reporting;

// The JSON shape of one (corpus × server build) run. Intentionally many records in one
// file: these are pure DTOs, and reading the result schema in one place is the point.

internal sealed record RunResult(
    int Schema,
    string RunDate,
    DateTimeOffset GeneratedAtUtc,
    CorpusInfo Corpus,
    BuildInfo Build,
    EnvironmentInfo Environment,
    RunSettings Settings,
    StartupResult Startup,
    List<ToolResult> Tools,
    List<TaskResult> Tasks);

internal sealed record CorpusInfo(
    string Name,
    string Tier,
    string Repository,
    string Commit,
    string Solution,
    int ProjectsDeclared,
    int? ProjectsLoaded,
    int CsFiles,
    long CsBytes,
    List<string> Notes);

internal sealed record BuildInfo(string Label, string ServerVersion, string Executable);

internal sealed record EnvironmentInfo(string Os, int ProcessorCount, string Runtime, string Machine);

/// <param name="PrefillTokensPerSecond">Modelling assumption, never a measurement. See README.</param>
internal sealed record RunSettings(
    int WarmIterations,
    int CallTimeoutSeconds,
    int WarmSkipMs,
    int GrepHeadLimit,
    double BytesPerToken,
    bool DivisorCalibrated,
    double PrefillTokensPerSecond);

/// <param name="ReadyMs">Process start → first tool answer that is not a loading status.</param>
/// <param name="FirstAnswerMs">Process start → first semantic answer (find_symbol) issued immediately after ready.</param>
/// <param name="WarmMs">Process start → the server's "All compilations warmed" log line; null when not observed.</param>
internal sealed record StartupResult(
    long? ReadyMs,
    long? FirstAnswerMs,
    string FirstAnswerStatus,
    long? WarmMs,
    MemoryPeaks Memory,
    int Restarts,
    List<string> LoadProblems);

/// <param name="ColdMs">First call to this tool in the session (after warm-up was awaited).</param>
/// <param name="WarmP95Ms">Nearest-rank p95; with five samples this is the slowest of the five.</param>
internal sealed record ToolResult(
    string Tool,
    Dictionary<string, string> Arguments,
    string Status,
    double ColdMs,
    double? WarmMedianMs,
    double? WarmP95Ms,
    int WarmSamples,
    int Bytes,
    int Tokens,
    bool? Truncated,
    string? ErrorCode,
    string? ErrorMessage,
    string? Note);

/// <summary>
/// One task answered both ways. Measured numbers and modelled numbers live in separately
/// named fields so no report can blend them by accident.
/// </summary>
internal sealed record TaskResult(
    string Id,
    string Title,
    string Tool,
    string NavigatorStatus,
    bool? NavigatorTruncated,
    int NavigatorTokens,
    int NavigatorCalls,
    double? NavigatorMeasuredMs,
    string? GrepPattern,
    int GrepMatches,
    int GrepLinesShown,
    int GrepTokens,
    double GrepMeasuredMs,
    int FilesRead,
    int FileTokens,
    double FileReadMeasuredMs,
    int ManualTokens,
    int ManualCalls,
    double ManualMeasuredMs,
    int? SavedTokens,
    double? SavingsPercent,
    double? Multiplier,
    double? ModelledSecondsSaved);
