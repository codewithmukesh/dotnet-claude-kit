using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;

namespace CWM.RoslynNavigator.Tests.Tools;

public class GetPublicApiTests(TestSolutionFixture fixture) : IClassFixture<TestSolutionFixture>
{
    [Fact]
    public async Task GetPublicApi_Interface_ReturnsAllMembers()
    {
        var json = await GetPublicApiTool.ExecuteAsync(fixture.WorkspaceManager, "IOrderRepository", ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<PublicApiResult>(json)!;

        Assert.Equal("interface", result.Type);
        Assert.Equal(4, result.Members.Count); // GetByIdAsync, GetAllAsync, AddAsync, UpdateAsync
        Assert.All(result.Members, m => Assert.Equal("method", m.Kind));
    }

    [Fact]
    public async Task GetPublicApi_Class_ReturnsPublicMembersOnly()
    {
        var json = await GetPublicApiTool.ExecuteAsync(fixture.WorkspaceManager, "Order", ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<PublicApiResult>(json)!;

        Assert.Equal("class", result.Type);
        // Should include public properties and methods, exclude private ones
        Assert.Contains(result.Members, m => m.Kind == "property");
        Assert.Contains(result.Members, m => m.Kind == "method" && m.Signature.Contains("Cancel"));
        Assert.Contains(result.Members, m => m.Kind == "method" && m.Signature.Contains("Ship"));
        Assert.Contains(result.Members, m => m.Kind == "method" && m.Signature.Contains("Create"));
    }

    [Fact]
    public async Task GetPublicApi_ExcludesPrivateMembers()
    {
        var json = await GetPublicApiTool.ExecuteAsync(fixture.WorkspaceManager, "Order", ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<PublicApiResult>(json)!;

        // The private constructor should not appear
        Assert.DoesNotContain(result.Members, m =>
            m.Kind == "constructor" && m.Accessibility == "private");
    }

    [Fact]
    public async Task GetPublicApi_NonexistentType_ReturnsSymbolNotFound()
    {
        var json = await GetPublicApiTool.ExecuteAsync(fixture.WorkspaceManager, "ZZZNonExistent", ct: TestContext.Current.CancellationToken);
        var error = JsonSerializer.Deserialize<ErrorResponse>(json)!;

        Assert.Equal(ErrorCodes.SymbolNotFound, error.Error);
    }

    [Fact]
    public async Task GetPublicApi_SymbolIsNotAType_ReturnsWrongSymbolKind()
    {
        var json = await GetPublicApiTool.ExecuteAsync(
            fixture.WorkspaceManager, "OrderService.GetOrderAsync", ct: TestContext.Current.CancellationToken);
        var error = JsonSerializer.Deserialize<ErrorResponse>(json)!;

        Assert.Equal(ErrorCodes.WrongSymbolKind, error.Error);
        Assert.Contains("method", error.Message);
    }
}
