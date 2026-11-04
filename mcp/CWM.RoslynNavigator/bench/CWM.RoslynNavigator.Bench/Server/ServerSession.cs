using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CWM.RoslynNavigator.Telemetry;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CWM.RoslynNavigator.Bench.Server;

/// <summary>
/// One server process driven over real MCP stdio, exactly as an agent host would drive it.
/// Knows nothing about the server's internals: readiness is observed by polling a tool, and
/// compilation warm-up by the server's own stderr log line.
/// </summary>
internal sealed class ServerSession : IAsyncDisposable
{
    /// <summary>Logged by every build that warms compilations; absent means "not observable".</summary>
    private const string WarmMarker = "All compilations warmed";

    /// <summary>Logged when background warm-up throws; there will be no warm line after it.</summary>
    private const string WarmFailedMarker = "warm-up failed";

    private static readonly string[] LoadProblemMarkers =
        ["Workspace failure", "Failed to load", "not found:", "Failed to read solution"];

    private static readonly TimeSpan ReadyPollInterval = TimeSpan.FromMilliseconds(250);

    private readonly Stopwatch _sinceStart;
    private readonly McpClient _client;
    private readonly ConcurrentQueue<string> _loadProblems;
    private readonly ConcurrentQueue<string> _stderrTail;
    private readonly TaskCompletionSource<long> _warm;
    private readonly Dictionary<string, HashSet<string>> _toolParameters;

    private ServerSession(
        Stopwatch sinceStart,
        McpClient client,
        ConcurrentQueue<string> loadProblems,
        ConcurrentQueue<string> stderrTail,
        TaskCompletionSource<long> warm,
        Dictionary<string, HashSet<string>> toolParameters)
    {
        _sinceStart = sinceStart;
        _client = client;
        _loadProblems = loadProblems;
        _stderrTail = stderrTail;
        _warm = warm;
        _toolParameters = toolParameters;
    }

    public bool IsAlive { get; private set; } = true;

    public IReadOnlyCollection<string> ToolNames => _toolParameters.Keys;

    public string ServerVersion => _client.ServerInfo?.Version ?? "unknown";

    /// <summary>Distinct load problems the server logged (project load failures, missing projects).</summary>
    public IReadOnlyList<string> LoadProblems => [.. _loadProblems.Distinct().Take(100)];

    /// <summary>The last stderr lines the server wrote — where a tool's exception detail lands.</summary>
    public IReadOnlyList<string> StderrTail => [.. _stderrTail];

