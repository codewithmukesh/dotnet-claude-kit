using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CWM.RoslynNavigator.Tests.Fixtures;

/// <summary>
/// Loads SampleSolution.sln and SecondSolution.slnx into one workspace (#16). The two share
/// SampleDomain; SecondSolution adds ShippingService.
/// </summary>
public sealed class MultiSolutionFixture : IAsyncLifetime
{
    public WorkspaceManager WorkspaceManager { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        TestSolutionFixture.RegisterMSBuild();

        WorkspaceManager = new WorkspaceManager(
            NullLoggerFactory.Instance.CreateLogger<WorkspaceManager>(),
            TimeProvider.System);

        await WorkspaceManager.LoadSolutionsAsync(
            [TestSolutionFixture.SampleSolutionPath, TestSolutionFixture.SecondSolutionPath]);
    }

    public ValueTask DisposeAsync()
    {
        WorkspaceManager?.Dispose();
        return ValueTask.CompletedTask;
    }
}
