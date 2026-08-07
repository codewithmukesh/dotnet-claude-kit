namespace CWM.RoslynNavigator.Tests.Fixtures;

/// <summary>
/// Manually advanced clock. WorkspaceManager gates its refresh work behind cooldowns, so
/// tests need to move time forward rather than sleep through them.
/// </summary>
public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}
