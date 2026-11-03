using CWM.RoslynNavigator.Tests.Fixtures;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CWM.RoslynNavigator.Tests;

[Collection(SampleSolutionCollection.Name)]
public class WorkspaceManagerTests(TestSolutionFixture fixture)
{
    [Fact]
    public void State_ShouldBeReady_AfterLoading()
    {
        Assert.Equal(WorkspaceState.Ready, fixture.WorkspaceManager.State);
    }

    [Fact]
    public void ProjectCount_ShouldBeThree()
    {
        Assert.Equal(3, fixture.WorkspaceManager.ProjectCount);
    }

    [Fact]
    public void GetSolution_ShouldReturnNonNull()
    {
        var solution = fixture.WorkspaceManager.GetSolution();
        Assert.NotNull(solution);
    }

    [Fact]
    public async Task GetCompilationAsync_ShouldReturnCompilation_ForValidProject()
    {
        var solution = fixture.WorkspaceManager.GetSolution()!;
        var project = solution.Projects.First();

        var compilation = await fixture.WorkspaceManager.GetCompilationAsync(project.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(compilation);
    }

    [Fact]
    public async Task GetAllCompilationsAsync_ShouldReturnAll()
    {
        var compilations = await fixture.WorkspaceManager.GetAllCompilationsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, compilations.Count);
    }

    [Fact]
    public void GetStatusMessage_ShouldReturnReady()
    {
        var message = fixture.WorkspaceManager.GetStatusMessage();

        Assert.Equal("Workspace is ready.", message);
    }

    [Fact]
    public async Task EnsureReadyOrStatusAsync_ShouldReturnNull_WhenAlreadyReady()
    {
        var result = await fixture.WorkspaceManager.EnsureReadyOrStatusAsync(TestContext.Current.CancellationToken);

        Assert.Null(result);
    }
}
