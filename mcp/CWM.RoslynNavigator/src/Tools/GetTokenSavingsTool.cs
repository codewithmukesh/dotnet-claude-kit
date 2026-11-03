using System.ComponentModel;
using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Telemetry;
using ModelContextProtocol.Server;

namespace CWM.RoslynNavigator.Tools;

[McpServerToolType]
public static class GetTokenSavingsTool
{
    [McpServerTool(Name = "get_token_savings"), Description("Report how many tokens this server's tools have saved in this repository, against a defensible baseline: the on-disk size of the source files each answer was derived from. Includes a per-tool leaderboard and separate zero-result, not-ready, and error counts. Telemetry is opt-in (CWM_ROSLYN_TELEMETRY=on) and never leaves the machine; when off, the tool says so and reports nothing.")]
    public static Task<string> ExecuteAsync(
        IServiceProvider services,
        WorkspaceManager workspace,
        [Description("Maximum tools to list in the leaderboard")] int maxResults = 25,
        CancellationToken ct = default)
    {
        // Resolved rather than injected: the telemetry types stay internal, so they cannot
        // appear in the signature of a public tool method.
        var telemetry = (ToolTelemetry?)services.GetService(typeof(ToolTelemetry));

        if (telemetry is null || !telemetry.Enabled)
        {
            return Task.FromResult(ToolJson.Serialize(new TokenSavingsResult(
                Enabled: false,
                Message: $"Telemetry is off. Set {TelemetryOptions.EnvironmentVariable}=on and restart the server to collect. Nothing is ever transmitted — records are written to this machine only.",
                0, 0, 0, 0, 0, 0, 0, 0, 0, [], null)));
        }

        var records = telemetry.Store.ReadAll();

        if (records.Count == 0)
        {
            return Task.FromResult(ToolJson.Serialize(new TokenSavingsResult(
                Enabled: true,
                Message: "Telemetry is on but no calls have been recorded yet for this repository.",
                0, 0, 0, 0, 0, RepoTokens(workspace), 0, 0, 0, [], Method())));
        }

        var saved = records.Sum(r => r.Saved);
        var byTool = records
            .GroupBy(r => r.Tool, StringComparer.Ordinal)
            .Select(group => new ToolUsage(
                Tool: group.Key,
                Calls: group.Count(),
                Saved: group.Sum(r => r.Saved),
                MedianMs: Median(group.Select(r => r.ElapsedMs)),
                ZeroResultPct: Percent(group.Count(r => r.Results == 0), group.Count())))
            .OrderByDescending(t => t.Saved)
            .ThenByDescending(t => t.Calls)
            .Take(Math.Max(1, maxResults))
            .ToList();

        var result = new TokenSavingsResult(
            Enabled: true,
            Message: Summarize(saved, records.Count, RepoTokens(workspace)),
            TotalCalls: records.Count,
            SavedTokens: saved,
            ResponseTokens: records.Sum(r => r.ResponseTokens),
            BaselineTokens: records.Sum(r => r.BaselineTokens),
            FilesCounted: records.Sum(r => r.Files),
            RepoTokens: RepoTokens(workspace),
            ZeroResultCalls: records.Count(r => r.State == TelemetryStates.Ok && r.Results == 0),
            NotReadyCalls: records.Count(r => r.State == TelemetryStates.Loading),
            ErrorCalls: records.Count(r => r.State == TelemetryStates.Error),
            ByTool: byTool,
            Method: Method());

        return Task.FromResult(ToolJson.Serialize(result));
    }

    /// <summary>
    /// States the assumptions inline. A reader who wants to reject the number should be able
    /// to see exactly what it was built from without leaving the response.
    /// </summary>
    private static SavingsMethod Method() => new(
        Baseline: "On-disk size of the source files each answer was derived from — what reading them would have cost. Deliberately understates: it ignores the wrong files a text search would have surfaced first.",
        BytesPerToken: TokenEstimator.BytesPerToken,
        DivisorCalibrated: TokenEstimator.IsCalibrated,
        CalibrationSampleSize: TokenEstimator.CalibrationSampleSize,
        Dedup: "A file counts once per session. Re-querying it adds nothing, because without the tool it would already have been in context.",
        Excluded: "Errors, zero-result calls, and calls made while the workspace was still loading contribute no savings.");

    private static string Summarize(int saved, int calls, int repoTokens)
    {
        var scale = repoTokens > 0
            ? $" This repository is ~{repoTokens:N0} tokens of source."
            : string.Empty;

        var caveat = TokenEstimator.IsCalibrated
            ? string.Empty
            : " The bytes-per-token divisor is an uncalibrated estimate, so treat this as indicative.";

        return $"~{saved:N0} tokens saved across {calls:N0} call(s).{scale}{caveat}";
    }

    private static int RepoTokens(WorkspaceManager workspace)
    {
        var bytes = 0L;
        foreach (var path in workspace.KnownDocumentPaths)
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Exists) bytes += info.Length;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // One unreadable file should not void the repo scale.
            }
        }

        return TokenEstimator.FromBytes(bytes);
    }

    private static long Median(IEnumerable<long> values)
    {
        var ordered = values.Order().ToList();
        if (ordered.Count == 0) return 0;

        var mid = ordered.Count / 2;
        return ordered.Count % 2 == 1
            ? ordered[mid]
            : (ordered[mid - 1] + ordered[mid]) / 2;
    }

    private static int Percent(int part, int total) =>
        total == 0 ? 0 : (int)Math.Round(part * 100.0 / total, MidpointRounding.AwayFromZero);
}
