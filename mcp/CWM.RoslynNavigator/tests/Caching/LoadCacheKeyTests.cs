using CWM.RoslynNavigator.Caching;

namespace CWM.RoslynNavigator.Tests.Caching;

/// <summary>
/// The cache key: a change to anything MSBuild reads (or would newly read) makes the entry a
/// miss, and a rewrite that leaves content unchanged does not.
/// </summary>
public sealed class LoadCacheKeyTests : IDisposable
{
    private readonly LoadCacheTestbed _bed = new();

    public static TheoryData<string, string> InputChanges => new()
    {
        { "project file edited", "SampleApi.csproj changed" },
        { "solution file edited", "SampleSolution.sln changed" },
        { "Directory.Build.props edited", "Directory.Build.props changed" },
        { "Directory.Build.props added beside a project", "Directory.Build.props is a new input" },
        { "global.json added", "global.json is a new input" },
        { "NuGet.config added", "NuGet.config is a new input" },
        { "project restored", "project.assets.json changed" },
        { "project deleted", "SampleTests.csproj was deleted" },
    };

    [Theory]
    [MemberData(nameof(InputChanges))]
    public async Task TryRead_InputChanged_MissesNamingTheInput(string change, string expectedReason)
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await _bed.LoadAndCacheAsync(ct);

        // Act
        ApplyChange(change);
        var (file, reason) = _bed.Store.TryRead(_bed.Header, ct);

