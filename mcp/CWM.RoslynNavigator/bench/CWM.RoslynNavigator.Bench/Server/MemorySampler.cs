using System.Diagnostics;

namespace CWM.RoslynNavigator.Bench.Server;

/// <summary>Peak memory seen for one server session.</summary>
internal sealed record MemoryPeaks(
    bool Supported,
    long ServerPeakBytes,
    long BuildHostPeakBytes,
    long CombinedPeakBytes,
    int BuildHostProcesses);

/// <summary>
/// Samples the server process and its children (MSBuild build hosts) every half second.
/// The server's own figure is the OS-reported peak working set, which is exact; the
/// combined figure is the largest simultaneous total seen at a sample, so it can miss a
/// spike shorter than the interval.
/// </summary>
internal sealed class MemorySampler : IAsyncDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Processes that are plumbing, not server: the console host, and the shell the stdio
    /// transport wraps the command in on Windows.
    /// </summary>
    private static readonly string[] Plumbing = ["conhost", "cmd", "sh", "bash"];

    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<int, long> _peaks = [];
    private readonly HashSet<int> _buildHosts = [];
    private readonly Lock _gate = new();
    private readonly Task _loop;
    private int _serverPid;
    private long _combinedPeak;

    public MemorySampler()
    {
        _loop = ProcessTree.IsSupported ? Task.Run(RunAsync) : Task.CompletedTask;
    }

    public MemoryPeaks Snapshot()
    {
        if (!ProcessTree.IsSupported) return new MemoryPeaks(false, 0, 0, 0, 0);

        Sample();
        lock (_gate)
        {
            var server = _peaks.GetValueOrDefault(_serverPid);
            var hosts = _buildHosts.Sum(pid => _peaks.GetValueOrDefault(pid));
            return new MemoryPeaks(true, server, hosts, _combinedPeak, _buildHosts.Count);
        }
    }

    private async Task RunAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            Sample();
            try
            {
                await Task.Delay(Interval, _stop.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void Sample()
    {
        var processes = new List<(int Pid, int Depth, long WorkingSet, long Peak)>();
        foreach (var (pid, depth) in ProcessTree.Descendants(Environment.ProcessId))
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                if (Array.Exists(Plumbing, p => p.Equals(process.ProcessName, StringComparison.OrdinalIgnoreCase))) continue;
                processes.Add((pid, depth, process.WorkingSet64, Math.Max(process.PeakWorkingSet64, process.WorkingSet64)));
            }
            catch (ArgumentException)
            {
                // Exited between the snapshot and the lookup.
            }
            catch (InvalidOperationException)
            {
            }
        }

        if (processes.Count == 0) return;

        // The server is the shallowest real process under the harness; everything deeper is
        // one of its MSBuild build hosts.
        var serverDepth = processes.Min(p => p.Depth);

        lock (_gate)
        {
            foreach (var (pid, depth, _, peak) in processes)
            {
                _peaks[pid] = Math.Max(_peaks.GetValueOrDefault(pid), peak);
                if (depth == serverDepth) _serverPid = pid;
                else _buildHosts.Add(pid);
            }

            _combinedPeak = Math.Max(_combinedPeak, processes.Sum(p => p.WorkingSet));
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _loop;
        _stop.Dispose();
    }
}
