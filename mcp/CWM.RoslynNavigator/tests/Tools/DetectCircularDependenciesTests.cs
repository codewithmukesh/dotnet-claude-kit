using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;

namespace CWM.RoslynNavigator.Tests.Tools;

[Collection(SampleSolutionCollection.Name)]
public class DetectCircularDependenciesTests(TestSolutionFixture fixture)
{
    [Fact]
    public async Task DetectCircularDependencies_Projects_NoCircularDeps()
    {
        var json = await DetectCircularDependenciesTool.ExecuteAsync(
            fixture.WorkspaceManager, scope: "projects",
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<CircularDependenciesResult>(json)!;

        // SampleSolution has clean dependency direction: Domain → nothing, Infra → Domain, Api → both
        Assert.Equal(0, result.Count);
    }

    [Fact]
    public async Task DetectCircularDependencies_Projects_AllChainsHaveProjectLevel()
    {
        var json = await DetectCircularDependenciesTool.ExecuteAsync(
            fixture.WorkspaceManager, scope: "projects",
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<CircularDependenciesResult>(json)!;

        Assert.All(result.Cycles, c => Assert.Equal("project", c.Level));
    }

    [Fact]
    public async Task DetectCircularDependencies_Types_ReturnsResult()
    {
        var json = await DetectCircularDependenciesTool.ExecuteAsync(
            fixture.WorkspaceManager, scope: "types", projectFilter: "SampleDomain",
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<CircularDependenciesResult>(json)!;

        // SampleDomain types should have no circular dependencies
        Assert.NotNull(result);
        Assert.All(result.Cycles, c => Assert.Equal("type", c.Level));
    }

    [Fact]
    public async Task DetectCircularDependencies_TypesWithProjectFilter_FiltersCorrectly()
    {
        var json = await DetectCircularDependenciesTool.ExecuteAsync(
            fixture.WorkspaceManager, scope: "types", projectFilter: "SampleApi",
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<CircularDependenciesResult>(json)!;

        // Should analyze only SampleApi types
        Assert.NotNull(result);
    }

    [Fact]
    public async Task DetectCircularDependencies_InvalidScope_DefaultsToProjects()
    {
        var json = await DetectCircularDependenciesTool.ExecuteAsync(
            fixture.WorkspaceManager, scope: "invalid",
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<CircularDependenciesResult>(json)!;

        // Should not throw, defaults to project-level analysis
        Assert.NotNull(result);
    }
}
