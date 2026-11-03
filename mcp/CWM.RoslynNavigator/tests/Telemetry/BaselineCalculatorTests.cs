using CWM.RoslynNavigator.Telemetry;

namespace CWM.RoslynNavigator.Tests.Telemetry;

/// <summary>
/// The dedup rules are what keep the headline number defensible, so they are pinned
/// precisely — each one exists to push the figure down, and a regression would silently
/// inflate a public claim.
/// </summary>
public sealed class BaselineCalculatorTests : IDisposable
{
    private readonly string _root;

    public BaselineCalculatorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cwm-bl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    private string WriteFile(string name, int bytes)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, new string('x', bytes));
        return path;
    }

    private BaselineCalculator Create(long repoTotalBytes = 0) =>
        new(() => _root, () => repoTotalBytes);

    [Fact]
    public void Compute_SolutionDirectoryDiscoveredAfterConstruction_StillResolvesPaths()
    {
        // Telemetry is built during host startup, before a solution has been discovered.
        // If the root is captured then rather than read per call, it is null forever and
        // every relative path silently contributes nothing — a zero baseline on every call.
        WriteFile("A.cs", 3500);

        string? root = null;
        var calculator = new BaselineCalculator(() => root, () => 0);

        var beforeDiscovery = calculator.Compute(["A.cs"]);
        root = _root;
        var afterDiscovery = calculator.Compute(["A.cs"]);

        Assert.Equal(0, beforeDiscovery.Tokens);
        Assert.Equal(1000, afterDiscovery.Tokens);
    }

    [Fact]
    public void Compute_UnresolvablePath_IsNotBurnedFromTheSessionSet()
    {
        // A path that could not be resolved was never actually counted, so it must stay
        // eligible once the root is known.
        WriteFile("A.cs", 3500);

        string? root = null;
        var calculator = new BaselineCalculator(() => root, () => 0);

        calculator.Compute(["A.cs"]);
        root = _root;

        Assert.Equal(1000, calculator.Compute(["A.cs"]).Tokens);
    }

    [Fact]
    public void Compute_SingleFile_ConvertsBytesToTokens()
    {
        WriteFile("A.cs", 3500);
        var calculator = Create();

        var baseline = calculator.Compute(["A.cs"]);

        Assert.Equal(1000, baseline.Tokens); // 3500 / 3.5
        Assert.Equal(1, baseline.Files);
    }

    [Fact]
    public void Compute_SamePathTwiceInOneCall_CountsItOnce()
    {
        WriteFile("A.cs", 3500);
        var calculator = Create();

        // Twenty references in one file are still one file to read.
        var baseline = calculator.Compute(["A.cs", "A.cs", "A.cs"]);

        Assert.Equal(1000, baseline.Tokens);
        Assert.Equal(1, baseline.Files);
    }

    [Fact]
    public void Compute_PathAlreadyCountedThisSession_ContributesNothing()
    {
        WriteFile("A.cs", 3500);
        var calculator = Create();

        var first = calculator.Compute(["A.cs"]);
        var second = calculator.Compute(["A.cs"]);

        // Without the tool the file would have been read once and stayed in context.
        // Charging it again on every later call is the main way this metric could inflate.
        Assert.Equal(1000, first.Tokens);
        Assert.Equal(0, second.Tokens);
        Assert.Equal(0, second.Files);
    }

    [Fact]
    public void Compute_NewFileAfterSeenFile_CountsOnlyTheNewOne()
    {
        WriteFile("A.cs", 3500);
        WriteFile("B.cs", 7000);
        var calculator = Create();

        calculator.Compute(["A.cs"]);
        var second = calculator.Compute(["A.cs", "B.cs"]);

        Assert.Equal(2000, second.Tokens);
        Assert.Equal(1, second.Files);
        Assert.Equal(2, calculator.SessionCountedFiles);
    }

    [Fact]
    public void Compute_ExceedsRepoTotal_IsCappedAtRepoSize()
    {
        WriteFile("A.cs", 35_000);
        var calculator = Create(repoTotalBytes: 3500);

        var baseline = calculator.Compute(["A.cs"]);

        // No single call can claim to have saved more than the repository holds.
        Assert.Equal(1000, baseline.Tokens);
    }

    [Fact]
    public void Compute_MissingFile_ContributesZeroWithoutThrowing()
    {
        var calculator = Create();

        var baseline = calculator.Compute(["NoSuchFile.cs"]);

        Assert.Equal(0, baseline.Tokens);
        Assert.Equal(0, baseline.Files);
    }

    [Fact]
    public void Compute_AbsolutePath_IsAccepted()
    {
        var absolute = WriteFile("A.cs", 3500);
        var calculator = Create();

        var baseline = calculator.Compute([absolute]);

        Assert.Equal(1000, baseline.Tokens);
    }

    [Fact]
    public void Compute_RelativeAndAbsoluteFormsOfSameFile_CountOnce()
    {
        var absolute = WriteFile("A.cs", 3500);
        var calculator = Create();

        // Responses carry relative paths, but a caller may pass an absolute one. Dedup has
        // to see through the difference or the same file is charged twice.
        var baseline = calculator.Compute(["A.cs", absolute]);

        Assert.Equal(1000, baseline.Tokens);
        Assert.Equal(1, baseline.Files);
    }

    [Fact]
    public void Compute_NestedRelativePath_ResolvesAgainstSolutionDirectory()
    {
        WriteFile(Path.Combine("Api", "Orders.cs"), 7000);
        var calculator = Create();

        var baseline = calculator.Compute(["Api/Orders.cs"]);

        Assert.Equal(2000, baseline.Tokens);
    }

    [Fact]
    public void Compute_EmptyInput_IsZero()
    {
        var baseline = Create().Compute([]);

        Assert.Equal(0, baseline.Tokens);
        Assert.Equal(0, baseline.Files);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
