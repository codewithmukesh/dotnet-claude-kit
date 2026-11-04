using CWM.RoslynNavigator.Caching;

namespace CWM.RoslynNavigator.Tests.Caching;

/// <summary>
/// A start with a valid cache entry is Ready from the cache, then verified by a real MSBuild
/// load in the background: an equivalent result keeps the cached snapshot (and its warm
/// compilations), a different one is swapped in. Files added or deleted between sessions are
/// reflected without waiting for the verification.
/// </summary>
public sealed class LoadCacheStartupTests : IDisposable
{
    private readonly LoadCacheTestbed _bed = new();

    [Fact]
    public async Task LoadSolution_NoCacheEntry_LoadsWithMSBuildAndWritesEntry()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var workspace = _bed.CreateWorkspace();

        // Act
        await workspace.LoadSolutionAsync(_bed.SolutionPath, ct);
        await workspace.WaitForCacheWriteAsync();

        // Assert
        var status = await LoadCacheTestbed.GetStatusAsync(workspace, ct);
        Assert.Equal("msbuild", status.LoadCache!.Source);
        Assert.Null(status.LoadCache.Verification);
        Assert.Contains("no cache entry yet", status.LoadCache.Detail);
        Assert.True(File.Exists(_bed.Store.GetFilePath(_bed.Header)));
        Assert.Equal("hit", _bed.Store.TryRead(_bed.Header, ct).Reason);
    }

    [Fact]
    public async Task LoadSolution_ValidEntry_IsReadyFromCacheBeforeMSBuildFinishes()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var original = await _bed.LoadAndCacheAsync(ct);
        var workspace = _bed.CreateWorkspace();
        var gate = SwapGate.Attach(workspace);

        // Act
        await workspace.LoadSolutionAsync(_bed.SolutionPath, ct);
        await gate.Reached.WaitAsync(ct); // MSBuild has loaded but not yet been compared

        // Assert
        Assert.Equal(WorkspaceState.Ready, workspace.State);
        Assert.False(workspace.IsReloading);
        Assert.Null(workspace.StalenessWarning);
        var status = await LoadCacheTestbed.GetStatusAsync(workspace, ct);
        Assert.Equal("cache", status.LoadCache!.Source);
        Assert.Equal("running", status.LoadCache.Verification);
        Assert.Contains("load cache", status.Message);
        Assert.Equal(
            original.GetSolution()!.Projects.Select(p => (p.Name, p.DocumentIds.Count)),
            workspace.GetSolution()!.Projects.Select(p => (p.Name, p.DocumentIds.Count)));
        Assert.Single(await SymbolResolver.FindSymbolsByNameAsync(workspace, "OrderService", ct: ct));

        gate.Release();
        await workspace.WaitForCacheVerificationAsync();
    }

    [Fact]
    public async Task Verification_EquivalentLoad_KeepsCachedSnapshotAndWarmCompilations()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await _bed.LoadAndCacheAsync(ct);
        var workspace = _bed.CreateWorkspace();
        var gate = SwapGate.Attach(workspace);
        await workspace.LoadSolutionAsync(_bed.SolutionPath, ct);
        var version = workspace.SnapshotVersion;
        var domain = workspace.GetSolution()!.Projects.Single(p => p.Name == "SampleDomain").Id;
        var compiled = await workspace.GetCompilationAsync(domain, ct);

        // Act
        gate.Release();
        await workspace.WaitForCacheVerificationAsync();

        // Assert
        var status = await LoadCacheTestbed.GetStatusAsync(workspace, ct);
        Assert.Equal("cache", status.LoadCache!.Source);
        Assert.Equal("unchanged", status.LoadCache.Verification);
        Assert.Equal(1, workspace.LoadCount);
        Assert.Equal(version, workspace.SnapshotVersion);
        Assert.Same(compiled, await workspace.GetCompilationAsync(domain, ct));
        Assert.Equal(1, workspace.LiveWorkspaceCount); // the verification's workspace was disposed
    }

    [Fact]
    public async Task Verification_DifferentLoad_SwapsInMSBuildSnapshotAndRewritesEntry()
    {
        // Arrange: an entry whose inputs still match but whose snapshot lacks a document,
        // which only the MSBuild load can notice.
        var ct = TestContext.Current.CancellationToken;
        await _bed.LoadAndCacheAsync(ct);
        var entry = _bed.Store.ReadEntry(_bed.Header)!;
        var helper = _bed.SamplePath("SampleDomain", "UnusedHelper.cs");
        _bed.Store.Write(entry with
        {
            Snapshot = entry.Snapshot with
            {
                Projects = [.. entry.Snapshot.Projects.Select(p => p with
                {
                    Documents = [.. p.Documents.Where(d => !string.Equals(d.FilePath, helper, StringComparison.OrdinalIgnoreCase))],
                })],
            },
        });
        var workspace = _bed.CreateWorkspace();

        // Act
        await workspace.LoadSolutionAsync(_bed.SolutionPath, ct);
        var fromCache = await SymbolResolver.FindSymbolsByNameAsync(workspace, "UnusedHelper", ct: ct);
        await workspace.WaitForCacheVerificationAsync();
        await workspace.WaitForCacheWriteAsync();

        // Assert
        Assert.Empty(fromCache);
        var status = await LoadCacheTestbed.GetStatusAsync(workspace, ct);
        Assert.Equal("msbuild", status.LoadCache!.Source);
        Assert.Equal("changed", status.LoadCache.Verification);
        Assert.Contains("SampleDomain: documents", status.LoadCache.Detail);
        Assert.Equal(2, workspace.LoadCount);
        Assert.Equal(1, workspace.LiveWorkspaceCount);
        Assert.NotEmpty(await SymbolResolver.FindSymbolsByNameAsync(workspace, "UnusedHelper", ct: ct));
        Assert.Contains(_bed.Store.ReadEntry(_bed.Header)!.Snapshot.Projects.SelectMany(p => p.Documents),
            d => string.Equals(d.FilePath, helper, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LoadSolution_DocumentDeletedBetweenSessions_IsLeftOutWithoutWaitingForMSBuild()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await _bed.LoadAndCacheAsync(ct);
        File.Delete(_bed.SamplePath("SampleDomain", "UnusedHelper.cs"));
        var workspace = _bed.CreateWorkspace();
        var gate = SwapGate.Attach(workspace);

        // Act
        await workspace.LoadSolutionAsync(_bed.SolutionPath, ct);

        // Assert: gone at once, and MSBuild agrees, so the cached snapshot stays.
        Assert.Empty(await SymbolResolver.FindSymbolsByNameAsync(workspace, "UnusedHelper", ct: ct));
        Assert.Contains("1 document(s) deleted", workspace.CacheStatus!.Detail);
        gate.Release();
        await workspace.WaitForCacheVerificationAsync();
        Assert.Equal("unchanged", workspace.CacheStatus!.Verification);
        Assert.Equal(1, workspace.LoadCount);
        Assert.Empty(await SymbolResolver.FindSymbolsByNameAsync(workspace, "UnusedHelper", ct: ct));
    }

    [Fact]
    public async Task LoadSolution_DocumentAddedBetweenSessions_IsFoundBeforeAndAfterVerification()
    {
        // Arrange: a new file in a project that compiles the SDK default glob.
        var ct = TestContext.Current.CancellationToken;
        await _bed.LoadAndCacheAsync(ct);
        await File.WriteAllTextAsync(_bed.SamplePath("SampleDomain", "AddedBetweenSessionsZq.cs"),
            "namespace SampleDomain;\npublic sealed class AddedBetweenSessionsZq;\n", ct);
        var workspace = _bed.CreateWorkspace();
        var gate = SwapGate.Attach(workspace);

        // Act
        await workspace.LoadSolutionAsync(_bed.SolutionPath, ct);
        Assert.Equal("cache", workspace.CacheStatus!.Source);
        Assert.Null(await workspace.EnsureReadyOrStatusAsync(ct)); // the first call's refresh adds it
        var beforeVerification = await SymbolResolver.FindSymbolsByNameAsync(workspace, "AddedBetweenSessionsZq", ct: ct);
        gate.Release();
        await workspace.WaitForCacheVerificationAsync();

        // Assert: found at once; MSBuild's verification may order it differently, but finds it too.
        Assert.Single(beforeVerification);
        Assert.Contains(workspace.CacheStatus!.Verification, new[] { "unchanged", "changed" });
        Assert.Single(await SymbolResolver.FindSymbolsByNameAsync(workspace, "AddedBetweenSessionsZq", ct: ct));
    }

    [Fact]
    public async Task LoadSolution_CacheDisabled_ReportsNoCacheStatusAndWritesNothing()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var workspace = _bed.CreateWorkspace();
        workspace.LoadCache = null;

        // Act
        await workspace.LoadSolutionAsync(_bed.SolutionPath, ct);
        await workspace.WaitForCacheWriteAsync();

        // Assert
        Assert.Null(workspace.CacheStatus);
        Assert.DoesNotContain("LoadCache", await CWM.RoslynNavigator.Tools.GetWorkspaceStatusTool.ExecuteAsync(workspace, ct: ct));
        Assert.False(Directory.Exists(_bed.Store.RootDirectory));
    }

    [Fact]
    public async Task LoadSolutions_TwoSolutions_RestoresBothWithMembership()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        string[] solutions = [_bed.SolutionPath, _bed.Data.SecondSolutionPath];
        var original = await _bed.LoadAndCacheAsync(ct, solutions);
        var workspace = _bed.CreateWorkspace();

        // Act
        await workspace.LoadSolutionsAsync(solutions, ct);
        await workspace.WaitForCacheVerificationAsync();

        // Assert
        Assert.Equal("cache", workspace.CacheStatus!.Source);
        Assert.Equal("unchanged", workspace.CacheStatus.Verification);
        Assert.Equal(original.LoadedSolutions.Select(s => (s.Name, s.ProjectPaths.Count)),
            workspace.LoadedSolutions.Select(s => (s.Name, s.ProjectPaths.Count)));
        var domain = workspace.GetSolution()!.Projects.Single(p => p.Name == "SampleDomain").FilePath;
        Assert.Equal(["SampleSolution.sln", "SecondSolution.slnx"], workspace.GetSolutionsForProject(domain));
    }

    public void Dispose() => _bed.Dispose();
}
