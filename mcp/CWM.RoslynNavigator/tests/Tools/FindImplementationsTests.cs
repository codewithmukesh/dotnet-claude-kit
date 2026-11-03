using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;

namespace CWM.RoslynNavigator.Tests.Tools;

[Collection(SampleSolutionCollection.Name)]
public class FindImplementationsTests(TestSolutionFixture fixture)
{
    [Fact]
    public async Task FindImplementations_InterfaceWithTwoImpls_ReturnsBoth()
    {
        var json = await FindImplementationsTool.ExecuteAsync(fixture.WorkspaceManager, "IOrderRepository", ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<ImplementationsResult>(json)!;

        Assert.Equal(2, result.Implementations.Count);
        Assert.Contains(result.Implementations, i => i.Type == "InMemoryOrderRepository");
        Assert.Contains(result.Implementations, i => i.Type == "CachedOrderRepository");
    }

    [Fact]
    public async Task FindImplementations_InterfaceWithOneImpl_ReturnsSingle()
    {
        var json = await FindImplementationsTool.ExecuteAsync(fixture.WorkspaceManager, "IProductRepository", ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<ImplementationsResult>(json)!;

        Assert.Single(result.Implementations);
        Assert.Equal("InMemoryProductRepository", result.Implementations[0].Type);
    }

    [Fact]
    public async Task FindImplementations_AbstractClass_ReturnsDerived()
    {
        var json = await FindImplementationsTool.ExecuteAsync(fixture.WorkspaceManager, "BaseEntity", ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<ImplementationsResult>(json)!;

        Assert.Contains(result.Implementations, i => i.Type == "AuditableProduct");
    }

    [Fact]
    public async Task FindImplementations_ClassWithNoImpls_ReturnsEmpty()
    {
        var json = await FindImplementationsTool.ExecuteAsync(fixture.WorkspaceManager, "Product", ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<ImplementationsResult>(json)!;

        Assert.Empty(result.Implementations);
    }
}
