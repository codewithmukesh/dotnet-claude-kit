using System.Diagnostics;

namespace CWM.RoslynNavigator.Tests;

public sealed class AnalyzerShadowCopierTests : IDisposable
{
    private readonly string _temp =
        Path.Combine(Path.GetTempPath(), "cwm-roslyn-navigator-tests", Guid.NewGuid().ToString("N"));

    private string Source => Path.Combine(_temp, "source");
    private string ShadowRoot => Path.Combine(_temp, "shadow");

    public AnalyzerShadowCopierTests()
    {
        Directory.CreateDirectory(Path.Combine(Source, "de"));
        File.WriteAllText(Path.Combine(Source, "Analyzer.dll"), "analyzer-v1");
        File.WriteAllText(Path.Combine(Source, "Dependency.dll"), "dependency");
        File.WriteAllText(Path.Combine(Source, "de", "Analyzer.resources.dll"), "satellite");
        Directory.CreateDirectory(Path.Combine(Source, "unrelated"));
        File.WriteAllText(Path.Combine(Source, "unrelated", "Big.dll"), "not a satellite");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_temp, recursive: true);
        }
        catch (IOException)
        {
            // Temp folder; best effort.
        }
    }

    [Fact]
    public void GetShadowPath_ExistingFile_CopiesWholeDirectoryIncludingSatellites()
    {
        var copier = new AnalyzerShadowCopier(ShadowRoot);

        var shadowPath = copier.GetShadowPath(Path.Combine(Source, "Analyzer.dll"));

        var shadowDirectory = Path.GetDirectoryName(shadowPath)!;
        Assert.StartsWith(ShadowRoot, shadowPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("analyzer-v1", File.ReadAllText(shadowPath));
        Assert.True(File.Exists(Path.Combine(shadowDirectory, "Dependency.dll")));
        Assert.True(File.Exists(Path.Combine(shadowDirectory, "de", "Analyzer.resources.dll")));
    }

    [Fact]
    public void GetShadowPath_NonCultureSubdirectory_IsNotCopied()
    {
        var copier = new AnalyzerShadowCopier(ShadowRoot);

        var shadowPath = copier.GetShadowPath(Path.Combine(Source, "Analyzer.dll"));

        Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(shadowPath)!, "unrelated")));
    }

    [Fact]
    public void GetShadowPath_SiblingFilesUnchanged_ShareOneCopy()
    {
        var copier = new AnalyzerShadowCopier(ShadowRoot);

        var first = copier.GetShadowPath(Path.Combine(Source, "Analyzer.dll"));
        var again = copier.GetShadowPath(Path.Combine(Source, "Analyzer.dll"));
        var sibling = copier.GetShadowPath(Path.Combine(Source, "Dependency.dll"));

        // Dependencies resolve from the analyzer's own directory, so siblings must share it.
        Assert.Equal(first, again);
        Assert.Equal(Path.GetDirectoryName(first), Path.GetDirectoryName(sibling));
        Assert.Single(Directory.GetDirectories(ShadowRoot));
    }

    [Fact]
    public void GetShadowPath_OriginalRewritten_LoadsFreshCopy()
    {
        var copier = new AnalyzerShadowCopier(ShadowRoot);
        var original = Path.Combine(Source, "Analyzer.dll");
        var before = copier.GetShadowPath(original);

        File.WriteAllText(original, "analyzer-v2-longer");
        var after = copier.GetShadowPath(original);

        // The old copy may still be loaded, so the new bits must land somewhere else.
        Assert.NotEqual(before, after);
        Assert.Equal("analyzer-v2-longer", File.ReadAllText(after));
    }

    [Fact]
    public void GetShadowPath_MissingFile_ReturnsOriginalPath()
    {
        var copier = new AnalyzerShadowCopier(ShadowRoot);
        var missing = Path.Combine(Source, "Missing.dll");

        Assert.Equal(missing, copier.GetShadowPath(missing));
    }

    [Fact]
    public void SweepStaleRoots_RootOfExitedProcess_IsDeleted()
    {
        var stale = CreateRoot(AnalyzerShadowCopier.GetRootName(ExitedProcessId(), DateTimeOffset.UnixEpoch));

        AnalyzerShadowCopier.SweepStaleRoots(ShadowRoot);

        Assert.False(Directory.Exists(stale));
    }

    [Fact]
    public void SweepStaleRoots_RootOfRunningProcess_IsKept()
    {
        using var current = Process.GetCurrentProcess();
        var live = CreateRoot(AnalyzerShadowCopier.GetRootName(current.Id, current.StartTime));

        AnalyzerShadowCopier.SweepStaleRoots(ShadowRoot);

        Assert.True(Directory.Exists(live));
    }

    [Fact]
    public void SweepStaleRoots_ProcessIdRecycled_IsDeleted()
    {
        // Same id as a running process, but a different start time: the owner is gone.
        var recycled = CreateRoot(AnalyzerShadowCopier.GetRootName(Environment.ProcessId, DateTimeOffset.UnixEpoch));

        AnalyzerShadowCopier.SweepStaleRoots(ShadowRoot);

        Assert.False(Directory.Exists(recycled));
    }

    [Fact]
    public void SweepStaleRoots_UnrecognizedDirectory_IsKept()
    {
        var foreign = CreateRoot("not-a-navigator-root");

        AnalyzerShadowCopier.SweepStaleRoots(ShadowRoot);

        Assert.True(Directory.Exists(foreign));
    }

    private string CreateRoot(string name)
    {
        var path = Path.Combine(ShadowRoot, name);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "Analyzer.dll"), "x");
        return path;
    }

    private static int ExitedProcessId()
    {
        using var process = Process.Start(new ProcessStartInfo("dotnet", "--version")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        })!;
        process.WaitForExit();
        return process.Id;
    }
}
