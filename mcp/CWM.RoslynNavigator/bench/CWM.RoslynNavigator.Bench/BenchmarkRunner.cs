using System.Runtime.InteropServices;
using System.Text.Json;
using CWM.RoslynNavigator.Bench.Corpus;
using CWM.RoslynNavigator.Bench.Measurement;
using CWM.RoslynNavigator.Bench.Reporting;
using CWM.RoslynNavigator.Bench.Server;
using CWM.RoslynNavigator.Telemetry;

namespace CWM.RoslynNavigator.Bench;

/// <summary>
/// Runs one corpus against one server build: start, ready, first answer, warm-up, every
/// tool cold then warm, then every task priced both ways. One server process per run;
/// if it dies mid-run it is restarted once per failure and the restart is recorded.
/// </summary>
internal sealed class BenchmarkRunner(BenchSettings settings, TextWriter log)
{
    public async Task<RunResult> RunAsync(
        CorpusDefinition corpus,
        string corpusRoot,
        string solutionPath,
        ServerBuild build,
        string runDate,
        CancellationToken ct)
    {
        var targets = corpus.Targets
            ?? throw new InvalidOperationException($"Corpus '{corpus.Name}' has no targets. Run 'select' first.");

        var scope = SolutionScope.For(solutionPath);
        var stats = CorpusStats.Measure(solutionPath);
        log.WriteLine($"[{corpus.Name} × {build.Label}] {stats.ProjectsDeclared} projects, {stats.CsFiles} .cs files");

        await using var memory = new MemorySampler();
        var session = await ServerSession.StartAsync(build, solutionPath, ct);
        var restarts = 0;
        var loadProblems = new List<string>();

        try
        {
            var readyMs = await session.WaitForReadyAsync(settings.ReadyTimeout, ct);
            log.WriteLine($"  ready: {Format(readyMs)} ms");

            // The first semantic question an agent asks, issued the moment the server says ready.
            var locate = ToolCatalog.For(targets)[0];
            var first = await session.CallAsync(locate.Tool, locate.Arguments, settings.CallTimeout, ct);
            long? firstAnswerMs = first.Status == "ok" ? session.ElapsedSinceStartMs : null;
            log.WriteLine($"  first answer: {Format(firstAnswerMs)} ms ({first.Status})");

            var warmMs = await session.WaitForWarmAsync(settings.WarmTimeout, ct);
            log.WriteLine($"  warm: {Format(warmMs)} ms");

            var projectsLoaded = await CountLoadedProjectsAsync(session, ct);

            var tools = new List<ToolResult>();
            var payloads = new Dictionary<string, string?>(StringComparer.Ordinal);

            foreach (var toolCase in ToolCatalog.For(targets))
            {
                if (!session.IsAlive)
                {
                    loadProblems.AddRange(session.LoadProblems);
                    await session.DisposeAsync();
                    session = await ServerSession.StartAsync(build, solutionPath, ct);
                    restarts++;
                    await session.WaitForReadyAsync(settings.ReadyTimeout, ct);
                    await session.WaitForWarmAsync(settings.WarmTimeout, ct);
                }

                var (result, payload) = await MeasureToolAsync(session, toolCase, ct);
                tools.Add(result);
                payloads[toolCase.Tool] = payload;
                log.WriteLine($"  {toolCase.Tool,-30} {result.Status,-15} cold {result.ColdMs,9:0} ms  warm p50 {Format(result.WarmMedianMs),9} ms  {result.Tokens,7} tok");
            }

            loadProblems.AddRange(session.LoadProblems);

            var measurer = new TaskMeasurer(scope, stats.CsBytes, settings.GrepHeadLimit, settings.PrefillTokensPerSecond);
            var tasks = TaskCatalog.For(targets)
                .Select(task =>
                {
                    var navigator = tools.First(t => t.Tool == task.Tool);
                    return measurer.Measure(task, navigator, payloads.GetValueOrDefault(task.Tool));
                })
                .ToList();

            var peaks = memory.Snapshot();
            log.WriteLine($"  memory: server peak {peaks.ServerPeakBytes / 1048576} MB, build hosts {peaks.BuildHostPeakBytes / 1048576} MB");

            return new RunResult(
                Schema: 1,
                RunDate: runDate,
                GeneratedAtUtc: DateTimeOffset.UtcNow,
                Corpus: new CorpusInfo(corpus.Name, corpus.Tier, corpus.Repository, corpus.Commit, corpus.Solution,
                    stats.ProjectsDeclared, projectsLoaded, stats.CsFiles, stats.CsBytes, corpus.Notes),
                Build: new BuildInfo(build.Label, session.ServerVersion, Path.GetFileName(build.Path)),
                Environment: new EnvironmentInfo(
                    RuntimeInformation.OSDescription,
                    Environment.ProcessorCount,
                    RuntimeInformation.FrameworkDescription,
                    RuntimeInformation.ProcessArchitecture.ToString()),
                Settings: new RunSettings(
                    settings.WarmIterations,
                    (int)settings.CallTimeout.TotalSeconds,
                    (int)settings.WarmSkipThreshold.TotalMilliseconds,
                    settings.GrepHeadLimit,
                    TokenEstimator.BytesPerToken,
                    TokenEstimator.IsCalibrated,
                    settings.PrefillTokensPerSecond),
                Startup: new StartupResult(readyMs, firstAnswerMs, first.Status, warmMs, peaks, restarts,
                    [.. loadProblems.Select(p => Anonymize(p, corpusRoot)).Distinct().Take(100)]),
                Tools: tools,
                Tasks: tasks);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    private async Task<(ToolResult Result, string? Payload)> MeasureToolAsync(
        ServerSession session, ToolCase toolCase, CancellationToken ct)
    {
        var arguments = toolCase.Arguments
            .Where(a => a.Value is not null)
            .ToDictionary(a => a.Key, a => Describe(a.Value));

        if (!session.HasTool(toolCase.Tool))
        {
            return (new ToolResult(toolCase.Tool, arguments, "n/a", 0, null, null, 0, 0, 0, null, null, null,
                "Tool not exposed by this build."), null);
        }

        var cold = await session.CallAsync(toolCase.Tool, toolCase.Arguments, settings.CallTimeout, ct);
        string? note = null;
        var samples = new List<double>();

        if (cold.Status is "timeout" or "transport-error")
        {
            note = "Warm repeats skipped: the cold call did not complete.";
        }
        else if (cold.ElapsedMs > settings.WarmSkipThreshold.TotalMilliseconds)
        {
            note = $"Warm repeats skipped: cold call exceeded {settings.WarmSkipThreshold.TotalSeconds:0} s.";
        }
        else
        {
            for (var i = 0; i < settings.WarmIterations && session.IsAlive; i++)
            {
                var warm = await session.CallAsync(toolCase.Tool, toolCase.Arguments, settings.CallTimeout, ct);
                if (warm.Status != cold.Status) note = $"Warm call {i + 1} returned '{warm.Status}'.";
                samples.Add(warm.ElapsedMs);
            }
        }

        samples.Sort();
        double? median = samples.Count == 0 ? null : Math.Round(Percentile(samples, 0.50), 1);
        double? p95 = samples.Count == 0 ? null : Math.Round(Percentile(samples, 0.95), 1);

        var result = new ToolResult(
            Tool: toolCase.Tool,
            Arguments: arguments,
            Status: cold.Status,
            ColdMs: Math.Round(cold.ElapsedMs, 1),
            WarmMedianMs: median,
            WarmP95Ms: p95,
            WarmSamples: samples.Count,
            Bytes: cold.Bytes,
            Tokens: TokenEstimator.FromBytes(cold.Bytes),
            Truncated: ReadTruncated(cold.Payload),
            ErrorCode: cold.ErrorCode,
            ErrorMessage: cold.ErrorMessage,
            Note: note);

        return (result, cold.Payload);
    }

    private static async Task<int?> CountLoadedProjectsAsync(ServerSession session, CancellationToken ct)
    {
        var graph = await session.CallAsync("get_project_graph", [], TimeSpan.FromMinutes(2), ct);
        if (graph.Status != "ok" || graph.Payload is null) return null;
        try
        {
            using var document = JsonDocument.Parse(graph.Payload);
            return document.RootElement.TryGetProperty("Projects", out var projects) ? projects.GetArrayLength() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Nearest-rank percentile over an already-sorted list.</summary>
    private static double Percentile(List<double> sorted, double percentile)
    {
        var rank = (int)Math.Ceiling(percentile * sorted.Count);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Count - 1)];
    }

    private static bool? ReadTruncated(string? payload)
    {
        if (string.IsNullOrEmpty(payload)) return null;
        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("Truncated", out var truncated)
                && truncated.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? truncated.GetBoolean()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Describe(object? value) => value switch
    {
        string s when s.Length > 80 => $"<{s.Length} chars>",
        string s => s,
        bool b => b ? "true" : "false",
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "",
    };

    /// <summary>
    /// Server log lines carry absolute paths; results are committed, so the machine-specific
    /// corpus root is cut off and only the corpus-relative path remains.
    /// </summary>
    private static string Anonymize(string line, string corpusRoot)
    {
        var root = Path.GetFullPath(corpusRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return line.Replace(root, "", StringComparison.OrdinalIgnoreCase);
    }

    private static string Format(double? value) => value is null ? "n/a" : value.Value.ToString("0");

    private static string Format(long? value) => value is null ? "n/a" : value.Value.ToString();
}
