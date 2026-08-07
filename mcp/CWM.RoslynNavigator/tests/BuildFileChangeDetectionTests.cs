using CWM.RoslynNavigator.Tests.Fixtures;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CWM.RoslynNavigator.Tests;

/// <summary>
/// A solution reload is the most expensive operation the server performs. These tests pin
/// down when one actually happens, using an isolated copy of the sample solution so build
/// files can be mutated without touching the repository.
/// </summary>
public sealed class BuildFileChangeDetectionTests : IDisposable
{
    private static readonly TimeSpan PastCooldown = TimeSpan.FromSeconds(10);

    private readonly string _root;
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly WorkspaceManager _workspace;

    public BuildFileChangeDetectionTests()
    {
        TestSolutionFixture.RegisterMSBuild();

        _root = Path.Combine(Path.GetTempPath(), "cwm-rn-" + Guid.NewGuid().ToString("N"));
        CopyDirectory(Path.GetDirectoryName(TestSolutionFixture.SampleSolutionPath)!, _root);

        _workspace = new WorkspaceManager(
            NullLoggerFactory.Instance.CreateLogger<WorkspaceManager>(), _clock);
    }

    private string SolutionPath => Path.Combine(_root, "SampleSolution.sln");
    private string ApiProjectPath => Path.Combine(_root, "SampleApi", "SampleApi.csproj");

    [Fact]
    public async Task Refresh_ProjectFileRewrittenWithIdenticalContent_DoesNotReload()
    {
        var ct = TestContext.Current.CancellationToken;
        await _workspace.LoadSolutionAsync(SolutionPath, ct);
        var loadsAfterInitial = _workspace.LoadCount;

        // What a branch switch or a formatter does: same bytes, new timestamp.
        var content = await File.ReadAllTextAsync(ApiProjectPath, ct);
        await File.WriteAllTextAsync(ApiProjectPath, content, ct);
        File.SetLastWriteTimeUtc(ApiProjectPath, DateTime.UtcNow.AddMinutes(1));

        _clock.Advance(PastCooldown);
        await _workspace.RefreshChangedDocumentsAsync(ct);

        Assert.Equal(loadsAfterInitial, _workspace.LoadCount);
        Assert.Equal(WorkspaceState.Ready, _workspace.State);
    }

    [Fact]
    public async Task Refresh_ProjectFileContentChanged_Reloads()
    {
        var ct = TestContext.Current.CancellationToken;
        await _workspace.LoadSolutionAsync(SolutionPath, ct);
        var loadsAfterInitial = _workspace.LoadCount;

        var content = await File.ReadAllTextAsync(ApiProjectPath, ct);
        await File.WriteAllTextAsync(
            ApiProjectPath,
            content.Replace("</Project>", "  <PropertyGroup><NoWarn>CS0219</NoWarn></PropertyGroup>\n</Project>"),
            ct);
        File.SetLastWriteTimeUtc(ApiProjectPath, DateTime.UtcNow.AddMinutes(1));

        _clock.Advance(PastCooldown);
        await _workspace.RefreshChangedDocumentsAsync(ct);

        Assert.Equal(loadsAfterInitial + 1, _workspace.LoadCount);
    }

    [Fact]
    public async Task Refresh_DirectoryPackagesPropsChanged_Reloads()
    {
        var ct = TestContext.Current.CancellationToken;

        // Not a document and not a csproj — previously invisible to change detection,
        // despite governing every package version in the solution.
        var propsPath = Path.Combine(_root, "Directory.Packages.props");
        await File.WriteAllTextAsync(propsPath, "<Project></Project>", ct);

        await _workspace.LoadSolutionAsync(SolutionPath, ct);
        var loadsAfterInitial = _workspace.LoadCount;

        await File.WriteAllTextAsync(
            propsPath, "<Project><PropertyGroup /></Project>", ct);
        File.SetLastWriteTimeUtc(propsPath, DateTime.UtcNow.AddMinutes(1));

        _clock.Advance(PastCooldown);
        await _workspace.RefreshChangedDocumentsAsync(ct);

        Assert.Equal(loadsAfterInitial + 1, _workspace.LoadCount);
    }

    [Fact]
    public async Task Refresh_NewSourceFile_DetectedByBackgroundScanNotInline()
    {
        var ct = TestContext.Current.CancellationToken;
        await _workspace.LoadSolutionAsync(SolutionPath, ct);
        var loadsAfterInitial = _workspace.LoadCount;

        await File.WriteAllTextAsync(
            Path.Combine(_root, "SampleApi", "BrandNewType.cs"),
            "namespace SampleApi;\n\ninternal sealed class BrandNewType;\n",
            ct);

        // First refresh only schedules the walk — it must not reload, and must not block
        // on the directory enumeration.
        _clock.Advance(TimeSpan.FromSeconds(90));
        await _workspace.RefreshChangedDocumentsAsync(ct);
        Assert.Equal(loadsAfterInitial, _workspace.LoadCount);

        await _workspace.WaitForStructuralScanAsync();

        // The next refresh acts on what the completed scan found.
        _clock.Advance(PastCooldown);
        await _workspace.RefreshChangedDocumentsAsync(ct);

        Assert.Equal(loadsAfterInitial + 1, _workspace.LoadCount);
        Assert.Contains(
            _workspace.GetSolution()!.Projects.SelectMany(p => p.Documents),
            d => d.Name == "BrandNewType.cs");
    }

    [Fact]
    public async Task Refresh_NoNewSourceFiles_BackgroundScanDoesNotTriggerReload()
    {
        var ct = TestContext.Current.CancellationToken;
        await _workspace.LoadSolutionAsync(SolutionPath, ct);
        var loadsAfterInitial = _workspace.LoadCount;

        _clock.Advance(TimeSpan.FromSeconds(90));
        await _workspace.RefreshChangedDocumentsAsync(ct);
        await _workspace.WaitForStructuralScanAsync();

        _clock.Advance(PastCooldown);
        await _workspace.RefreshChangedDocumentsAsync(ct);

        Assert.Equal(loadsAfterInitial, _workspace.LoadCount);
    }

    [Fact]
    public async Task Refresh_NothingChanged_DoesNotReload()
    {
        var ct = TestContext.Current.CancellationToken;
        await _workspace.LoadSolutionAsync(SolutionPath, ct);
        var loadsAfterInitial = _workspace.LoadCount;

        _clock.Advance(PastCooldown);
        await _workspace.RefreshChangedDocumentsAsync(ct);

        Assert.Equal(loadsAfterInitial, _workspace.LoadCount);
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

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        // Filter on the path relative to the source root. The source itself sits under the
        // test output directory, so an absolute-path check for "bin" matches everything.
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (IsBuildOutput(relative)) continue;

            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static bool IsBuildOutput(string relativePath)
    {
        foreach (var segment in relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("obj", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
