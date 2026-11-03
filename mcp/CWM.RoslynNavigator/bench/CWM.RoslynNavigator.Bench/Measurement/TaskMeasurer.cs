using System.Diagnostics;
using System.Text.Json;
using CWM.RoslynNavigator.Bench.Corpus;
using CWM.RoslynNavigator.Bench.Reporting;
using CWM.RoslynNavigator.Telemetry;

namespace CWM.RoslynNavigator.Bench.Measurement;

/// <summary>
/// Prices a task both ways. The navigator side is the measured tool call; the manual side is
/// a measured search plus the measured read of every file the answer lives in, priced with
/// the server's own <see cref="BaselineCalculator"/> (each file once, capped at repo size).
/// </summary>
internal sealed class TaskMeasurer(SolutionScope scope, long repoBytes, int grepHeadLimit, double prefillTokensPerSecond)
{
    public TaskResult Measure(TaskCase task, ToolResult navigator, string? payload)
    {
        var grep = task.GrepPattern is null
            ? null
            : GrepScanner.Search(scope, task.GrepPattern, task.GrepGlob, grepHeadLimit);

        var paths = BaselinePaths(task, payload);

        // A fresh calculator per task: each task is priced as if it were the first thing the
        // agent did, so no task inherits a discount from files an earlier task "already read".
        var baseline = new BaselineCalculator(() => scope.SolutionDirectory, () => repoBytes).Compute(paths);
        var readMs = TimeFileReads(paths);

        var grepTokens = grep is null ? 0 : TokenEstimator.FromBytes(grep.OutputBytes);
        var manualTokens = grepTokens + baseline.Tokens;
        var manualCalls = (grep is null ? 0 : 1) + baseline.Files;
        var grepMs = grep?.ElapsedMs ?? 0;

        var answered = navigator.Status == "ok";
        var navMs = navigator.WarmMedianMs ?? (answered ? navigator.ColdMs : null);
        int? saved = answered ? manualTokens - navigator.Tokens : null;

        return new TaskResult(
            Id: task.Id,
            Title: task.Title,
            Tool: task.Tool,
            NavigatorStatus: navigator.Status,
            NavigatorTruncated: navigator.Truncated,
            NavigatorTokens: navigator.Tokens,
            NavigatorCalls: 1,
            NavigatorMeasuredMs: navMs,
            GrepPattern: task.GrepPattern,
            GrepMatches: grep?.TotalMatches ?? 0,
            GrepLinesShown: grep?.OutputLines ?? 0,
            GrepTokens: grepTokens,
            GrepMeasuredMs: Math.Round(grepMs, 1),
            FilesRead: baseline.Files,
            FileTokens: baseline.Tokens,
            FileReadMeasuredMs: Math.Round(readMs, 1),
            ManualTokens: manualTokens,
            ManualCalls: manualCalls,
            ManualMeasuredMs: Math.Round(grepMs + readMs, 1),
            SavedTokens: saved,
            SavingsPercent: answered && manualTokens > 0 ? Math.Round(100.0 * saved!.Value / manualTokens, 1) : null,
            Multiplier: answered && navigator.Tokens > 0 && manualTokens > 0 ? Math.Round((double)manualTokens / navigator.Tokens, 1) : null,
            ModelledSecondsSaved: answered ? Math.Round(saved!.Value / prefillTokensPerSecond, 2) : null);
    }

    private static List<string> BaselinePaths(TaskCase task, string? payload) => task.Baseline switch
    {
        BaselineSource.FixedFiles => task.FixedFiles,
        BaselineSource.ResponseProjectPaths => ProjectPaths(payload),
        _ => [.. ResponseInspector.Inspect(payload).Files],
    };

    private static List<string> ProjectPaths(string? payload)
    {
        if (string.IsNullOrEmpty(payload)) return [];
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty("Projects", out var projects)) return [];
            return [.. projects.EnumerateArray()
                .Select(p => p.TryGetProperty("Path", out var path) ? path.GetString() : null)
                .OfType<string>()];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Reads each distinct file once, as an agent's Read calls would. Measures I/O only.</summary>
    private double TimeFileReads(List<string> paths)
    {
        var distinct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var started = Stopwatch.GetTimestamp();

        foreach (var path in paths)
        {
            var absolute = Path.IsPathRooted(path) ? path : Path.Combine(scope.SolutionDirectory, path);
            if (!distinct.Add(Path.GetFullPath(absolute)) || !File.Exists(absolute)) continue;
            _ = File.ReadAllBytes(absolute);
        }

        return Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }
}
