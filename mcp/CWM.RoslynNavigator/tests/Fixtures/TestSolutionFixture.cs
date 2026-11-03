using CWM.RoslynNavigator;
using Microsoft.Build.Locator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CWM.RoslynNavigator.Tests.Fixtures;

/// <summary>
/// Shared test fixture that loads the sample solution once per test collection.
/// Uses MSBuildLocator and WorkspaceManager to provide a warm workspace for all tests.
/// </summary>
public class TestSolutionFixture : IAsyncLifetime
{
    private static bool _msbuildRegistered;
    private static readonly object _registrationLock = new();

    public WorkspaceManager WorkspaceManager { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        EnsureMSBuildRegistered();

        var logger = NullLoggerFactory.Instance.CreateLogger<WorkspaceManager>();
        WorkspaceManager = new WorkspaceManager(logger, TimeProvider.System);

        var solutionPath = FindSampleSolutionPath();
        await WorkspaceManager.LoadSolutionAsync(solutionPath);
    }

    public ValueTask DisposeAsync()
    {
        WorkspaceManager?.Dispose();
        return ValueTask.CompletedTask;
    }

    private static void EnsureMSBuildRegistered()
    {
        lock (_registrationLock)
        {
            if (!_msbuildRegistered)
            {
                MSBuildLocator.RegisterDefaults();
                _msbuildRegistered = true;
            }
        }
    }

    /// <summary>
    /// Locates the sample solution for tests that need their own isolated WorkspaceManager
    /// instead of the shared one (e.g. reload behaviour, which mutates workspace state).
    /// </summary>
    public static string SampleSolutionPath => FindSampleSolutionPath();

    /// <summary>
    /// A second solution (.slnx) that shares SampleDomain with SampleSolution.sln and adds
    /// ShippingService — the multi-solution (#16) counterpart.
    /// </summary>
    public static string SecondSolutionPath => FindTestDataFile("SecondSolution", "SecondSolution.slnx");

    /// <summary>
    /// Registers MSBuild for tests that construct a WorkspaceManager directly.
    /// </summary>
    public static void RegisterMSBuild() => EnsureMSBuildRegistered();

    private static string FindSampleSolutionPath() => FindTestDataFile("SampleSolution", "SampleSolution.sln");

    private static string FindTestDataFile(string folder, string fileName)
    {
        // Walk up from the test output directory to find the TestData folder
        var directory = AppContext.BaseDirectory;
        while (directory is not null)
        {
            var candidatePath = Path.Combine(directory, "TestData", folder, fileName);
            if (File.Exists(candidatePath))
                return candidatePath;

            // Also check in tests/ subfolder
            candidatePath = Path.Combine(directory, "tests", "TestData", folder, fileName);
            if (File.Exists(candidatePath))
                return candidatePath;

            directory = Path.GetDirectoryName(directory);
        }

        throw new FileNotFoundException(
            $"Could not find {fileName}. Ensure TestData is copied to the output directory.");
    }
}