    public static async Task<ServerSession> StartAsync(
        ServerBuild build, string solutionPath, CancellationToken ct, IReadOnlyList<string>? extraArguments = null)
    {
        var loadProblems = new ConcurrentQueue<string>();
        var stderrTail = new ConcurrentQueue<string>();
        var warm = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sinceStart = Stopwatch.StartNew();

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = build.Label,
            Command = build.Command,
            Arguments = build.Arguments(solutionPath, extraArguments),
            WorkingDirectory = Path.GetDirectoryName(solutionPath),
            // Telemetry off: its background writer is not part of what is being measured.
            EnvironmentVariables = new Dictionary<string, string?> { ["CWM_ROSLYN_TELEMETRY"] = "off" },
            ShutdownTimeout = TimeSpan.FromSeconds(10),
            StandardErrorLines = line =>
            {
                stderrTail.Enqueue(line);
                while (stderrTail.Count > 200) stderrTail.TryDequeue(out _);
                if (line.Contains(WarmMarker, StringComparison.Ordinal))
                    warm.TrySetResult(sinceStart.ElapsedMilliseconds);
                else if (line.Contains(WarmFailedMarker, StringComparison.OrdinalIgnoreCase))
                    warm.TrySetResult(-1);
                else if (Array.Exists(LoadProblemMarkers, m => line.Contains(m, StringComparison.Ordinal)))
                    loadProblems.Enqueue(line.Trim());
            },
        });

        var client = await McpClient.CreateAsync(transport, cancellationToken: ct);
        var tools = await client.ListToolsAsync(cancellationToken: ct);

        var parameters = tools.ToDictionary(
            t => t.Name,
            t => SchemaProperties(t.ProtocolTool.InputSchema),
            StringComparer.Ordinal);

        return new ServerSession(sinceStart, client, loadProblems, stderrTail, warm, parameters);
    }

    public bool HasTool(string name) => _toolParameters.ContainsKey(name);

    /// <summary>
    /// Polls a cheap tool until the workspace stops answering with a loading status.
    /// Returns milliseconds since process start, or null if it never became ready.
    /// </summary>
    public async Task<long?> WaitForReadyAsync(TimeSpan timeout, CancellationToken ct)
    {
        var deadline = _sinceStart.Elapsed + timeout;
        while (_sinceStart.Elapsed < deadline && IsAlive)
        {
            var probe = await CallAsync("get_project_graph", [], TimeSpan.FromMinutes(2), ct);
            if (probe.Status is "ok" or "error") return _sinceStart.ElapsedMilliseconds;
            if (probe.Status is "transport-error") return null;
            if (probe.Payload?.Contains("\"Error\"", StringComparison.Ordinal) == true
                && probe.Payload.Contains("\"State\"", StringComparison.Ordinal))
            {
                return null; // Workspace state Error: it will never become ready.
            }

            await Task.Delay(ReadyPollInterval, ct);
        }

        return null;
    }

    /// <summary>Milliseconds since process start at which warm-up finished, or null if not seen in time.</summary>
    public async Task<long?> WaitForWarmAsync(TimeSpan timeout, CancellationToken ct)
    {
        var finished = await Task.WhenAny(_warm.Task, Task.Delay(timeout, ct));
        return finished == _warm.Task && _warm.Task.Result >= 0 ? _warm.Task.Result : null;
    }

    public long ElapsedSinceStartMs => _sinceStart.ElapsedMilliseconds;

    public async Task<ToolCallResult> CallAsync(
        string tool,
        Dictionary<string, object?> arguments,
        TimeSpan timeout,
        CancellationToken ct)
    {
        if (!_toolParameters.TryGetValue(tool, out var accepted)) return ToolCallResult.NotAvailable;

        // A parameter an older build does not know is dropped rather than sent: the question
        // asked is then the closest one that build can answer.
        var supported = arguments
            .Where(a => a.Value is not null && (accepted.Count == 0 || accepted.Contains(a.Key)))
            .ToDictionary(a => a.Key, a => a.Value);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(timeout);
        var started = Stopwatch.GetTimestamp();

        try
        {
            var result = await _client.CallToolAsync(tool, supported, cancellationToken: linked.Token);
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var payload = result.Content?.OfType<TextContentBlock>().FirstOrDefault()?.Text;
            var bytes = payload is null ? 0 : Encoding.UTF8.GetByteCount(payload);

            if (result.IsError == true)
                return new ToolCallResult("error", elapsed, payload, bytes, "ToolException", Truncate(payload));

            var facts = ResponseInspector.Inspect(payload);
            return facts.Outcome switch
            {
                CallOutcome.Error => new ToolCallResult("error", elapsed, payload, bytes, facts.ErrorCode, Truncate(payload)),
                CallOutcome.NotReady => new ToolCallResult("not-ready", elapsed, payload, bytes, null, Truncate(payload)),
                _ => new ToolCallResult("ok", elapsed, payload, bytes, null, null),
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            return new ToolCallResult("timeout", elapsed, null, 0, "Timeout", $"No response within {timeout.TotalSeconds:0} s.");
        }
        catch (McpException e)
        {
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            return new ToolCallResult("error", elapsed, null, 0, "McpException", Truncate(e.Message));
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or ObjectDisposedException)
        {
            IsAlive = false;
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            return new ToolCallResult("transport-error", elapsed, null, 0, e.GetType().Name, Truncate(e.Message));
        }
    }

    private static HashSet<string> SchemaProperties(JsonElement schema)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (schema.ValueKind == JsonValueKind.Object
            && schema.TryGetProperty("properties", out var properties)
            && properties.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in properties.EnumerateObject()) names.Add(property.Name);
        }

        return names;
    }

    private static string? Truncate(string? text) =>
        text is null ? null : text.Length <= 400 ? text : text[..400] + "…";

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _client.DisposeAsync();
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or OperationCanceledException)
        {
            // The process may already be gone; shutting it down is best effort.
        }
    }
}
