using CWM.RoslynNavigator.Tests.Fixtures;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CWM.RoslynNavigator.Tests;

/// <summary>
/// MSBuild imports Directory.Build.* walking up from each project, so the files that matter
/// live wherever the repo puts them — src/, tests/, or above the solution folder — not just
/// beside the solution. The solution file itself decides which projects load at all.
/// </summary>
public sealed class NestedBuildFileTests : IDisposable
{
    private static readonly TimeSpan PastCooldown = TimeSpan.FromSeconds(10);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "cwm-rn-nested-" + Guid.NewGuid().ToString("N"));
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly WorkspaceManager _workspace;

    public NestedBuildFileTests()
    {
        TestSolutionFixture.RegisterMSBuild();
        _workspace = new WorkspaceManager(NullLoggerFactory.Instance.CreateLogger<WorkspaceManager>(), _clock);
    }

    [Fact]
    public async Task Refresh_DirectoryBuildPropsInProjectSubfolder_Reloads()
    {
        var ct = TestContext.Current.CancellationToken;
        // Repo layout: repo/App.sln, repo/src/Directory.Build.props, repo/src/SampleApi/...
        var solutionDirectory = Path.Combine(_root, "repo");
        var src = Path.Combine(solutionDirectory, "src");
        BuildFileChangeDetectionTests.CopyDirectory(Path.GetDirectoryName(TestSolutionFixture.SampleSolutionPath)!, src);
        File.Move(Path.Combine(src, "SampleSolution.sln"), Path.Combine(solutionDirectory, "SampleSolution.sln"));
        await RewriteAsync(Path.Combine(solutionDirectory, "SampleSolution.sln"), s => s.Replace(", \"Sample", ", \"src\\Sample"), ct);
        var props = Path.Combine(src, "Directory.Build.props");
        await File.WriteAllTextAsync(props, "<Project />", ct);

        await _workspace.LoadSolutionAsync(Path.Combine(solutionDirectory, "SampleSolution.sln"), ct);
        Assert.Equal(4, _workspace.ProjectCount); // SampleSolution: Domain, Infrastructure, Api, Tests

        await TouchAsync(props, "<Project><PropertyGroup><NoWarn>CS0219</NoWarn></PropertyGroup></Project>", ct);
        await RefreshAndSettleAsync(ct);

        Assert.Equal(2, _workspace.LoadCount);
    }

    [Fact]
    public async Task Refresh_DirectoryBuildPropsAboveSolutionFolder_Reloads()
    {
        var ct = TestContext.Current.CancellationToken;
        var solutionDirectory = Path.Combine(_root, "services", "orders");
        BuildFileChangeDetectionTests.CopyDirectory(Path.GetDirectoryName(TestSolutionFixture.SampleSolutionPath)!, solutionDirectory);
        var props = Path.Combine(_root, "Directory.Build.props");
        await File.WriteAllTextAsync(props, "<Project />", ct);

        await _workspace.LoadSolutionAsync(Path.Combine(solutionDirectory, "SampleSolution.sln"), ct);
        await TouchAsync(props, "<Project><PropertyGroup><NoWarn>CS0219</NoWarn></PropertyGroup></Project>", ct);
        await RefreshAndSettleAsync(ct);

        Assert.Equal(2, _workspace.LoadCount);
    }

    [Fact]
    public async Task Refresh_ProjectAddedToSolutionFile_ReloadsAndLoadsIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await WriteLibraryAsync("LibA", ct);
        await WriteLibraryAsync("LibB", ct);
        var solution = Path.Combine(_root, "Libs.slnx");
        await File.WriteAllTextAsync(solution, "<Solution>\n  <Project Path=\"LibA/LibA.csproj\" />\n</Solution>\n", ct);

        await _workspace.LoadSolutionAsync(solution, ct);
        Assert.Equal(1, _workspace.ProjectCount);

        await TouchAsync(solution,
            "<Solution>\n  <Project Path=\"LibA/LibA.csproj\" />\n  <Project Path=\"LibB/LibB.csproj\" />\n</Solution>\n", ct);
        await RefreshAndSettleAsync(ct);

        Assert.Equal(2, _workspace.LoadCount);
        Assert.Equal(2, _workspace.ProjectCount);
    }

    [Fact]
    public void EnumerateSharedBuildFiles_WalksUpFromEveryProject_Deduplicated()
    {
        // repo/Directory.Build.props, repo/src/Directory.Build.props, repo/tests/Directory.Build.props,
        // repo/NuGet.config, and projects under src/ and tests/.
        var repo = Path.Combine(_root, "repo");
        string[] files =
        [
            Path.Combine(repo, "Directory.Build.props"),
            Path.Combine(repo, "NuGet.config"),
            Path.Combine(repo, "src", "Directory.Build.props"),
            Path.Combine(repo, "src", "Directory.Packages.props"),
            Path.Combine(repo, "tests", "Directory.Build.props")
        ];
        foreach (var file in files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "<Project />");
        }

        var found = WorkspaceManager.EnumerateSharedBuildFiles(
            [
                Path.Combine(repo, "src", "Orders", "Orders.csproj"),
                Path.Combine(repo, "src", "Billing", "Billing.csproj"),
                Path.Combine(repo, "tests", "Orders.Tests", "Orders.Tests.csproj")
            ],
            [Path.Combine(repo, "All.sln")]);

        Assert.Equal(files.Order(PathComparison.Comparer), found.Order(PathComparison.Comparer), PathComparison.Comparer);
    }

    private async Task RefreshAndSettleAsync(CancellationToken ct)
    {
        _clock.Advance(PastCooldown);
        await _workspace.RefreshChangedDocumentsAsync(ct);
        await _workspace.WaitForReloadAsync();
    }

    private async Task WriteLibraryAsync(string name, CancellationToken ct)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        await File.WriteAllTextAsync(Path.Combine(directory, $"{name}.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>", ct);
        await File.WriteAllTextAsync(Path.Combine(directory, "Thing.cs"), $"namespace {name};\n\npublic sealed class Thing;\n", ct);
        // Stop MSBuild walking into whatever the temp folder's ancestors contain.
        var guard = Path.Combine(_root, "Directory.Build.props");
        if (!File.Exists(guard)) await File.WriteAllTextAsync(guard, "<Project />", ct);
    }

    private static async Task RewriteAsync(string path, Func<string, string> change, CancellationToken ct) =>
        await File.WriteAllTextAsync(path, change(await File.ReadAllTextAsync(path, ct)), ct);

    private static async Task TouchAsync(string path, string content, CancellationToken ct)
    {
        await File.WriteAllTextAsync(path, content, ct);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
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
