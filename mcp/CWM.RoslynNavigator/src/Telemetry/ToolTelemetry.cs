using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace CWM.RoslynNavigator.Telemetry;

/// <summary>
/// Records one line per tool call, and answers questions about what has been recorded.
///
/// <para>Writes go onto a bounded channel drained by <see cref="TelemetryWriter"/>, so a
/// tool call never waits on disk. The channel drops the oldest record when full: losing a
/// measurement is acceptable, making a user's query slower to keep one is not.</para>
///
/// <para>When telemetry is off — the default — <see cref="Record"/> returns on its first
/// branch and nothing else in this class is reached.</para>
/// </summary>
internal sealed class ToolTelemetry
{
    /// <summary>
    /// Enough headroom that a burst is never lost in practice, small enough that a stalled
    /// writer cannot grow memory without bound.
    /// </summary>
    private const int QueueCapacity = 1024;

    private readonly TelemetryOptions _options;
    private readonly WorkspaceManager _workspace;
    private readonly TelemetryStore _store;
    private readonly ILogger<ToolTelemetry> _logger;
    private readonly Channel<CallRecord> _queue;
    private readonly BaselineCalculator _baseline;
    private readonly Lock _initGate = new();

    private long _repoTotalBytes = -1;
    private bool _initialized;
    private bool _storeReady;
    private volatile bool _scaleRecorded;

    public ToolTelemetry(TelemetryOptions options, WorkspaceManager workspace, ILogger<ToolTelemetry> logger)
    {
        _options = options;
        _workspace = workspace;
        _logger = logger;
        _store = new TelemetryStore(options, logger);

        _queue = Channel.CreateBounded<CallRecord>(new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        // Both callbacks are deferred: at construction time the host is still being built
        // and no solution has been discovered yet.
        _baseline = new BaselineCalculator(() => workspace.SolutionDirectory, GetRepoTotalBytes);
    }

    public bool Enabled => _options.Enabled;

    public ChannelReader<CallRecord> Queue => _queue.Reader;

    internal TelemetryStore Store => _store;

    /// <summary>
    /// Records a completed call. Never throws: every failure path here is a measurement we
    /// lose, and none of them is worth failing a user's query over.
    /// </summary>
    /// <summary>
    /// The reporting tool is excluded from its own numbers. It answers from recorded data
    /// rather than from source, so it saves nothing, and counting it would inflate both the
    /// call count and the zero-result rate it reports.
    /// </summary>
    private const string ReportingTool = "get_token_savings";

    public void Record(string tool, string? responseJson, bool transportError, long elapsedMs)
    {
        if (!_options.Enabled || tool == ReportingTool) return;

        try
        {
            if (!EnsureInitialized()) return;

            var facts = ResponseInspector.Inspect(responseJson);

            // A ready workspace that produced results is the only case where the baseline
            // means anything; the others are recorded so miss rates stay visible.
            var state = facts.Outcome switch
            {
                CallOutcome.NotReady => TelemetryStates.Loading,
                CallOutcome.Error => TelemetryStates.Error,
                _ when transportError => TelemetryStates.Error,
                _ => TelemetryStates.Ok
            };

            var ok = state == TelemetryStates.Ok && facts.Results > 0;

            var baseline = ok ? _baseline.Compute(facts.Files) : new Baseline(0, 0);

            var record = new CallRecord
            {
                Timestamp = DateTimeOffset.UtcNow,
                Tool = tool,
                Ok = ok,
                Results = facts.Results,
                ResponseTokens = TokenEstimator.FromText(responseJson),
                BaselineTokens = baseline.Tokens,
                Files = baseline.Files,
                ElapsedMs = elapsedMs,
                State = state,
                ErrorCode = facts.ErrorCode
            };

            _queue.Writer.TryWrite(record);

            RecordRepoScaleOnce();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Telemetry record for {Tool} failed.", tool);
        }
    }

    /// <summary>
    /// Rewrites repo.json once the workspace is loaded.
    ///
    /// <para>The store is initialized on the first call, which is routinely a call that
    /// raced the workspace load — at that point the project count and source size are both
    /// zero. Left alone, those zeroes would stand for the whole session and every report
    /// would claim the repository has no source in it.</para>
    /// </summary>
    private void RecordRepoScaleOnce()
    {
        if (_scaleRecorded) return;

        var totalBytes = GetRepoTotalBytes();
        if (totalBytes <= 0) return; // Workspace still loading; try again on a later call.

        lock (_initGate)
        {
            if (_scaleRecorded) return;

            var solutionPath = _workspace.SolutionFilePath;
            if (solutionPath is null) return;

            _scaleRecorded = _store.Initialize(solutionPath, _workspace.ProjectCount, totalBytes);
        }
    }

    /// <summary>
    /// Defers storage setup until the first recorded call, because the solution path is not
    /// known at construction time — the workspace may still be discovering it.
    /// </summary>
    private bool EnsureInitialized()
    {
        lock (_initGate)
        {
            if (_initialized) return _storeReady;

            var solutionPath = _workspace.SolutionFilePath;
            if (solutionPath is null) return false; // Retry on a later call.

            _initialized = true;
            _storeReady = _store.Initialize(solutionPath, _workspace.ProjectCount, GetRepoTotalBytes());
            return _storeReady;
        }
    }

    /// <summary>
    /// Total source bytes in the solution, backing the per-call cap.
    ///
    /// <para>Only a non-zero result is cached. A call can arrive before the workspace has
    /// finished loading, when there are no known documents yet; caching the zero it returns
    /// would pin repo scale at nothing for the rest of the session and silently disable the
    /// cap.</para>
    /// </summary>
    private long GetRepoTotalBytes()
    {
        var cached = Interlocked.Read(ref _repoTotalBytes);
        if (cached > 0) return cached;

        var total = 0L;
        foreach (var path in _workspace.KnownDocumentPaths)
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Exists) total += info.Length;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // One unreadable file should not void the repo scale.
            }
        }

        if (total > 0) Interlocked.Exchange(ref _repoTotalBytes, total);
        return total;
    }
}
