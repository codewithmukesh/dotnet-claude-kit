using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CWM.RoslynNavigator.Telemetry;

/// <summary>
/// Drains the telemetry queue to disk in the background.
///
/// <para>Records are batched: a burst of calls becomes one append rather than one per call.
/// The drain loop is the only writer, which is why the channel is configured
/// <c>SingleReader</c>.</para>
/// </summary>
internal sealed class TelemetryWriter(ToolTelemetry telemetry, ILogger<TelemetryWriter> logger)
    : BackgroundService
{
    private const int MaxBatch = 64;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!telemetry.Enabled) return;

        var batch = new List<CallRecord>(MaxBatch);

        try
        {
            while (await telemetry.Queue.WaitToReadAsync(stoppingToken))
            {
                batch.Clear();

                while (batch.Count < MaxBatch && telemetry.Queue.TryRead(out var record))
                    batch.Add(record);

                telemetry.Store.Append(batch);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown. Flush whatever is still queued so the last few calls are not lost.
            FlushRemaining();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Telemetry writer stopped.");
        }
    }

    private void FlushRemaining()
    {
        var remaining = new List<CallRecord>();
        while (remaining.Count < MaxBatch && telemetry.Queue.TryRead(out var record))
            remaining.Add(record);

        telemetry.Store.Append(remaining);
    }
}
