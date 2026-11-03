using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;

namespace CWM.RoslynNavigator.Tests.Tools;

[Collection(SampleSolutionCollection.Name)]
public class GetTestCoverageMapTests(TestSolutionFixture fixture)
{
    [Fact]
    public async Task GetTestCoverageMap_Solution_FindsProductionTypes()
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
        // SampleTests is a test project, so its types are never listed as production types
        // (TotalTypes is unchanged by adding it). Every class it declares counts as a test
        // class — OrderTests, OrderApiTests and the five framework stubs — and each [Fact] /
        // [Theory] method counts as a test.
        Assert.Equal(28, result.TotalTypes);
        Assert.Equal(7, result.TestClassCount);
        Assert.Equal(4, result.TestMethodCount);
    }

    [Fact]
    public async Task GetTestCoverageMap_TestClassOnlyInProductionProject_DoesNotCount()
    {
        // SampleApi.OrderServiceTests sits in a production project. Only classes in test
        // projects (SampleTests) are candidates, so OrderService stays untested.
        var json = await GetTestCoverageMapTool.ExecuteAsync(
            fixture.WorkspaceManager, projectFilter: "SampleApi",
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<TestCoverageMapResult>(json)!;

        var orderServiceEntry = result.Coverage.FirstOrDefault(c => c.Type == "OrderService");
        Assert.NotNull(orderServiceEntry);
        Assert.False(orderServiceEntry.HasTests, "OrderServiceTests is not in a test project");
    }

    [Fact]
    public async Task GetTestCoverageMap_TestProjectHasMatchingClass_MarksTypeTested()
    {
        // Arrange — SampleTests.OrderTests matches SampleDomain.Order by name.
        var ct = TestContext.Current.CancellationToken;

        // Act
        var json = await GetTestCoverageMapTool.ExecuteAsync(
            fixture.WorkspaceManager, projectFilter: "SampleDomain", ct: ct);

        // Assert
        var result = JsonSerializer.Deserialize<TestCoverageMapResult>(json)!;
        var order = Assert.Single(result.Coverage, c => c.Type == "Order");
        Assert.True(order.HasTests);
        Assert.Equal("SampleTests/OrderTests.cs", order.TestFile);
        Assert.Equal(1, result.TestedTypes);
        Assert.All(result.Coverage.Where(c => c.Type != "Order"), c => Assert.False(c.HasTests, c.Type));
    }

    [Fact]
    public async Task GetTestCoverageMap_TestProjectFilter_ListsNoProductionTypes()
    {
        // Act
        var json = await GetTestCoverageMapTool.ExecuteAsync(
            fixture.WorkspaceManager, projectFilter: "SampleTests", ct: TestContext.Current.CancellationToken);

        // Assert — a known project, so not an error; but it holds tests, not production types.
        var result = JsonSerializer.Deserialize<TestCoverageMapResult>(json)!;
        Assert.Empty(result.Coverage);
        Assert.Equal(0, result.TotalTypes);
    }

    [Fact]
    public async Task GetTestCoverageMap_TypeWithoutTestClass_IsUntested()
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
    public async Task GetTestCoverageMap_Solution_PercentageWithinBounds()
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
