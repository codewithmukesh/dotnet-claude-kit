using System.Collections.Concurrent;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CWM.RoslynNavigator.Tests.Fixtures;

/// <summary>
/// The real server: the built CWM.RoslynNavigator.dll spawned over stdio exactly as an MCP
/// client launches it, against a private copy of the sample solution, connected through the
/// ModelContextProtocol client SDK. Covers what in-process tests cannot: parameter binding,
/// DI wiring, the call-tool filters in Program.cs, and the background-load path.
/// </summary>
public sealed class McpServerFixture : IAsyncLifetime
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromMinutes(3);

    private readonly ConcurrentQueue<string> _stderr = new();
    private TestDataCopy _copy = null!;
    private string _telemetryRoot = null!;

    public McpClient Client { get; private set; } = null!;

    /// <summary>The first tools/call answer, sent before waiting for the workspace.</summary>
    public string FirstCallResponse { get; private set; } = null!;

    /// <summary>
    /// Telemetry is switched on (into a private folder) so the telemetry call filter runs.
    /// Not on macOS: LocalApplicationData there resolves under $HOME, which cannot be
    /// redirected without also moving the SDK's and NuGet's folders.
    /// </summary>
    public static bool TelemetryEnabled => !OperatingSystem.IsMacOS();

    public string StandardError => string.Join(Environment.NewLine, _stderr);

    public async ValueTask InitializeAsync()
    {
        _copy = TestDataCopy.Create("cwm-rn-e2e");
        _telemetryRoot = Path.Combine(_copy.Root, "telemetry-home");
        Directory.CreateDirectory(_telemetryRoot);

        var options = new StdioClientTransportOptions
        {
            Name = "cwm-roslyn-navigator",
            Command = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            Arguments = [Path.Combine(AppContext.BaseDirectory, "CWM.RoslynNavigator.dll"), "--solution", _copy.SampleSolutionPath],
            WorkingDirectory = _copy.Root,
            StandardErrorLines = line => _stderr.Enqueue(line),
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["CWM_ROSLYN_TELEMETRY"] = TelemetryEnabled ? "on" : "off",
                ["LOCALAPPDATA"] = _telemetryRoot,
                ["XDG_DATA_HOME"] = _telemetryRoot,
                // The load cache stays inside the throwaway copy, never in the user's cache folder.
                ["CWM_ROSLYN_CACHE_DIR"] = Path.Combine(_copy.Root, "load-cache"),
            },
            ShutdownTimeout = TimeSpan.FromSeconds(10),
        };

        using var startup = new CancellationTokenSource(ReadyTimeout);
        Client = await McpClient.CreateAsync(new StdioClientTransport(options), cancellationToken: startup.Token);

        FirstCallResponse = await CallTextAsync("find_symbol", new Dictionary<string, object?> { ["name"] = "OrderService" }, startup.Token);
        await WaitUntilReadyAsync(startup.Token);
    }

    public async Task<CallToolResult> CallAsync(
        string tool, IReadOnlyDictionary<string, object?>? args, CancellationToken ct) =>
        await Client.CallToolAsync(tool, args ?? new Dictionary<string, object?>(), cancellationToken: ct);

    public async Task<string> CallTextAsync(
        string tool, IReadOnlyDictionary<string, object?>? args, CancellationToken ct) =>
        Text(await CallAsync(tool, args, ct));

    public static string Text(CallToolResult result) =>
        Assert.Single(result.Content.OfType<TextContentBlock>()).Text;

    public async ValueTask DisposeAsync()
    {
        if (Client is not null)
            await Client.DisposeAsync();

        _copy?.Dispose();
    }

    private async Task WaitUntilReadyAsync(CancellationToken ct)
    {
        while (true)
        {
            using var status = JsonDocument.Parse(await CallTextAsync("get_workspace_status", null, ct));
            var root = status.RootElement;
            if (root.GetProperty("State").GetString() == "Ready"
                && root.GetProperty("WarmUp").GetProperty("State").GetString() == "completed")
                return;

            if (root.GetProperty("State").GetString() == "Error")
                throw new InvalidOperationException($"Server failed to load the sample: {root}{Environment.NewLine}{StandardError}");

            await Task.Delay(250, ct);
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class McpServerCollection : ICollectionFixture<McpServerFixture>
{
    public const string Name = "McpServerProcess";
}
