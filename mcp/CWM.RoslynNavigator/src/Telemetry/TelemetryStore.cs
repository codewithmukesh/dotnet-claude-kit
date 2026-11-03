using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CWM.RoslynNavigator.Telemetry;

/// <summary>
/// On-disk layout for one repository's telemetry.
///
/// <para>Data lives under the user's local application data, not in the repository being
/// analyzed. That keeps usage data out of git entirely — no .gitignore entry to forget, no
/// accidental commit — and lets it survive a re-clone.</para>
///
/// <code>
/// %LOCALAPPDATA%/cwm-roslyn-navigator/repos/&lt;sha256(solutionPath)[..12]&gt;/
///   repo.json     solution path and scale, written once
///   calls.jsonl   append-only, one line per call
/// </code>
/// </summary>
internal sealed class TelemetryStore(TelemetryOptions options, ILogger logger)
{
    /// <summary>Roll the log at this size so a long-lived install cannot fill a disk.</summary>
    private const long MaxLogBytes = 50L * 1024 * 1024;

    private const string CallsFileName = "calls.jsonl";
    private const string RepoFileName = "repo.json";

    /// <summary>
    /// Shared: a JsonSerializerOptions instance caches type metadata on first use, so a new
    /// one per write rebuilds that cache every time.
    /// </summary>
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    private readonly Lock _writeGate = new();

    public string? RepoDirectory { get; private set; }

    public string? CallsPath => RepoDirectory is null ? null : Path.Combine(RepoDirectory, CallsFileName);

    /// <summary>
    /// Short, stable, filesystem-safe id for a solution. A hash rather than the path itself
    /// because a path contains a username, and this directory name should not.
    /// </summary>
    public static string RepoId(string solutionPath)
    {
        var normalized = solutionPath.Replace('\\', '/').ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }

    /// <summary>
    /// Prepares the directory and writes repo.json. Returns false when telemetry is off or
    /// the location is unusable, in which case nothing further is attempted.
    /// </summary>
    public bool Initialize(string solutionPath, int projectCount, long totalSourceBytes)
    {
        if (!options.Enabled) return false;

        try
        {
            var dir = Path.Combine(options.RootDirectory, "repos", RepoId(solutionPath));
            Directory.CreateDirectory(dir);
            RepoDirectory = dir;

            var repo = new
            {
                solutionPath,
                name = Path.GetFileNameWithoutExtension(solutionPath),
                projectCount,
                totalSourceBytes,
                totalSourceTokens = TokenEstimator.FromBytes(totalSourceBytes),
                bytesPerToken = TokenEstimator.BytesPerToken,
                calibrated = TokenEstimator.IsCalibrated,
                updatedUtc = DateTimeOffset.UtcNow
            };

            File.WriteAllText(
                Path.Combine(dir, RepoFileName),
                JsonSerializer.Serialize(repo, IndentedJson));

            return true;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Telemetry storage unavailable; collection disabled for this session.");
            RepoDirectory = null;
            return false;
        }
    }

    /// <summary>
    /// Appends a batch of records. Failures are swallowed by design — a disk problem must
    /// never surface as a failed tool call.
    /// </summary>
    public void Append(IReadOnlyList<CallRecord> records)
    {
        if (records.Count == 0 || CallsPath is not { } path) return;

        try
        {
            var builder = new StringBuilder();
            foreach (var record in records)
                builder.AppendLine(JsonSerializer.Serialize(record, TelemetryJsonContext.Default.CallRecord));

            lock (_writeGate)
            {
                RollIfOversized(path);
                File.AppendAllText(path, builder.ToString());
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Telemetry append failed; {Count} record(s) dropped.", records.Count);
        }
    }

    /// <summary>Reads every record. Malformed lines are skipped, not fatal.</summary>
    public IReadOnlyList<CallRecord> ReadAll()
    {
        if (CallsPath is not { } path || !File.Exists(path)) return [];

        var records = new List<CallRecord>();

        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    var record = JsonSerializer.Deserialize(line, TelemetryJsonContext.Default.CallRecord);
                    if (record is not null) records.Add(record);
                }
                catch (JsonException)
                {
                    // A partially-flushed trailing line is expected while the server is live.
                }
            }
        }
        catch (IOException ex)
        {
            logger.LogDebug(ex, "Telemetry read failed.");
        }

        return records;
    }

    private static void RollIfOversized(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < MaxLogBytes) return;

        var rolled = Path.ChangeExtension(path, $".{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.jsonl");
        File.Move(path, rolled, overwrite: true);
    }
}
