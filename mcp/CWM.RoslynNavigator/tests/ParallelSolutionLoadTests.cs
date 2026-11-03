using CWM.RoslynNavigator.Tests.Fixtures;
using Microsoft.CodeAnalysis;

namespace CWM.RoslynNavigator.Tests;

/// <summary>
/// A solution loaded through several build hosts (<see cref="ParallelSolutionLoader"/>) must
/// be the same solution a single <c>OpenSolutionAsync</c> produces: same projects, documents,
/// and references, with shared libraries loaded once.
/// </summary>
public sealed class ParallelSolutionLoadTests : IDisposable
{
    private readonly TestDataCopy _data = TestDataCopy.Create("cwm-rn-parallel");
    private readonly List<WorkspaceManager> _workspaces = [];

    [Fact]
    public async Task LoadSolutionAsync_FourBuildHosts_SameSolutionAsSingleHost()
    {
        var ct = TestContext.Current.CancellationToken;
        var single = await LoadAsync(1, ct, _data.SampleSolutionPath);

        var parallel = await LoadAsync(4, ct, _data.SampleSolutionPath);

        Assert.Equal(Fingerprint(single), Fingerprint(parallel));
    }

    [Fact]
    public async Task LoadSolutionsAsync_FourBuildHostsWithSecondSolution_SharedProjectsLoadedOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        var single = await LoadAsync(1, ct, _data.SampleSolutionPath, _data.SecondSolutionPath);

        var parallel = await LoadAsync(4, ct, _data.SampleSolutionPath, _data.SecondSolutionPath);

        Assert.Equal(Fingerprint(single), Fingerprint(parallel));
        Assert.Equal(
            parallel.GetSolution()!.Projects.Count(),
            parallel.GetSolution()!.Projects.Select(p => p.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task LoadSolutionAsync_FourBuildHosts_FindsImplementationsAcrossGroups()
    {
        var ct = TestContext.Current.CancellationToken;
        var workspace = await LoadAsync(4, ct, _data.SampleSolutionPath);

        var json = await RoslynNavigator.Tools.FindImplementationsTool.ExecuteAsync(workspace, "IOrderRepository", ct: ct);

        Assert.Contains("InMemoryOrderRepository", json, StringComparison.Ordinal);
        Assert.Contains("CachedOrderRepository", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Merge_DuplicateAcrossGroups_KeepsOneAndRedirectsReferences()
    {
        var sharedA = Project("Shared", @"C:\repo\Shared\Shared.csproj");
        var sharedB = Project("Shared", @"C:\repo\Shared\Shared.csproj");
        var appA = Project("AppA", @"C:\repo\AppA\AppA.csproj", sharedA.Id);
        var appB = Project("AppB", @"C:\repo\AppB\AppB.csproj", sharedB.Id);

        var merged = ParallelSolutionLoader.Merge([[sharedA, appA], [sharedB, appB]]);

        Assert.Equal(["AppA", "AppB", "Shared"], merged.Select(p => p.Name).Order());
        var b = Assert.Single(merged, p => p.Name == "AppB");
        Assert.Equal(sharedA.Id, Assert.Single(b.ProjectReferences).ProjectId);
    }

    [Fact]
    public void CreatePlan_HubReferencingEverything_EstimatesNoSpeedup()
    {
        using var temp = new TempProjects();
        var libs = Enumerable.Range(0, 8).Select(i => temp.Write($"Lib{i}")).ToList();
        var hub = temp.Write("Hub", [.. libs]);

        var plan = ProjectPartitioner.CreatePlan([hub, .. libs], 4);

        Assert.Equal(9, plan.SingleLoad);
        Assert.Equal(9, plan.LargestGroupLoad);
        Assert.Equal(1.0, plan.EstimatedSpeedup);
    }

    [Fact]
    public void CreatePlan_IndependentProjects_SplitsEvenlyAndPlacesEachOnce()
    {
        using var temp = new TempProjects();
        var core = temp.Write("Core");
        var apps = Enumerable.Range(0, 8).Select(i => temp.Write($"App{i}", core)).ToList();

        var plan = ProjectPartitioner.CreatePlan([core, .. apps], 4);

        Assert.Equal(4, plan.Groups.Count);
        Assert.Equal(
            new[] { core }.Concat(apps).Order(StringComparer.OrdinalIgnoreCase),
            plan.Groups.SelectMany(g => g).Order(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        Assert.True(plan.EstimatedSpeedup >= 2, $"estimated {plan.EstimatedSpeedup}");
    }

    private async Task<WorkspaceManager> LoadAsync(int parallelism, CancellationToken ct, params string[] solutions)
    {
        var workspace = TestWorkspace.Create(TimeProvider.System);
        workspace.LoadParallelism = parallelism;
        _workspaces.Add(workspace);
        await workspace.LoadSolutionsAsync(solutions, ct);
        return workspace;
    }

    /// <summary>Every project with its documents and references, by name and path, independent of ids and order.</summary>
    private static List<string> Fingerprint(WorkspaceManager workspace)
    {
        var solution = workspace.GetSolution()!;
        return
        [
            .. solution.Projects.Select(p => string.Join(" | ",
                p.Name,
                p.FilePath,
                string.Join(",", p.Documents.Select(d => d.FilePath).Order(StringComparer.Ordinal)),
                string.Join(",", p.ProjectReferences.Select(r => solution.GetProject(r.ProjectId)?.Name ?? "?").Order(StringComparer.Ordinal)),
                string.Join(",", p.MetadataReferences.Select(r => Path.GetFileName(r.Display)).Order(StringComparer.Ordinal)),
                p.AnalyzerReferences.Count))
            .Order(StringComparer.Ordinal)
        ];
    }

    private static ProjectInfo Project(string name, string path, params ProjectId[] references) =>
        ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Default, name, name, LanguageNames.CSharp,
            filePath: path, outputFilePath: Path.ChangeExtension(path, ".dll"),
            projectReferences: references.Select(r => new ProjectReference(r)));

    public void Dispose()
    {
        foreach (var workspace in _workspaces)
            workspace.Dispose();
        _data.Dispose();
    }

    /// <summary>Project files on disk for the partitioner, which reads ProjectReference items.</summary>
    private sealed class TempProjects : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("cwm-rn-plan-").FullName;

        public string Write(string name, params string[] references)
        {
            var path = Path.Combine(_root, name, name + ".csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var items = string.Concat(references.Select(r => $"<ProjectReference Include=\"{r}\" />"));
            File.WriteAllText(path, $"<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup>{items}</ItemGroup></Project>");
            return SolutionFileReader.NormalizePath(path);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Temp folder.
            }
        }
    }
}
