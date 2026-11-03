using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CWM.RoslynNavigator;

/// <summary>
/// Background service that initializes the workspace on application startup.
/// This runs the potentially slow solution loading in the background so the
/// MCP server can start accepting connections immediately.
/// </summary>
internal sealed class WorkspaceInitializer(
    WorkspaceManager workspaceManager,
    ILogger<WorkspaceInitializer> logger) : BackgroundService
{
    /// <summary>
    /// The solutions to load, primary first. Set by Program.cs from command-line args or discovery.
    /// </summary>
    public static IReadOnlyList<string> SolutionPaths { get; set; } = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (SolutionPaths.Count == 0)
        {
            logger.LogWarning("No solution path configured. Workspace will not be initialized. " +
                              "Pass --solution <path> or ensure a .sln/.slnx file exists in the working directory.");
            return;
        }

        try
        {
            logger.LogInformation("Initializing workspace with solution: {SolutionPath}", string.Join(", ", SolutionPaths));
            await workspaceManager.LoadSolutionsAsync(SolutionPaths, stoppingToken);
            logger.LogInformation(
                "Workspace ready: {ProjectCount} projects loaded. Compilations warm up in the background.",
                workspaceManager.ProjectCount);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Workspace initialization cancelled.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Workspace initialization failed.");
            // Don't rethrow — the server should still run, tools will return error status
        }
    }
}
