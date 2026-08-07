using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;

namespace CWM.RoslynNavigator.Tests.Tools;

public class FindCallersTests(TestSolutionFixture fixture) : IClassFixture<TestSolutionFixture>
{
    [Fact]
    public async Task FindCallers_MethodCalledFromService_ReturnsCallers()
    {
        var json = await FindCallersTool.ExecuteAsync(
            fixture.WorkspaceManager, "IOrderRepository.GetByIdAsync",
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<CallersResult>(json)!;

        // IOrderRepository.GetByIdAsync is called from OrderService and CachedOrderRepository
        Assert.True(result.Count > 0, "Expected callers of IOrderRepository.GetByIdAsync");
    }

    [Fact]
    public async Task FindCallers_AmbiguousBareName_ReportsCandidatesInsteadOfGuessing()
    {
        // GetByIdAsync is declared on two interfaces and three implementations. Picking one
        // silently would report the wrong call sites with full confidence.
        var json = await FindCallersTool.ExecuteAsync(
            fixture.WorkspaceManager, "GetByIdAsync", ct: TestContext.Current.CancellationToken);
        var error = JsonSerializer.Deserialize<ErrorResponse>(json)!;

        Assert.Equal(ErrorCodes.AmbiguousMatch, error.Error);
        Assert.NotNull(error.Candidates);
        Assert.True(error.Candidates.Count > 1);
        Assert.Contains(error.Candidates, c => c.Qualified == "SampleDomain.IOrderRepository.GetByIdAsync");
    }

    [Fact]
    public async Task FindCallers_WithClassName_DisambiguatesCorrectly()
    {
        var json = await FindCallersTool.ExecuteAsync(
            fixture.WorkspaceManager, "Cancel", className: "Order",
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<CallersResult>(json)!;

        // Order.Cancel() is called from OrderService.CancelOrderAsync
        Assert.True(result.Count > 0, "Expected callers of Order.Cancel");
        Assert.Contains(result.Callers, c => c.ContainingType == "OrderService");
    }

    [Fact]
    public async Task FindCallers_NonexistentMethod_ReturnsSymbolNotFound()
    {
        var json = await FindCallersTool.ExecuteAsync(
            fixture.WorkspaceManager, "MethodThatDoesNotExist12345",
            ct: TestContext.Current.CancellationToken);
        var error = JsonSerializer.Deserialize<ErrorResponse>(json)!;

        // Distinct from a real method with zero callers, which returns an empty CallersResult.
        Assert.Equal(ErrorCodes.SymbolNotFound, error.Error);
    }

    [Fact]
    public async Task FindCallers_FactoryMethod_ReturnsCallers()
    {
        var json = await FindCallersTool.ExecuteAsync(
            fixture.WorkspaceManager, "Create", className: "Order",
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<CallersResult>(json)!;

        // Order.Create is called from OrderService.CreateOrderAsync
        Assert.True(result.Count > 0, "Expected callers of Order.Create");
    }
}
