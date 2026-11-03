using System.Diagnostics;
using CWM.RoslynNavigator.Tests.Fixtures;

namespace CWM.RoslynNavigator.Tests;

/// <summary>
/// The background walk for new source files must stay bounded on a large repo: no descent
/// into bin/obj, dot-folders, node_modules, other projects' folders, or links that can loop.
/// </summary>
public sealed class StructuralScanTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cwm-rn-scan-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _links = [];

    [Fact]
    public void EnumerateProjectSourceFiles_ExcludedAndForeignFolders_AreSkipped()
    {
        var project = Path.Combine(_root, "App");
        string[] expected =
        [
            Write(project, "Program.cs"),
            Write(project, "Features", "Orders.cs"),
            Write(project, "Features", "bin", "NestedBinIsCompiled.cs") // only the project's own bin/ is excluded
        ];
        Write(project, "bin", "Debug", "Generated.cs");
        Write(project, "obj", "Debug", "AssemblyInfo.cs");
        Write(project, ".idea", "Scratch.cs");
        Write(project, "ClientApp", "node_modules", "pkg", "Weird.cs");
        Write(project, "Tools", "Tool.csproj");
        Write(project, "Tools", "Tool.cs");
        Write(project, "README.md");

        HashSet<string> projectDirectories = new(PathComparison.Comparer) { project, Path.Combine(project, "Tools") };
        var found = WorkspaceManager.EnumerateProjectSourceFiles(project, projectDirectories).ToList();

        Assert.Equal(expected.Order(PathComparison.Comparer), found.Order(PathComparison.Comparer), PathComparison.Comparer);
    }

    [Fact]
    public void EnumerateProjectSourceFiles_DirectoryLinkLoop_IsNotFollowed()
    {
        var project = Path.Combine(_root, "App");
        var source = Write(project, "Features", "Orders.cs");
        if (!TryCreateDirectoryLink(Path.Combine(project, "Features", "loop"), project))
            Assert.Skip("Creating a directory symlink or junction is not permitted here.");

        HashSet<string> projectDirectories = new(PathComparison.Comparer) { project };
        // Bounded so a regression fails instead of hanging the run.
        var found = WorkspaceManager.EnumerateProjectSourceFiles(project, projectDirectories).Take(1000).ToList();

        Assert.Equal([source], found);
    }

    [Fact]
    public async Task StructuralScan_LinkLoopInProject_CompletesAndAddsNewFileOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        TestSolutionFixture.RegisterMSBuild();
        TestDataCopy.CopyDirectory(Path.GetDirectoryName(TestSolutionFixture.SampleSolutionPath)!, _root);
        var clock = new TestClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var workspace = TestWorkspace.Create(clock);
        await workspace.LoadSolutionAsync(Path.Combine(_root, "SampleSolution.sln"), ct);

        var api = Path.Combine(_root, "SampleApi");
        Directory.CreateDirectory(Path.Combine(api, "Features"));
        if (!TryCreateDirectoryLink(Path.Combine(api, "Features", "loop"), api))
            Assert.Skip("Creating a directory symlink or junction is not permitted here.");
        var added = Write(api, "Features", "LoopSafeZq.cs");
        await File.WriteAllTextAsync(added, "namespace SampleApi;\n\npublic sealed class LoopSafeZq;\n", ct);

        clock.Advance(TimeSpan.FromSeconds(90));
        await workspace.RefreshChangedDocumentsAsync(ct);
        await workspace.WaitForStructuralScanAsync().WaitAsync(TimeSpan.FromSeconds(30), ct);
        clock.Advance(TimeSpan.FromSeconds(10));
        await workspace.RefreshChangedDocumentsAsync(ct);

        var document = Assert.Single(workspace.GetSolution()!.Projects.SelectMany(p => p.Documents), d => d.Name == "LoopSafeZq.cs");
        Assert.Equal(added, document.FilePath);
        Assert.Equal(1, workspace.LoadCount);
    }

    private static string Write(params string[] parts)
    {
        var path = Path.Combine(parts);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "// test\n");
        return path;
    }

    /// <summary>A symlink where permitted; on Windows without that privilege, a junction.</summary>
    private bool TryCreateDirectoryLink(string link, string target)
    {
        _links.Add(link);
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!OperatingSystem.IsWindows()) return false;
        }

        using var mklink = Process.Start(new ProcessStartInfo("cmd.exe", ["/c", "mklink", "/J", link, target])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        })!;
        mklink.WaitForExit();
        return mklink.ExitCode == 0 && Directory.Exists(link);
    }

    public void Dispose()
    {
        // Remove links first so the recursive delete cannot follow them.
        foreach (var link in _links)
        {
            try
            {
                Directory.Delete(link);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort.
            }
        }

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
