namespace CWM.RoslynNavigator.Bench;

/// <summary>Knobs for a run. Defaults are the values the committed results used.</summary>
internal sealed record BenchSettings
{
    /// <summary>Warm calls per tool after the cold call; median and p95 come from these.</summary>
    public int WarmIterations { get; init; } = 5;

    /// <summary>Per-call ceiling. A call that exceeds it is recorded as "timeout".</summary>
    public TimeSpan CallTimeout { get; init; } = TimeSpan.FromSeconds(300);

    /// <summary>A cold call slower than this skips its warm repeats; the cost is already clear.</summary>
    public TimeSpan WarmSkipThreshold { get; init; } = TimeSpan.FromSeconds(60);

    public TimeSpan ReadyTimeout { get; init; } = TimeSpan.FromMinutes(20);

    public TimeSpan WarmTimeout { get; init; } = TimeSpan.FromMinutes(20);

    /// <summary>Lines of search output an agent sees — Claude Code's Grep default head limit.</summary>
    public int GrepHeadLimit { get; init; } = 250;

    /// <summary>
    /// Modelling assumption for "time saved": input tokens a model processes per second.
    /// Not measured by the bench. See bench/README.md for the reasoning behind the default.
    /// </summary>
    public double PrefillTokensPerSecond { get; init; } = 10000;
}
