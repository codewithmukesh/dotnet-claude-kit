using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;

namespace CWM.RoslynNavigator.Tests.Tools;

public class GetSymbolDetailTests(TestSolutionFixture fixture) : IClassFixture<TestSolutionFixture>
{
    [Fact]
    public async Task GetSymbolDetail_Class_ReturnsFullInfo()
    {
        var json = await GetSymbolDetailTool.ExecuteAsync(
            fixture.WorkspaceManager, "Order",
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<SymbolDetail>(json)!;

        Assert.Equal("Order", result.Name);
        Assert.Equal("class", result.Kind);
        Assert.Equal("SampleDomain", result.Namespace);
        Assert.NotNull(result.XmlDoc);
        Assert.Contains("customer order", result.XmlDoc, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetSymbolDetail_Method_ReturnsParametersAndReturnType()
    {
        var json = await GetSymbolDetailTool.ExecuteAsync(
            fixture.WorkspaceManager, "Create", containingType: "Order",
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<SymbolDetail>(json)!;

        Assert.Equal("Create", result.Name);
        Assert.Equal("method", result.Kind);
        Assert.NotNull(result.Parameters);
        Assert.True(result.Parameters.Count >= 2, "Create should have parameters");
        Assert.NotNull(result.ReturnType);
    }

    [Fact]
    public async Task GetSymbolDetail_Property_ReturnsType()
    {
        var json = await GetSymbolDetailTool.ExecuteAsync(
            fixture.WorkspaceManager, "CustomerId", containingType: "Order",
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<SymbolDetail>(json)!;

        Assert.Equal("CustomerId", result.Name);
        Assert.Equal("property", result.Kind);
        Assert.Equal("string", result.ReturnType);
    }

    [Fact]
    public async Task GetSymbolDetail_Interface_ReturnsXmlDoc()
    {
        var json = await GetSymbolDetailTool.ExecuteAsync(
            fixture.WorkspaceManager, "IOrderRepository",
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<SymbolDetail>(json)!;

        Assert.Equal("IOrderRepository", result.Name);
        Assert.Equal("interface", result.Kind);
        Assert.NotNull(result.XmlDoc);
    }

    [Fact]
    public async Task GetSymbolDetail_Nonexistent_ReturnsSymbolNotFound()
    {
        var json = await GetSymbolDetailTool.ExecuteAsync(
            fixture.WorkspaceManager, "ZZZNonExistent12345",
            ct: TestContext.Current.CancellationToken);
        var error = JsonSerializer.Deserialize<ErrorResponse>(json)!;

        Assert.Equal(ErrorCodes.SymbolNotFound, error.Error);
    }
}
