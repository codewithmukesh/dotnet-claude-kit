using CWM.RoslynNavigator.Tests.Fixtures;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CWM.RoslynNavigator.Tests;

/// <summary>
/// Reloads must bring back every configured solution, not just the first (#16). Uses an
/// isolated copy of both sample solutions so build files can be mutated.
/// </summary>
public sealed class MultiSolutionReloadTests : IDisposable
{
    private readonly string _root;
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly WorkspaceManager _workspace;

    public MultiSolutionReloadTests()
    {
        TestSolutionFixture.RegisterMSBuild();

        _root = Path.Combine(Path.GetTempPath(), "cwm-rn-multi-" + Guid.NewGuid().ToString("N"));
        BuildFileChangeDetectionTests.CopyDirectory(
            Path.GetDirectoryName(TestSolutionFixture.SampleSolutionPath)!, Path.Combine(_root, "SampleSolution"));
        BuildFileChangeDetectionTests.CopyDirectory(
            Path.GetDirectoryName(TestSolutionFixture.SecondSolutionPath)!, Path.Combine(_root, "SecondSolution"));

        _workspace = new WorkspaceManager(NullLoggerFactory.Instance.CreateLogger<WorkspaceManager>(), _clock);
    }

    private string[] SolutionPaths =>
    [
        Path.Combine(_root, "SampleSolution", "SampleSolution.sln"),
        Path.Combine(_root, "SecondSolution", "SecondSolution.slnx")
    ];

    [Fact]
    public async Task Refresh_ProjectInSecondSolutionChanged_ReloadsAllSolutions()
    {
        var ct = TestContext.Current.CancellationToken;
        await _workspace.LoadSolutionsAsync(SolutionPaths, ct);
        var loadsAfterInitial = _workspace.LoadCount;

        var projectPath = Path.Combine(_root, "SecondSolution", "ShippingService", "ShippingService.csproj");
        var content = await File.ReadAllTextAsync(projectPath, ct);
        await File.WriteAllTextAsync(
            projectPath,
            content.Replace("</Project>", "  <PropertyGroup><NoWarn>CS0219</NoWarn></PropertyGroup>\n</Project>"),
            ct);
        File.SetLastWriteTimeUtc(projectPath, DateTime.UtcNow.AddMinutes(1));

        _clock.Advance(TimeSpan.FromSeconds(10));
        await _workspace.RefreshChangedDocumentsAsync(ct);
        await _workspace.WaitForReloadAsync(); // reloads build the next snapshot in the background

        Assert.Equal(loadsAfterInitial + 1, _workspace.LoadCount);
        Assert.Equal(WorkspaceState.Ready, _workspace.State);
        Assert.Equal(4, _workspace.ProjectCount);
        Assert.Contains(_workspace.GetSolution()!.Projects, p => p.Name == "ShippingService");
        Assert.Equal(2, _workspace.LoadedSolutions.Count);
    }

    [Fact]
    public async Task Load_UnreadableAdditionalSolution_RecordsErrorAndKeepsTheRest()
    {
        var ct = TestContext.Current.CancellationToken;
        var broken = Path.Combine(_root, "Broken.slnx");
        await File.WriteAllTextAsync(broken, "<Solution><Project", ct);

        await _workspace.LoadSolutionsAsync([SolutionPaths[0], broken], ct);

        Assert.Equal(WorkspaceState.Ready, _workspace.State);
        Assert.Equal(3, _workspace.ProjectCount);
        Assert.NotNull(_workspace.LoadedSolutions.Single(s => s.Name == "Broken.slnx").Error);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A locked file in a temp copy is not worth failing a test over.
        }
    }
}
