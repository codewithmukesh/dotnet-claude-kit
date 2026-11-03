using CWM.RoslynNavigator.Telemetry;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CWM.RoslynNavigator.Tests.Telemetry;

/// <summary>
/// Recording a call must cost the request one queue write. Parsing the response, sizing
/// files, and preparing storage all happen when the queue is read.
/// </summary>
public sealed class TelemetryRequestPathTests(TestSolutionFixture fixture)
    : IClassFixture<TestSolutionFixture>, IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "cwm-tel-rp-" + Guid.NewGuid().ToString("N"));

    private ToolTelemetry CreateTelemetry(TimeProvider? clock = null) =>
        new(new TelemetryOptions(true, _root),
            fixture.WorkspaceManager,
            NullLoggerFactory.Instance.CreateLogger<ToolTelemetry>(),
            clock);

    [Fact]
    public async Task Record_DoesNoMeasurementWorkUntilTheQueueIsRead()
    {
        var telemetry = CreateTelemetry();
        var json = await FindSymbolTool.ExecuteAsync(
            fixture.WorkspaceManager, "OrderService", "class", ct: TestContext.Current.CancellationToken);

        telemetry.Record("find_symbol", json, transportError: false, elapsedMs: 3);

        // Storage is prepared lazily by the reader, so nothing has touched the disk yet.
        Assert.Null(telemetry.Store.RepoDirectory);
        Assert.False(Directory.Exists(_root));

        Assert.True(telemetry.Queue.TryRead(out var record));
        Assert.True(record.BaselineTokens > 0);
        Assert.NotNull(telemetry.Store.RepoDirectory);
    }

    [Fact]
    public void Record_UsesTheInjectedClockForTheCallTimestamp()
    {
        var at = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
        var telemetry = CreateTelemetry(new TestClock(at));

        telemetry.Record("find_symbol", "{\"Count\":0}", transportError: false, elapsedMs: 1);

        Assert.True(telemetry.Queue.TryRead(out var record));
        Assert.Equal(at, record.Timestamp);
    }

    [Fact]
    public void Record_MalformedPayload_StillRecordedInOrder()
    {
        var telemetry = CreateTelemetry();

        telemetry.Record("a", "{not json", transportError: false, elapsedMs: 1);
        telemetry.Record("b", null, transportError: true, elapsedMs: 2);

        Assert.True(telemetry.Queue.TryRead(out var first));
        Assert.True(telemetry.Queue.TryRead(out var second));
        Assert.Equal(("a", "b"), (first.Tool, second.Tool));
        Assert.Equal(TelemetryStates.Error, second.State);
        Assert.False(telemetry.Queue.TryRead(out _));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
