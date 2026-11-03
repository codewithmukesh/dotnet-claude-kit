using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CWM.RoslynNavigator.Tests;

/// <summary>
/// A server started without a solution discovers one from the client's MCP roots — one
/// solution per root — and follows the roots when the client reports that they changed.
/// </summary>
public sealed class RootsDiscoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cwm-rn-roots-" + Guid.NewGuid().ToString("N"));
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly WorkspaceManager _workspace;
    private IReadOnlyList<string> _roots = [];
    private int _rootRequests;

    public RootsDiscoveryTests()
    {
        TestSolutionFixture.RegisterMSBuild();
        BuildFileChangeDetectionTests.CopyDirectory(
            Path.GetDirectoryName(TestSolutionFixture.SampleSolutionPath)!, SampleRoot);
        BuildFileChangeDetectionTests.CopyDirectory(
            Path.GetDirectoryName(TestSolutionFixture.SecondSolutionPath)!, SecondRoot);
        Directory.CreateDirectory(EmptyRoot);

        _workspace = new WorkspaceManager(NullLoggerFactory.Instance.CreateLogger<WorkspaceManager>(), _clock)
        {
            RootsProvider = _ =>
            {
                Interlocked.Increment(ref _rootRequests);
                return Task.FromResult(_roots);
            }
        };
    }

    private string SampleRoot => Path.Combine(_root, "SampleSolution");
    private string SecondRoot => Path.Combine(_root, "SecondSolution");
    private string EmptyRoot => Path.Combine(_root, "docs");

    [Fact]
    public async Task MultipleRoots_EachWithASolution_LoadsThemAllIntoOneWorkspace()
    {
        var ct = TestContext.Current.CancellationToken;
        _roots = [SampleRoot, SecondRoot];

        Assert.Null(await _workspace.EnsureReadyOrStatusAsync(ct));

        Assert.Equal(["SampleSolution.sln", "SecondSolution.slnx"], _workspace.LoadedSolutions.Select(s => s.Name));
        Assert.Equal(4, _workspace.ProjectCount);
        Assert.Equal(Path.GetFullPath(_root), _workspace.SolutionDirectory);
    }

    [Fact]
    public async Task RootWithoutSolution_IsSkipped_OthersLoad()
    {
        var ct = TestContext.Current.CancellationToken;
        _roots = [EmptyRoot, SampleRoot, Path.Combine(_root, "does-not-exist")];

        Assert.Null(await _workspace.EnsureReadyOrStatusAsync(ct));

        Assert.Equal("SampleSolution.sln", Assert.Single(_workspace.LoadedSolutions).Name);
    }

    [Fact]
    public async Task NoRootHasASolution_ReportsNotStarted_AndLooksAgainAfterCooldown()
    {
        var ct = TestContext.Current.CancellationToken;
        _roots = [EmptyRoot];

        var status = JsonSerializer.Deserialize<StatusResponse>((await _workspace.EnsureReadyOrStatusAsync(ct))!)!;
        Assert.Equal("NotStarted", status.State);
        Assert.Equal(1, _rootRequests);

        // Not on every call...
        Assert.NotNull(await _workspace.EnsureReadyOrStatusAsync(ct));
        Assert.Equal(1, _rootRequests);

        // ...but once the cooldown has passed: the solution has been created meanwhile.
        BuildFileChangeDetectionTests.CopyDirectory(SampleRoot, EmptyRoot);
        _clock.Advance(TimeSpan.FromSeconds(31));
        Assert.Null(await _workspace.EnsureReadyOrStatusAsync(ct));
        Assert.Equal(2, _rootRequests);
        Assert.Equal(WorkspaceState.Ready, _workspace.State);
    }

    [Fact]
    public async Task RootsChanged_NoSolutionYet_RediscoversOnTheNextCall()
    {
        var ct = TestContext.Current.CancellationToken;
        _roots = [EmptyRoot];
        Assert.NotNull(await _workspace.EnsureReadyOrStatusAsync(ct));

        _roots = [EmptyRoot, SampleRoot];
        _workspace.OnRootsChanged();

        Assert.Null(await _workspace.EnsureReadyOrStatusAsync(ct));
        Assert.Equal("SampleSolution.sln", Assert.Single(_workspace.LoadedSolutions).Name);
    }

    [Fact]
    public async Task RootsChanged_WhileReady_ReloadsInBackgroundWithTheNewSolutions()
    {
        var ct = TestContext.Current.CancellationToken;
        _roots = [SampleRoot];
        Assert.Null(await _workspace.EnsureReadyOrStatusAsync(ct));
        Assert.Equal(1, _workspace.LoadCount);

        _roots = [SampleRoot, SecondRoot];
        _workspace.OnRootsChanged();

        // Answers immediately from the current snapshot; the new solution set loads behind it.
        Assert.Null(await _workspace.EnsureReadyOrStatusAsync(ct));
        await _workspace.WaitForReloadAsync();

        Assert.Equal(2, _workspace.LoadCount);
        Assert.Equal(2, _workspace.LoadedSolutions.Count);
        Assert.Equal(4, _workspace.ProjectCount);
    }

    [Fact]
    public async Task RootsChanged_SameSolutions_DoesNotReload()
    {
        var ct = TestContext.Current.CancellationToken;
        _roots = [SampleRoot];
        Assert.Null(await _workspace.EnsureReadyOrStatusAsync(ct));

        _roots = [SampleRoot, EmptyRoot];
        _workspace.OnRootsChanged();
        Assert.Null(await _workspace.EnsureReadyOrStatusAsync(ct));
        await _workspace.WaitForReloadAsync();

        Assert.Equal(2, _rootRequests);
        Assert.Equal(1, _workspace.LoadCount);
    }

    [Fact]
    public async Task ConfiguredSolution_RootsAreNeverConsulted()
    {
        var ct = TestContext.Current.CancellationToken;
        _roots = [SecondRoot];
        _workspace.HasConfiguredSolutions = true;

        // Before the configured load has even started, and after it.
        Assert.NotNull(await _workspace.EnsureReadyOrStatusAsync(ct));
        await _workspace.LoadSolutionAsync(Path.Combine(SampleRoot, "SampleSolution.sln"), ct);
        _workspace.OnRootsChanged();
        Assert.Null(await _workspace.EnsureReadyOrStatusAsync(ct));

        Assert.Equal(0, _rootRequests);
        Assert.Equal("SampleSolution.sln", Assert.Single(_workspace.LoadedSolutions).Name);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Temp folder; leaving it behind only costs disk space.
        }
    }
}