        // Assert
        Assert.Null(file);
        Assert.Contains(expectedReason, reason);
    }

    [Fact]
    public async Task LoadSolution_ProjectFileEdited_LoadsWithMSBuildAndReportsWhy()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await _bed.LoadAndCacheAsync(ct);
        ApplyChange("project file edited");
        var workspace = _bed.CreateWorkspace();

        // Act
        await workspace.LoadSolutionAsync(_bed.SolutionPath, ct);
        await workspace.WaitForCacheWriteAsync();

        // Assert
        Assert.Equal("msbuild", workspace.CacheStatus!.Source);
        Assert.Contains("SampleApi.csproj changed", workspace.CacheStatus.Detail);
        Assert.Equal("hit", _bed.Store.TryRead(_bed.Header, ct).Reason); // rewritten for the next start
    }

    [Fact]
    public async Task TryRead_ImportedPropsEdited_Misses()
    {
        // Arrange: the root Directory.Build.props imports a file by a statically resolvable path.
        var ct = TestContext.Current.CancellationToken;
        var shared = Path.Combine(_bed.Data.Root, "build", "Shared.props");
        Directory.CreateDirectory(Path.GetDirectoryName(shared)!);
        await File.WriteAllTextAsync(shared, "<Project><PropertyGroup><Shared>1</Shared></PropertyGroup></Project>", ct);
        await File.WriteAllTextAsync(Path.Combine(_bed.Data.Root, "Directory.Build.props"),
            """<Project><Import Project="$(MSBuildThisFileDirectory)build\Shared.props" /></Project>""", ct);
        File.SetLastWriteTimeUtc(shared, DateTime.UtcNow.AddMinutes(-1));
        File.SetLastWriteTimeUtc(Path.Combine(_bed.Data.Root, "Directory.Build.props"), DateTime.UtcNow.AddMinutes(-1));
        await _bed.LoadAndCacheAsync(ct);
        Assert.Equal("hit", _bed.Store.TryRead(_bed.Header, ct).Reason);

        // Act
        await File.WriteAllTextAsync(shared, "<Project><PropertyGroup><Shared>2</Shared></PropertyGroup></Project>", ct);
        var (file, reason) = _bed.Store.TryRead(_bed.Header, ct);

        // Assert
        Assert.Null(file);
        Assert.Contains("Shared.props changed", reason);
    }

    [Fact]
    public async Task TryRead_BuildFileRewrittenWithSameContent_StillHits()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await _bed.LoadAndCacheAsync(ct);
        var project = _bed.SamplePath("SampleApi", "SampleApi.csproj");

        // Act: what a branch switch or a formatter does to unchanged files
        await File.WriteAllTextAsync(project, await File.ReadAllTextAsync(project, ct), ct);
        File.SetLastWriteTimeUtc(project, DateTime.UtcNow.AddMinutes(5));

        // Assert
        Assert.Equal("hit", _bed.Store.TryRead(_bed.Header, ct).Reason);
    }

    [Fact]
    public async Task TryRead_DifferentNavigatorBuildOrSdk_Misses()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await _bed.LoadAndCacheAsync(ct);

        // Act
        var otherBuild = _bed.Store.TryRead(_bed.Header with { NavigatorBuild = "another-build" }, ct);
        var otherSdk = _bed.Store.TryRead(_bed.Header with { Sdk = "another-sdk" }, ct);

        // Assert
        Assert.Equal((null, "navigator build changed"), otherBuild);
        Assert.Equal((null, ".NET SDK changed"), otherSdk);
    }

    [Fact]
    public async Task TryRead_AllTargetFrameworksOption_UsesItsOwnEntry()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await _bed.LoadAndCacheAsync(ct);
        var allFrameworks = LoadCacheStore.CreateHeader([_bed.SolutionPath], allTargetFrameworks: true);

        // Act
        var (file, reason) = _bed.Store.TryRead(allFrameworks, ct);

        // Assert
        Assert.Null(file);
        Assert.Equal("no cache entry yet", reason);
        Assert.NotEqual(_bed.Store.GetFilePath(_bed.Header), _bed.Store.GetFilePath(allFrameworks));
    }

    [Fact]
    public async Task LoadSolution_InputEditedDuringLoad_WritesNoEntry()
    {
        // Arrange: hold the load after MSBuild read the solution, then change an input.
        var ct = TestContext.Current.CancellationToken;
        var workspace = _bed.CreateWorkspace();
        var gate = SwapGate.Attach(workspace);
        var load = workspace.LoadSolutionAsync(_bed.SolutionPath, ct);
        await gate.Reached.WaitAsync(ct);
        await File.AppendAllTextAsync(_bed.SamplePath("SampleApi", "SampleApi.csproj"), "\n<!-- edited mid-load -->\n", ct);

        // Act
        gate.Release();
        await load;
        await workspace.WaitForCacheWriteAsync();

        // Assert: an entry would key the edited file to a snapshot that never saw it.
        Assert.Equal("no cache entry yet", _bed.Store.TryRead(_bed.Header, ct).Reason);
    }

    private void ApplyChange(string change)
    {
        switch (change)
        {
            case "project file edited":
                File.AppendAllText(_bed.SamplePath("SampleApi", "SampleApi.csproj"), "\n<!-- edited -->\n");
                break;
            case "solution file edited":
                File.AppendAllText(_bed.SolutionPath, "\n# edited\n");
                break;
            case "Directory.Build.props edited":
                File.WriteAllText(Path.Combine(_bed.Data.Root, "Directory.Build.props"),
                    "<Project><PropertyGroup><Edited>true</Edited></PropertyGroup></Project>");
                break;
            case "Directory.Build.props added beside a project":
                File.WriteAllText(_bed.SamplePath("SampleApi", "Directory.Build.props"), "<Project />");
                break;
            case "global.json added":
                File.WriteAllText(Path.Combine(_bed.SampleRoot, "global.json"), "{}");
                break;
            case "NuGet.config added":
                File.WriteAllText(Path.Combine(_bed.SampleRoot, "NuGet.config"), "<configuration />");
                break;
            case "project restored":
                // A restore that resolves a different package version rewrites the assets file.
                Directory.CreateDirectory(_bed.SamplePath("SampleApi", "obj"));
                File.WriteAllText(_bed.SamplePath("SampleApi", "obj", "project.assets.json"), "{ \"version\": 3 }");
                break;
            case "project deleted":
                Directory.Delete(_bed.SamplePath("SampleTests"), recursive: true);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change), change, null);
        }
    }

    public void Dispose() => _bed.Dispose();
}
