namespace CWM.RoslynNavigator.Tests;

/// <summary>
/// Pure filesystem tests for <see cref="SolutionDiscovery"/>.
/// Uses temp directories with empty marker files — no Roslyn/MSBuild needed.
/// </summary>
public class SolutionDiscoveryTests : IDisposable
{
    private readonly string _tempRoot;

    public SolutionDiscoveryTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"SolutionDiscoveryTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private string CreateDir(params string[] segments)
    {
        var path = Path.Combine([_tempRoot, .. segments]);
        Directory.CreateDirectory(path);
        return path;
    }

    private string CreateFile(params string[] segments)
    {
        var path = Path.Combine([_tempRoot, .. segments]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }

    // 1. Root-level .sln found — backward compatibility
    [Fact]
    public void FindSolutionPath_RootLevelSln_ReturnsIt()
    {
        var slnPath = CreateFile("MyApp.sln");

        var result = SolutionDiscovery.FindSolutionPath([], _tempRoot);

        Assert.Equal(slnPath, result);
    }

    // 2. Solution in subdirectory — core bug fix
    [Fact]
    public void FindSolutionPath_SlnInSubdirectory_ReturnsIt()
    {
        var slnPath = CreateFile("src", "MyApp.sln");

        var result = SolutionDiscovery.FindSolutionPath([], _tempRoot);

        Assert.Equal(slnPath, result);
    }

    // 3. Solution at depth 3 — max boundary (found)
    [Fact]
    public void FindSolutionPath_SlnAtDepth3_ReturnsIt()
    {
        var slnPath = CreateFile("level1", "level2", "level3", "Deep.sln");

        var result = SolutionDiscovery.FindSolutionPath([], _tempRoot);

        Assert.Equal(slnPath, result);
    }

    // 4. Solution at depth 4 — max boundary (not found)
    [Fact]
    public void FindSolutionPath_SlnAtDepth4_ReturnsNull()
    {
        CreateFile("level1", "level2", "level3", "level4", "TooDeep.sln");

        var result = SolutionDiscovery.FindSolutionPath([], _tempRoot);

        Assert.Null(result);
    }

    // 5. Multiple depths — shallowest wins
    [Fact]
    public void FindSolutionPath_MultipleDepths_ShallowestWins()
    {
        CreateFile("src", "Deep.sln");
        var rootSln = CreateFile("Root.sln");

        var result = SolutionDiscovery.FindSolutionPath([], _tempRoot);

        Assert.Equal(rootSln, result);
    }

    // 6. Same depth — alphabetical first (case-insensitive)
    [Fact]
    public void FindSolutionPath_SameDepth_AlphabeticalFirst()
    {
        CreateFile("Zebra.sln");
        CreateFile("Alpha.sln");

        var result = SolutionDiscovery.FindSolutionPath([], _tempRoot);

        Assert.NotNull(result);
        Assert.EndsWith("Alpha.sln", result);
    }

    // 7. .slnx extension found
    [Fact]
    public void FindSolutionPath_SlnxExtension_ReturnsIt()
    {
        var slnxPath = CreateFile("src", "Framework.slnx");

        var result = SolutionDiscovery.FindSolutionPath([], _tempRoot);

        Assert.Equal(slnxPath, result);
    }

    // 8. Skipped directories ignored
    [Theory]
    [InlineData("node_modules")]
    [InlineData(".git")]
    [InlineData("bin")]
    [InlineData("obj")]
    [InlineData(".vs")]
    [InlineData("artifacts")]
    public void FindSolutionPath_SkippedDirectory_Ignored(string skippedDir)
    {
        CreateFile(skippedDir, "Hidden.sln");

        var result = SolutionDiscovery.FindSolutionPath([], _tempRoot);

        Assert.Null(result);
    }

    // 9. Explicit --solution dir arg — recursive scan
    [Fact]
    public void FindSolutionPath_ExplicitDirArg_RecursiveScans()
    {
        var slnPath = CreateFile("src", "MyApp.sln");

        var result = SolutionDiscovery.FindSolutionPath(["--solution", _tempRoot]);

        Assert.Equal(slnPath, result);
    }

    // 10. Explicit --solution file arg — direct return
    [Fact]
    public void FindSolutionPath_ExplicitFileArg_ReturnsDirect()
    {
        var slnPath = CreateFile("src", "MyApp.sln");

        var result = SolutionDiscovery.FindSolutionPath(["--solution", slnPath]);

        Assert.Equal(Path.GetFullPath(slnPath), result);
    }

    // 11. Non-existent directory — null
    [Fact]
    public void FindSolutionPath_NonExistentDirectory_ReturnsNull()
    {
        var result = SolutionDiscovery.FindSolutionPath([], Path.Combine(_tempRoot, "does-not-exist"));

        Assert.Null(result);
    }

    // 12. Empty directory — null
    [Fact]
    public void FindSolutionPath_EmptyDirectory_ReturnsNull()
    {
        var result = SolutionDiscovery.FindSolutionPath([], _tempRoot);

        Assert.Null(result);
    }

    // 13. Root solution wins over subdirectory
    [Fact]
    public void FindSolutionPath_RootSlnWinsOverSubdirectory()
    {
        var rootSln = CreateFile("Root.sln");
        CreateFile("src", "Sub.sln");

        var result = SolutionDiscovery.FindSolutionPath([], _tempRoot);

        Assert.Equal(rootSln, result);
    }

    // 14. -s short flag works
    [Fact]
    public void FindSolutionPath_ShortFlag_Works()
    {
        var slnPath = CreateFile("MyApp.sln");

        var result = SolutionDiscovery.FindSolutionPath(["-s", _tempRoot]);

        Assert.Equal(slnPath, result);
    }

    // Multi-solution (#16)

    [Fact]
    public void FindSolutionPaths_RepeatedSolutionFlag_ReturnsAllInOrder()
    {
        var orders = CreateFile("orders", "Orders.sln");
        var billing = CreateFile("billing", "Billing.slnx");

        var result = SolutionDiscovery.FindSolutionPaths(["--solution", orders, "-s", billing]);

        Assert.Equal([orders, billing], result);
    }

    [Fact]
    public void FindSolutionPaths_DuplicateSolutionFlag_Deduplicated()
    {
        var orders = CreateFile("orders", "Orders.sln");

        var result = SolutionDiscovery.FindSolutionPaths(["--solution", orders, "--solution", orders]);

        Assert.Equal([orders], result);
    }

    [Fact]
    public void FindSolutionPaths_MissingExplicitPath_SkippedWithoutFallback()
    {
        var orders = CreateFile("orders", "Orders.sln");
        CreateFile("Root.sln");

        var result = SolutionDiscovery.FindSolutionPaths(
            ["--solution", Path.Combine(_tempRoot, "nope.sln"), "--solution", orders], _tempRoot);

        Assert.Equal([orders], result);
    }

    [Fact]
    public void FindSolutionPaths_WithoutAllFlag_ReturnsOnlyShallowest()
    {
        var root = CreateFile("Root.sln");
        CreateFile("svc", "Service.sln");

        var result = SolutionDiscovery.FindSolutionPaths([], _tempRoot);

        Assert.Equal([root], result);
    }

    [Fact]
    public void FindSolutionPaths_AllSolutionsFlag_ReturnsEverySolutionWithinDepth()
    {
        var orders = CreateFile("services", "orders", "Orders.sln");
        var billing = CreateFile("services", "billing", "Billing.slnx");
        CreateFile("a", "b", "c", "d", "TooDeep.sln");

        var result = SolutionDiscovery.FindSolutionPaths(["--all-solutions"], _tempRoot);

        Assert.Equal([billing, orders], result); // BFS, then alphabetical
    }

    [Fact]
    public void FindSolutionPaths_AllSolutionsFlagWithDirectoryArg_ScansThatDirectory()
    {
        var orders = CreateFile("services", "orders", "Orders.sln");
        var billing = CreateFile("services", "billing", "Billing.sln");
        CreateFile("Elsewhere.sln");

        var result = SolutionDiscovery.FindSolutionPaths(
            ["--solution", Path.Combine(_tempRoot, "services"), "--all-solutions"]);

        Assert.Equal([billing, orders], result);
    }

    [Fact]
    public void FindSolutionPath_SeveralConfigured_ReturnsFirst()
    {
        var orders = CreateFile("orders", "Orders.sln");
        var billing = CreateFile("billing", "Billing.sln");

        var result = SolutionDiscovery.FindSolutionPath(["--solution", orders, "--solution", billing]);

        Assert.Equal(orders, result);
    }
}
