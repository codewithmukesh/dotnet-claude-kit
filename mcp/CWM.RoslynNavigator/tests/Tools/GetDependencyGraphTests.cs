using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;

namespace CWM.RoslynNavigator.Tests.Tools;

public class GetDependencyGraphTests(TestSolutionFixture fixture) : IClassFixture<TestSolutionFixture>
{
    [Fact]
    public async Task GetDependencyGraph_ServiceMethod_ReturnsDependencies()
    {
        var json = await GetDependencyGraphTool.ExecuteAsync(
            fixture.WorkspaceManager, "CreateOrderAsync",
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<DependencyGraphResult>(json)!;

        // CreateOrderAsync calls Order.Create and repository methods
        Assert.True(result.TotalNodes > 0, "Expected dependencies for CreateOrderAsync");
        Assert.Contains(result.Dependencies, d => d.Symbol == "Create");
    }

    [Fact]
    public async Task GetDependencyGraph_DepthLimit_RespectsMaxDepth()
    {
        var json = await GetDependencyGraphTool.ExecuteAsync(
            fixture.WorkspaceManager, "CreateOrderAsync", depth: 1,
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<DependencyGraphResult>(json)!;

        // All nodes should be at depth 1
        Assert.All(result.Dependencies, d => Assert.Equal(1, d.Depth));
    }

    [Fact]
    public async Task GetDependencyGraph_NonexistentMethod_ReturnsSymbolNotFound()
    {
        var json = await GetDependencyGraphTool.ExecuteAsync(
            fixture.WorkspaceManager, "NonExistentMethod12345",
            ct: TestContext.Current.CancellationToken);
        var error = JsonSerializer.Deserialize<ErrorResponse>(json)!;

        Assert.Equal(ErrorCodes.SymbolNotFound, error.Error);
    }

    [Fact]
    public async Task GetDependencyGraph_TypeInsteadOfMethod_ReturnsWrongSymbolKind()
    {
        var json = await GetDependencyGraphTool.ExecuteAsync(
            fixture.WorkspaceManager, "OrderService", ct: TestContext.Current.CancellationToken);
        var error = JsonSerializer.Deserialize<ErrorResponse>(json)!;

        Assert.Equal(ErrorCodes.WrongSymbolKind, error.Error);
    }

    [Fact]
    public async Task GetDependencyGraph_RootSymbol_IsSet()
    {
        var json = await GetDependencyGraphTool.ExecuteAsync(
            fixture.WorkspaceManager, "GetOrderAsync",
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<DependencyGraphResult>(json)!;

        Assert.Contains("GetOrderAsync", result.RootSymbol);
    }

    [Fact]
    public async Task GetDependencyGraph_DepthClampedTo5()
    {
        // Passing depth > 5 should be clamped to 5
        var json = await GetDependencyGraphTool.ExecuteAsync(
            fixture.WorkspaceManager, "CreateOrderAsync", depth: 100,
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<DependencyGraphResult>(json)!;

        // Should not crash, results should be bounded
        Assert.NotNull(result);
        Assert.All(result.Dependencies, d => Assert.True(d.Depth <= 5));
    }
}
