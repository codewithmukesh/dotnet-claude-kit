using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;

namespace CWM.RoslynNavigator.Tests.Tools;

[Collection(SampleSolutionCollection.Name)]
public class GetTestCoverageMapTests(TestSolutionFixture fixture)
{
    [Fact]
    public async Task GetTestCoverageMap_ReturnsResults()
    {
        var json = await GetTestCoverageMapTool.ExecuteAsync(
            fixture.WorkspaceManager,
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<TestCoverageMapResult>(json)!;

        Assert.True(result.TotalTypes > 0, "Expected production types to be found");
    }

    [Fact]
    public async Task GetTestCoverageMap_Solution_ListsEveryProductionTypeInDeclarationOrder()
    {
        // Pins the full result so declaration-walk optimisations cannot drift it.
        var json = await GetTestCoverageMapTool.ExecuteAsync(
            fixture.WorkspaceManager, maxResults: 1000,
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<TestCoverageMapResult>(json)!;

        Assert.Equal(
            [
                "BaseEntity", "AuditableProduct", "Order", "OrderItem", "Product", "UnusedHelper",
                "CachedOrderRepository", "InMemoryOrderRepository", "InMemoryProductRepository",
                "LegacyPricingPlugin", "TrulyUnusedCalculator", "PluginLoader", "AntiPatternExamples",
                "CustomerWorkflowTests", "DbSet", "FakeDbContext", "FakeDatabaseFacade", "FakeChangeTracker",
                "Customer", "CustomerNote", "FalsePositiveExamples", "AuditMiddleware", "DomainOutcome",
                "HttpContext", "FakeHostLifetime", "OrderService", "OrderServiceTests", "ProductService",
            ],
            result.Coverage.Select(e => e.Type));
        Assert.Equal(28, result.TotalTypes);
        Assert.Equal(0, result.TestClassCount);
        Assert.Equal(0, result.TestMethodCount);
    }

    [Fact]
    public async Task GetTestCoverageMap_NoTestProjects_AllTypesUntested()
    {
        // SampleSolution has no dedicated test projects (no xUnit/NUnit references,
        // no project names ending in "Tests"), so all types should be untested.
        var json = await GetTestCoverageMapTool.ExecuteAsync(
            fixture.WorkspaceManager, projectFilter: "SampleApi",
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<TestCoverageMapResult>(json)!;

        var orderServiceEntry = result.Coverage.FirstOrDefault(c => c.Type == "OrderService");
        Assert.NotNull(orderServiceEntry);
        Assert.False(orderServiceEntry.HasTests, "No test projects exist in SampleSolution");
    }

    [Fact]
    public async Task GetTestCoverageMap_DetectsUntestedTypes()
    {
        var json = await GetTestCoverageMapTool.ExecuteAsync(
            fixture.WorkspaceManager, projectFilter: "SampleApi",
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<TestCoverageMapResult>(json)!;

        // ProductService has no matching test class
        var productServiceEntry = result.Coverage.FirstOrDefault(c => c.Type == "ProductService");
        Assert.NotNull(productServiceEntry);
        Assert.False(productServiceEntry.HasTests, "ProductService should not have matching test class");
    }

    [Fact]
    public async Task GetTestCoverageMap_PercentageCalculation()
    {
        var json = await GetTestCoverageMapTool.ExecuteAsync(
            fixture.WorkspaceManager,
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<TestCoverageMapResult>(json)!;

        Assert.True(result.Percentage >= 0 && result.Percentage <= 100);
        Assert.True(result.TestedTypes <= result.TotalTypes);
    }

    [Fact]
    public async Task GetTestCoverageMap_MaxResults_LimitsOutput()
    {
        var json = await GetTestCoverageMapTool.ExecuteAsync(
            fixture.WorkspaceManager, maxResults: 3,
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<TestCoverageMapResult>(json)!;

        Assert.True(result.Coverage.Count <= 3);
    }

    [Fact]
    public async Task GetTestCoverageMap_ProjectFilter_FiltersCorrectly()
    {
        var json = await GetTestCoverageMapTool.ExecuteAsync(
            fixture.WorkspaceManager, projectFilter: "SampleDomain",
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<TestCoverageMapResult>(json)!;

        // Should only contain types from SampleDomain
        Assert.All(result.Coverage, c =>
            Assert.Contains("SampleDomain", c.File, StringComparison.OrdinalIgnoreCase));
    }
}
