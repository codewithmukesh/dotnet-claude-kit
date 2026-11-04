using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tests.Snapshots;

namespace CWM.RoslynNavigator.Tests.Caching;

/// <summary>
/// A snapshot restored from the cache answers every tool exactly as the MSBuild load it was
/// written from: same projects, documents, references, options, and load diagnostics.
/// </summary>
public sealed class LoadCacheEquivalenceTests : IDisposable
{
    /// <summary>Tools whose answer describes the load itself rather than the code.</summary>
    private static readonly HashSet<string> LoadDescribingTools = ["get_workspace_status", "get_token_savings"];

    private readonly LoadCacheTestbed _bed = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EveryTool_CachedSnapshot_AnswersAsTheMSBuildLoad(bool withSecondSolution)
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        string[] solutions = withSecondSolution ? [_bed.SolutionPath, _bed.Data.SecondSolutionPath] : [_bed.SolutionPath];
        var fresh = await _bed.LoadAndCacheAsync(ct, solutions);
        await fresh.WaitForWarmUpAsync();

        var cached = _bed.CreateWorkspace();
        await cached.LoadSolutionsAsync(solutions, ct);
        await cached.WaitForCacheVerificationAsync();
        await cached.WaitForWarmUpAsync();
        Assert.Equal("cache", cached.CacheStatus!.Source);
        Assert.Equal("unchanged", cached.CacheStatus.Verification);

        // Act + Assert
        foreach (var tool in ToolCatalog.All.Where(t => !LoadDescribingTools.Contains(t.Name)))
        {
            var args = ToolArguments.Valid(tool.Name);
            var expected = SnapshotScrubber.Scrub(await tool.InvokeAsync(fresh, args, ct), _bed.SampleRoot);
            var actual = SnapshotScrubber.Scrub(await tool.InvokeAsync(cached, args, ct), _bed.SampleRoot);
            Assert.True(expected == actual, $"{tool.Name} differs.\nMSBuild: {expected}\nCache:   {actual}");
        }

        Assert.Equal(
            fresh.Health.GetDiagnostics(fresh.GetSolution()).Select(d => (d.Kind, d.Message)),
            cached.Health.GetDiagnostics(cached.GetSolution()).Select(d => (d.Kind, d.Message)));
        Assert.Equal(
            fresh.GetProjectProblems().Select(p => (p.Name, string.Join("|", p.Issues))),
            cached.GetProjectProblems().Select(p => (p.Name, string.Join("|", p.Issues))));
    }

    [Fact]
    public async Task LoadSolution_MultiTargetedProject_RestoresThePrunedFrameworksAndTheirDiagnostic()
    {
        // Arrange: one project targeting two frameworks; the default load keeps one.
        var ct = TestContext.Current.CancellationToken;
        var root = Path.Combine(_bed.Data.Root, "MultiTarget");
        var project = Directory.CreateDirectory(Path.Combine(root, "Lib")).FullName;
        await File.WriteAllTextAsync(Path.Combine(project, "Lib.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFrameworks>net10.0;net8.0</TargetFrameworks></PropertyGroup>
            </Project>
            """, ct);
        await File.WriteAllTextAsync(Path.Combine(project, "Greeter.cs"), "namespace Lib;\npublic sealed class Greeter;\n", ct);
        var solution = Path.Combine(root, "MultiTarget.slnx");
        await File.WriteAllTextAsync(solution, """<Solution><Project Path="Lib/Lib.csproj" /></Solution>""", ct);
        foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(-1));

        var fresh = await _bed.LoadAndCacheAsync(ct, solution);
        var cached = _bed.CreateWorkspace();

        // Act
        await cached.LoadSolutionAsync(solution, ct);

        // Assert
        Assert.Equal("cache", cached.CacheStatus!.Source);
        Assert.Equal(["Lib(net10.0)"], cached.GetSolution()!.Projects.Select(p => p.Name));
        Assert.Equal(
            fresh.Health.GetDiagnostics(null).Select(d => d.Message),
            cached.Health.GetDiagnostics(null).Select(d => d.Message));
        Assert.Contains(cached.Health.GetDiagnostics(null), d => d.Message.Contains("one target framework"));
        Assert.Equal(
            fresh.GetSolution()!.Projects.Single().ParseOptions!.PreprocessorSymbolNames,
            cached.GetSolution()!.Projects.Single().ParseOptions!.PreprocessorSymbolNames);
        await cached.WaitForCacheVerificationAsync();
        Assert.Equal("unchanged", cached.CacheStatus.Verification);
    }

    public void Dispose() => _bed.Dispose();
}
