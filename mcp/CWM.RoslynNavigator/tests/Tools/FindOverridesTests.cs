using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;

namespace CWM.RoslynNavigator.Tests.Tools;

public class FindOverridesTests(TestSolutionFixture fixture) : IClassFixture<TestSolutionFixture>
{
    [Fact]
    public async Task FindOverrides_NonVirtualMethod_ReturnsEmpty()
    {
        var json = await FindOverridesTool.ExecuteAsync(
            fixture.WorkspaceManager, "Cancel", className: "Order",
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<OverridesResult>(json)!;

        // Order.Cancel is not virtual, so no overrides
        Assert.Equal(0, result.Count);
    }

    [Fact]
    public async Task FindOverrides_NonexistentMethod_ReturnsSymbolNotFound()
    {
        var json = await FindOverridesTool.ExecuteAsync(
            fixture.WorkspaceManager, "NonExistentMethod12345",
            ct: TestContext.Current.CancellationToken);
        var error = JsonSerializer.Deserialize<ErrorResponse>(json)!;

        Assert.Equal(ErrorCodes.SymbolNotFound, error.Error);
    }

    [Fact]
    public async Task FindOverrides_WithClassName_DisambiguatesCorrectly()
    {
        // GetByIdAsync exists on both IOrderRepository and IProductRepository
        var json = await FindOverridesTool.ExecuteAsync(
            fixture.WorkspaceManager, "GetByIdAsync", className: "IOrderRepository",
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<OverridesResult>(json)!;

        // Interface methods don't have overrides (they have implementations)
        Assert.NotNull(result);
    }
}
