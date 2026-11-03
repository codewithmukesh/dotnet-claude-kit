using CWM.RoslynNavigator.Tests.Fixtures;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CWM.RoslynNavigator.Tests;

/// <summary>
/// The workspace reports Ready as soon as the solution is open and compiles in the
/// background. Tool calls that race the warm-up must still get complete, ordered answers.
/// </summary>
public sealed class WorkspaceWarmUpTests : IDisposable
{
    private readonly WorkspaceManager _workspace;

    public WorkspaceWarmUpTests()
    {
        TestSolutionFixture.RegisterMSBuild();
        _workspace = new WorkspaceManager(NullLoggerFactory.Instance.CreateLogger<WorkspaceManager>(), TimeProvider.System);
    }

    [Fact]
    public async Task Load_WarmUpCompletes_EveryProjectCompiled()
    {
        var ct = TestContext.Current.CancellationToken;
        await _workspace.LoadSolutionAsync(TestSolutionFixture.SampleSolutionPath, ct);

        Assert.Equal(WorkspaceState.Ready, _workspace.State);

        await _workspace.WaitForWarmUpAsync();

        Assert.All(_workspace.GetSolution()!.Projects, p => Assert.True(p.TryGetCompilation(out _), p.Name));
    }

    [Fact]
    public async Task FindSymbols_DuringWarmUp_ReturnsCompleteResults()
    {
        var ct = TestContext.Current.CancellationToken;
        await _workspace.LoadSolutionAsync(TestSolutionFixture.SampleSolutionPath, ct);

        // Issued immediately, racing the background warm-up for the same compilations.
        var lookups = await Task.WhenAll(
            SymbolResolver.FindSymbolsByNameAsync(_workspace, "OrderService", ct: ct),
            SymbolResolver.FindSymbolsByNameAsync(_workspace, "Order", "class", ct),
            SymbolResolver.FindSymbolsByNameAsync(_workspace, "IOrderRepository", ct: ct));

        Assert.Single(lookups[0]);
        Assert.Single(lookups[1]);
        Assert.Single(lookups[2]);
    }

    [Fact]
    public async Task GetAllCompilations_Concurrent_PreservesProjectOrder()
    {
        var ct = TestContext.Current.CancellationToken;
        await _workspace.LoadSolutionAsync(TestSolutionFixture.SampleSolutionPath, ct);

        var compilations = await _workspace.GetAllCompilationsAsync(ct);

        var expected = _workspace.GetSolution()!.ProjectIds
            .Select(id => _workspace.GetSolution()!.GetProject(id)!.AssemblyName);
        Assert.Equal(expected, compilations.Select(c => c.AssemblyName));
    }

    [Fact]
    public async Task Reload_CancelsThePreviousWarmUp()
    {
        var ct = TestContext.Current.CancellationToken;
        await _workspace.LoadSolutionAsync(TestSolutionFixture.SampleSolutionPath, ct);
        var firstWarmUp = _workspace.WaitForWarmUpAsync();

        await _workspace.LoadSolutionAsync(TestSolutionFixture.SampleSolutionPath, ct);

        // Cancelled (or already finished) — never left compiling a discarded snapshot, and
        // never faulted: cancellation is swallowed inside the warm-up.
        await firstWarmUp.WaitAsync(TimeSpan.FromSeconds(30), ct);
        Assert.True(firstWarmUp.IsCompletedSuccessfully);
        Assert.Equal(WorkspaceState.Ready, _workspace.State);

        await _workspace.WaitForWarmUpAsync();
        Assert.All(_workspace.GetSolution()!.Projects, p => Assert.True(p.TryGetCompilation(out _), p.Name));
    }

    public void Dispose() => _workspace.Dispose();
}
