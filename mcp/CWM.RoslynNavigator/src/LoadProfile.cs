using System.Diagnostics;
using Microsoft.CodeAnalysis.MSBuild;

namespace CWM.RoslynNavigator;

/// <summary>
/// Where a workspace load spends its time. MSBuildWorkspace reports every project's evaluate,
/// design-time build, and resolve step through <see cref="IProgress{T}"/>; the totals, plus
/// the wall-clock time of the navigator's own steps, are logged once per load so a slow load
/// on a large solution can be attributed without a profiler.
/// </summary>
internal sealed class LoadProfile : IProgress<ProjectLoadProgress>
{
    private readonly Lock _gate = new();
    private readonly Dictionary<ProjectLoadOperation, (int Count, TimeSpan Total)> _operations = [];
    private readonly List<(string Step, long ElapsedMs)> _steps = [];
    private readonly long _started = Stopwatch.GetTimestamp();
    private long _stepStarted = Stopwatch.GetTimestamp();

    public void Report(ProjectLoadProgress value)
    {
        lock (_gate)
        {
            _operations.TryGetValue(value.Operation, out var current);
            _operations[value.Operation] = (current.Count + 1, current.Total + value.ElapsedTime);
        }
    }

    /// <summary>Ends the current step, attributing the time since the previous one to <paramref name="step"/>.</summary>
    public void Step(string step)
    {
        var now = Stopwatch.GetTimestamp();
        lock (_gate)
        {
            _steps.Add((step, (long)Stopwatch.GetElapsedTime(_stepStarted, now).TotalMilliseconds));
            _stepStarted = now;
        }
    }

    public long TotalMs => (long)Stopwatch.GetElapsedTime(_started).TotalMilliseconds;

    /// <summary>
    /// One line: wall-clock steps, then MSBuild's per-operation totals (summed across projects,
    /// so they exceed wall-clock time when the build host works in parallel).
    /// </summary>
    public string Summarize()
    {
        lock (_gate)
        {
            var steps = string.Join(", ", _steps.Select(s => $"{s.Step} {s.ElapsedMs} ms"));
            var operations = string.Join(", ", _operations
                .OrderBy(o => o.Key)
                .Select(o => $"{o.Key.ToString().ToLowerInvariant()} {o.Value.Count}x {(long)o.Value.Total.TotalMilliseconds} ms"));
            return operations.Length == 0
                ? $"{TotalMs} ms total; steps: {steps}"
                : $"{TotalMs} ms total; steps: {steps}; MSBuild (summed per project): {operations}";
        }
    }
}
