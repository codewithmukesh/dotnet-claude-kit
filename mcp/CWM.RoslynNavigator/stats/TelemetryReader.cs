using System.Text.Json;

namespace CWM.RoslynNavigator.Stats;

/// <summary>Everything recorded for one repository.</summary>
internal sealed class RepoTelemetry
{
    public required RepoInfo Info { get; init; }
    public required IReadOnlyList<CallRecord> Calls { get; init; }

    public int Saved => Calls.Sum(c => c.Saved);
    public int ResponseTokens => Calls.Sum(c => c.ResponseTokens);
    public int BaselineTokens => Calls.Sum(c => c.BaselineTokens);
    public int ZeroResultCalls => Calls.Count(c => c.State == "ok" && c.Results == 0);
    public int NotReadyCalls => Calls.Count(c => c.State == "loading");
    public int ErrorCalls => Calls.Count(c => c.State == "error");
}

/// <summary>
/// Reads the per-repository telemetry the server writes. Malformed lines are skipped
/// rather than fatal: a live server may be mid-write on the trailing line.
/// </summary>
internal sealed class TelemetryReader(string root)
{
    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "cwm-roslyn-navigator");

    public IReadOnlyList<RepoTelemetry> Read(CancellationToken ct = default)
    {
        var reposDirectory = Path.Combine(root, "repos");
        if (!Directory.Exists(reposDirectory)) return [];

        var results = new List<RepoTelemetry>();

        foreach (var directory in Directory.EnumerateDirectories(reposDirectory))
        {
            ct.ThrowIfCancellationRequested();

            var info = ReadRepoInfo(Path.Combine(directory, "repo.json"));
            if (info is null) continue;

            var calls = new List<CallRecord>();

            // Rolled logs share the directory; all of them count toward the total.
            foreach (var log in Directory.EnumerateFiles(directory, "calls*.jsonl"))
                calls.AddRange(ReadCalls(log, ct));

            results.Add(new RepoTelemetry { Info = info, Calls = calls });
        }

        return results.OrderByDescending(r => r.Saved).ToList();
    }

    private static RepoInfo? ReadRepoInfo(string path)
    {
        if (!File.Exists(path)) return null;

        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(path), StatsJsonContext.Default.RepoInfo);
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            return null;
        }
    }

    private static IEnumerable<CallRecord> ReadCalls(string path, CancellationToken ct)
    {
        IEnumerable<string> lines;
        try
        {
            lines = File.ReadLines(path);
        }
        catch (IOException)
        {
            yield break;
        }

        foreach (var line in lines)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line)) continue;

            CallRecord? record = null;
            try
            {
                record = JsonSerializer.Deserialize(line, StatsJsonContext.Default.CallRecord);
            }
            catch (JsonException)
            {
                // Partially-flushed trailing line while the server is running.
            }

            if (record is not null) yield return record;
        }
    }
}
