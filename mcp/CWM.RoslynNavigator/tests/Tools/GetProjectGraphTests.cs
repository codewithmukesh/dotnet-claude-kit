using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;

namespace CWM.RoslynNavigator.Tests.Tools;

public class GetProjectGraphTests(TestSolutionFixture fixture) : IClassFixture<TestSolutionFixture>
{
    [Fact]
    public async Task GetProjectGraph_ReturnsAllProjects()
    {
        var json = await GetProjectGraphTool.ExecuteAsync(fixture.WorkspaceManager, ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<ProjectGraphResult>(json)!;

        Assert.Equal(3, result.Projects.Count);
        Assert.Contains(result.Projects, p => p.Name == "SampleDomain");
        Assert.Contains(result.Projects, p => p.Name == "SampleInfrastructure");
        Assert.Contains(result.Projects, p => p.Name == "SampleApi");
    }

    [Fact]
    public async Task GetProjectGraph_CorrectReferences_InfrastructureDependsOnDomain()
    {
        var json = await GetProjectGraphTool.ExecuteAsync(fixture.WorkspaceManager, ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<ProjectGraphResult>(json)!;

        var infra = result.Projects.First(p => p.Name == "SampleInfrastructure");
        Assert.Contains("SampleDomain", infra.References);
    }

    [Fact]
    public async Task GetProjectGraph_CorrectReferences_ApiDependsOnBoth()
    {
        var json = await GetProjectGraphTool.ExecuteAsync(fixture.WorkspaceManager, ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<ProjectGraphResult>(json)!;

        var api = result.Projects.First(p => p.Name == "SampleApi");
        Assert.Contains("SampleDomain", api.References);
        Assert.Contains("SampleInfrastructure", api.References);
    }

    [Fact]
    public async Task GetProjectGraph_SolutionName_IsCorrect()
    {
        var json = await GetProjectGraphTool.ExecuteAsync(fixture.WorkspaceManager, ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<ProjectGraphResult>(json)!;

        Assert.Equal("SampleSolution.sln", result.Solution);
    }

    [Fact]
    public async Task GetProjectGraph_SingleSolution_OmitsMultiSolutionFields()
    {
        // Multi-solution support (#16) must not change single-solution output.
        var json = await GetProjectGraphTool.ExecuteAsync(fixture.WorkspaceManager, ct: TestContext.Current.CancellationToken);

        Assert.DoesNotContain("\"Solutions\"", json);
        Assert.Equal(
            Path.GetDirectoryName(TestSolutionFixture.SampleSolutionPath),
            fixture.WorkspaceManager.SolutionDirectory);
    }
}
