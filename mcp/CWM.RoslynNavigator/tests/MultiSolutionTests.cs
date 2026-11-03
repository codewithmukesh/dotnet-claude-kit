using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;

namespace CWM.RoslynNavigator.Tests;

/// <summary>
/// Several solutions in one workspace (#16): SampleSolution.sln and SecondSolution.slnx share
/// SampleDomain. Every tool works across both with no solution parameter.
/// </summary>
public sealed class MultiSolutionTests(MultiSolutionFixture fixture) : IClassFixture<MultiSolutionFixture>
{
    private const string Primary = "SampleSolution.sln";
    private const string Second = "SecondSolution.slnx";

    private WorkspaceManager Workspace => fixture.WorkspaceManager;

    [Fact]
    public void Load_SharedProject_IsLoadedOnce()
    {
        var solution = Workspace.GetSolution()!;

        var domainProjects = solution.Projects
            .Where(p => Path.GetFileName(p.FilePath) == "SampleDomain.csproj")
            .ToList();

        Assert.Single(domainProjects);
        Assert.Equal(5, Workspace.ProjectCount); // Domain, Infrastructure, Api, Tests + ShippingService
    }

    [Fact]
    public void Load_CrossSolutionProjectReference_ResolvesToTheSharedProject()
    {
        var solution = Workspace.GetSolution()!;
        var domain = solution.Projects.Single(p => p.Name == "SampleDomain");
        var shipping = solution.Projects.Single(p => p.Name == "ShippingService");

        var reference = Assert.Single(shipping.ProjectReferences);
        Assert.Equal(domain.Id, reference.ProjectId);
    }

    [Fact]
    public async Task Load_ProjectFromSecondSolution_CompilesAgainstSharedProject()
    {
        var shipping = Workspace.GetSolution()!.Projects.Single(p => p.Name == "ShippingService");

        var compilation = await Workspace.GetCompilationAsync(shipping.Id, TestContext.Current.CancellationToken);

        var errors = compilation!.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .ToList();
        Assert.Empty(errors);
    }

    [Fact]
    public void State_IsReady_WithBothSolutionsRecorded()
    {
        Assert.Equal(WorkspaceState.Ready, Workspace.State);
        Assert.Equal([Primary, Second], Workspace.LoadedSolutions.Select(s => s.Name));
        Assert.All(Workspace.LoadedSolutions, s => Assert.Null(s.Error));
        Assert.Equal(TestSolutionFixture.SampleSolutionPath, Workspace.SolutionFilePath);
    }

    [Theory]
    [InlineData("OrderService")]       // SampleSolution only
    [InlineData("ShipmentDispatcher")] // SecondSolution only
    [InlineData("Order")]              // shared project
    public async Task FindSymbol_SymbolsFromEitherSolution_AreFound(string name)
    {
        var json = await FindSymbolTool.ExecuteAsync(Workspace, name, kind: "class", ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<SymbolSearchResult>(json)!;

        Assert.Contains(result.Symbols, s => s.Name == name);
    }

    [Fact]
    public async Task FindReferences_SharedInterface_CrossesSolutionBoundaries()
    {
        var json = await FindReferencesTool.ExecuteAsync(
            Workspace, "IOrderRepository", maxResults: 200, ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<ReferencesResult>(json)!;

        Assert.Contains(result.References, r => r.File.StartsWith("SampleSolution/SampleApi/", StringComparison.Ordinal));
        Assert.Contains(result.References, r => r.File.StartsWith("SecondSolution/ShippingService/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FindReferences_PathsAreRelativeToCommonRoot_NoParentSegments()
    {
        var json = await FindReferencesTool.ExecuteAsync(
            Workspace, "IOrderRepository", maxResults: 200, ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<ReferencesResult>(json)!;

        Assert.All(result.References, r => Assert.DoesNotContain("..", r.File));
    }

    [Fact]
    public async Task GetFileOutline_PathReturnedByAnotherTool_RoundTrips()
    {
        var json = await GetFileOutlineTool.ExecuteAsync(
            Workspace, "SecondSolution/ShippingService/ShipmentDispatcher.cs", ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<FileOutlineResult>(json)!;

        Assert.Equal("SecondSolution/ShippingService/ShipmentDispatcher.cs", result.File);
    }

    [Fact]
    public async Task GetProjectGraph_ListsEverySolution()
    {
        var result = await GetProjectGraphAsync();

        Assert.Equal(Primary, result.Solution);
        Assert.NotNull(result.Solutions);
        Assert.Equal([Primary, Second], result.Solutions.Select(s => s.Name));
        Assert.Equal(4, result.Solutions.Single(s => s.Name == Primary).ProjectCount);
        Assert.Equal(2, result.Solutions.Single(s => s.Name == Second).ProjectCount);
    }

    [Fact]
    public async Task GetProjectGraph_ReportsSolutionMembershipPerProject()
    {
        var result = await GetProjectGraphAsync();

        Assert.Equal([Primary, Second], result.Projects.Single(p => p.Name == "SampleDomain").Solutions!);
        Assert.Equal([Primary], result.Projects.Single(p => p.Name == "SampleApi").Solutions!);
        Assert.Equal([Second], result.Projects.Single(p => p.Name == "ShippingService").Solutions!);
    }

    [Fact]
    public void ComputeRootDirectory_SiblingSolutions_ReturnsTheirParent()
    {
        var root = WorkspaceManager.ComputeRootDirectory(
            [TestSolutionFixture.SampleSolutionPath, TestSolutionFixture.SecondSolutionPath]);

        Assert.Equal(Path.GetDirectoryName(Path.GetDirectoryName(TestSolutionFixture.SampleSolutionPath)), root);
    }

    [Fact]
    public void ComputeRootDirectory_SingleSolution_ReturnsItsDirectory()
    {
        var root = WorkspaceManager.ComputeRootDirectory([TestSolutionFixture.SampleSolutionPath]);

        Assert.Equal(Path.GetDirectoryName(TestSolutionFixture.SampleSolutionPath), root);
    }

    private async Task<ProjectGraphResult> GetProjectGraphAsync()
    {
        var json = await GetProjectGraphTool.ExecuteAsync(Workspace, TestContext.Current.CancellationToken);
        return JsonSerializer.Deserialize<ProjectGraphResult>(json)!;
    }
}
