using System.Diagnostics;
using CWM.RoslynNavigator;
using CWM.RoslynNavigator.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

// MSBuild locator MUST be called before any Roslyn types are loaded.
// This resolves the MSBuild instance needed by MSBuildWorkspace.
MSBuildRegistration.Register();

var builder = Host.CreateApplicationBuilder(args);

// Configure logging. MCP stdio transport reserves stdout for JSON-RPC framing,
// so ALL log output must go to stderr or the client drops the connection.
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddConsole(options =>
    options.LogToStandardErrorThreshold = LogLevel.Trace);

// Register workspace services
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<WorkspaceManager>();
builder.Services.AddHostedService<WorkspaceInitializer>();

// Telemetry is opt-in (CWM_ROSLYN_TELEMETRY=on) and never leaves the machine.
// Registered unconditionally so the tool can explain that it is off; when disabled every
// entry point returns immediately and the writer never starts.
builder.Services.AddSingleton(TelemetryOptions.FromEnvironment());
builder.Services.AddSingleton<ToolTelemetry>();
builder.Services.AddHostedService<TelemetryWriter>();

// Configure MCP server with stdio transport.
//
// Measurement hangs off a single call-tool filter rather than a line in each
// tool method. One registration covers every tool including ones added later, and there
// is no per-tool line to forget — a forgotten line would be a silent measurement gap.
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithRequestFilters(filters => filters
    // An empty answer from a partially loaded workspace says so (see IncompleteWorkspaceNote).
    // Done here, once, so no tool's response shape changes and new tools are covered.
    // The same filter leases the current workspace for the call, so a background reload that
    // swaps in a new snapshot meanwhile disposes the old workspace only once the call is done.
    .AddCallToolFilter(next => async (context, ct) =>
    {
        var workspace = context.Services?.GetService<WorkspaceManager>();
        using var lease = workspace?.AcquireSnapshotLease();
        var result = await next(context, ct);
        return workspace is null || context.Params?.Name == "get_workspace_status"
            ? result
            : IncompleteWorkspaceNote.Apply(result, workspace);
    })
    .AddCallToolFilter(next => async (context, ct) =>
    {
        var telemetry = context.Services?.GetService<ToolTelemetry>();
        if (telemetry is not { Enabled: true })
            return await next(context, ct);

        var tool = context.Params?.Name ?? "unknown";
        var timestamp = Stopwatch.GetTimestamp();

        CallToolResult result;
        try
        {
            result = await next(context, ct);
        }
        catch (OperationCanceledException)
        {
            throw; // A cancelled call is not a failed one; leave it unrecorded.
        }
        catch (Exception)
        {
            telemetry.Record(tool, responseJson: null, transportError: true,
                elapsedMs: (long)Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds);
            throw;
        }

        // Tools return their JSON as a single text block; that payload is what the client
        // pays for, and it names the files the answer came from.
        var payload = result.Content?.OfType<TextContentBlock>().FirstOrDefault()?.Text;

        telemetry.Record(tool, payload, transportError: result.IsError == true,
            elapsedMs: (long)Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds);

        return result;
    }));

// A client whose roots change (a folder added to the workspace) says so; solutions discovered
// from roots are then rediscovered on the next call.
builder.Services.AddOptions<McpServerOptions>().Configure<WorkspaceManager>((options, workspace) =>
    options.Handlers.NotificationHandlers =
    [
        .. options.Handlers.NotificationHandlers ?? [],
        new(NotificationMethods.RootsListChangedNotification, (_, _) =>
        {
            workspace.OnRootsChanged();
            return ValueTask.CompletedTask;
        })
    ]);

// Discover solution paths: --solution is repeatable, --all-solutions loads every solution found.
var solutionPaths = SolutionDiscovery.FindSolutionPaths(args);
WorkspaceInitializer.SolutionPaths = solutionPaths;

var app = builder.Build();

var workspaceManager = app.Services.GetRequiredService<WorkspaceManager>();
workspaceManager.Services = app.Services;
workspaceManager.DiscoverAllSolutions = SolutionDiscovery.IsAllSolutionsMode(args);
WorkspaceOptions.Apply(args, workspaceManager);
workspaceManager.HasConfiguredSolutions = solutionPaths.Count > 0;

var logger = app.Services.GetRequiredService<ILogger<Program>>();
if (solutionPaths.Count > 0)
{
    foreach (var solutionPath in solutionPaths)
        logger.LogInformation("Discovered solution: {SolutionPath}", solutionPath);
}
else
{
    logger.LogWarning(
        "No .sln/.slnx file found in '{SearchDirectory}' (searched {MaxDepth} levels deep). " +
        "Tools will return not-ready status. Use --solution <path> to specify explicitly.",
        Directory.GetCurrentDirectory(),
        SolutionDiscovery.MaxSearchDepth);
}

await app.RunAsync();
