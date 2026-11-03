namespace CWM.RoslynNavigator.Tests.Fixtures;

/// <summary>
/// Manually advanced clock. WorkspaceManager gates its refresh work behind cooldowns, so
/// tests need to move time forward rather than sleep through them. Thread-safe: concurrency
/// tests read it from many tool calls while the test thread advances it.
/// </summary>
public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    private long _utcTicks = start.UtcTicks;

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);

    public void Advance(TimeSpan by) => Interlocked.Add(ref _utcTicks, by.Ticks);
}
