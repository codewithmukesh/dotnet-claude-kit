using System.Collections.Concurrent;

namespace CWM.RoslynNavigator.Telemetry;

/// <summary>Baseline for one call: what reading the same answer out of source would have cost.</summary>
internal readonly record struct Baseline(int Tokens, int Files);

/// <summary>
/// Computes the counterfactual "what would this have cost without the tool" using the
/// file-read floor: the on-disk size of the files the answer actually came from.
///
/// <para>Three guards keep the number honest, and all three push it <i>down</i>:</para>
///
/// <list type="number">
/// <item><b>Per-call dedup</b> — twenty references in one file cost that file once.</item>
/// <item><b>Session dedup</b> — the important one. Without the tool, Claude would have read
/// a file once and kept it in context; charging its baseline again on every later call that
/// touches it would inflate the total badly. A path that has already contributed contributes
/// zero for the rest of the process, whose lifetime is the session.</item>
/// <item><b>Repo cap</b> — no single call can claim to have saved more than the repository
/// contains, however broad the query.</item>
/// </list>
///
/// <para>Files are never read. One <c>stat</c> per path, cached by path and write time, so
/// the cost is microseconds and does not grow with file size.</para>
/// </summary>
/// <remarks>
/// The solution directory is supplied as a callback, not a value. Telemetry is constructed
/// during host build, before any solution has been discovered — capturing the directory
/// then would capture null, and every relative path in every response would fail to resolve
/// for the life of the process, silently reporting a zero baseline for every call.
/// </remarks>
internal sealed class BaselineCalculator(Func<string?> solutionDirectory, Func<long> repoTotalBytes)
{
    private readonly ConcurrentDictionary<string, CachedSize> _sizes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _sessionCounted = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _sessionGate = new();

    private readonly record struct CachedSize(DateTime LastWriteUtc, long Length);

    /// <summary>Distinct paths that have contributed to a baseline so far this session.</summary>
    public int SessionCountedFiles
    {
        get { lock (_sessionGate) { return _sessionCounted.Count; } }
    }

    public Baseline Compute(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return new Baseline(0, 0);

        var withinCall = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bytes = 0L;
        var counted = 0;

        foreach (var path in paths)
        {
            var absolute = ToAbsolutePath(path);
            if (absolute is null) continue;

            if (!withinCall.Add(absolute)) continue;

            lock (_sessionGate)
            {
                if (!_sessionCounted.Add(absolute)) continue;
            }

            var length = GetLength(absolute);
            if (length <= 0) continue;

            bytes += length;
            counted++;
        }

        var tokens = TokenEstimator.FromBytes(bytes);

        // Cap after conversion so the comparison happens in the same unit as the claim.
        var cap = TokenEstimator.FromBytes(SafeRepoTotalBytes());
        if (cap > 0 && tokens > cap) tokens = cap;

        return new Baseline(tokens, counted);
    }

    /// <summary>
    /// Responses carry solution-relative paths for token efficiency, so they have to be
    /// rebased before they can be stat'ed. Absolute paths are passed through.
    /// </summary>
    private string? ToAbsolutePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        try
        {
            if (Path.IsPathRooted(path)) return Path.GetFullPath(path);

            var root = solutionDirectory();
            return root is null ? null : Path.GetFullPath(Path.Combine(root, path));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>
    /// File length, cached by write time so an edited file is re-measured but an unchanged
    /// one is not. A missing or unreadable file yields zero — the spec's rule is to record
    /// the call with no baseline rather than drop it.
    /// </summary>
    private long GetLength(string absolutePath)
    {
        try
        {
            var info = new FileInfo(absolutePath);
            if (!info.Exists) return 0;

            var stamp = info.LastWriteTimeUtc;

            if (_sizes.TryGetValue(absolutePath, out var cached) && cached.LastWriteUtc == stamp)
                return cached.Length;

            _sizes[absolutePath] = new CachedSize(stamp, info.Length);
            return info.Length;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private long SafeRepoTotalBytes()
    {
        try
        {
            return repoTotalBytes();
        }
        catch (Exception)
        {
            return 0;
        }
    }
}
