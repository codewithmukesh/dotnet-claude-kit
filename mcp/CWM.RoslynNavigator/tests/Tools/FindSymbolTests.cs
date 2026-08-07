using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;

namespace CWM.RoslynNavigator.Tests.Tools;

public class FindSymbolTests(TestSolutionFixture fixture) : IClassFixture<TestSolutionFixture>
{
    [Fact]
    public async Task FindSymbol_Class_ReturnsCorrectLocation()
    {
        var json = await FindSymbolTool.ExecuteAsync(fixture.WorkspaceManager, "Order", "class", ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<SymbolSearchResult>(json)!;

        Assert.NotEmpty(result.Symbols);
        Assert.Contains(result.Symbols, s => s.Name == "Order" && s.Kind == "class");
    }

    [Fact]
    public async Task FindSymbol_Interface_ReturnsCorrectKind()
    {
        var json = await FindSymbolTool.ExecuteAsync(fixture.WorkspaceManager, "IOrderRepository", "interface", ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<SymbolSearchResult>(json)!;

        Assert.Single(result.Symbols);
        Assert.Equal("interface", result.Symbols[0].Kind);
        Assert.Equal("SampleDomain", result.Symbols[0].Namespace);
    }

    [Fact]
    public async Task FindSymbol_Method_ReturnsMethodKind()
    {
        var json = await FindSymbolTool.ExecuteAsync(fixture.WorkspaceManager, "Cancel", "method", ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<SymbolSearchResult>(json)!;

        Assert.NotEmpty(result.Symbols);
        Assert.Contains(result.Symbols, s => s.Kind == "method");
    }

    [Fact]
    public async Task FindSymbol_Nonexistent_ReturnsEmpty()
    {
        var json = await FindSymbolTool.ExecuteAsync(fixture.WorkspaceManager, "NonExistentType12345", ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<SymbolSearchResult>(json)!;

        Assert.Empty(result.Symbols);
    }

    [Fact]
    public async Task FindSymbol_Enum_ReturnsEnumKind()
    {
        var json = await FindSymbolTool.ExecuteAsync(fixture.WorkspaceManager, "OrderStatus", "enum", ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<SymbolSearchResult>(json)!;

        Assert.Single(result.Symbols);
        Assert.Equal("enum", result.Symbols[0].Kind);
    }

    [Fact]
    public async Task FindSymbol_MaxResultsBelowTotal_FlagsTruncatedAndReportsLimit()
    {
        var json = await FindSymbolTool.ExecuteAsync(
            fixture.WorkspaceManager, "GetByIdAsync", "method", maxResults: 1,
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<SymbolSearchResult>(json)!;

        Assert.True(result.Truncated);
        Assert.Equal(1, result.Limit);
        Assert.Single(result.Symbols);
        Assert.True(result.TotalFound > 1);
    }

    [Fact]
    public async Task FindSymbol_CompleteResultSet_IsNotFlaggedTruncated()
    {
        var json = await FindSymbolTool.ExecuteAsync(
            fixture.WorkspaceManager, "OrderStatus", "enum", maxResults: 50,
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<SymbolSearchResult>(json)!;

        Assert.False(result.Truncated);
        Assert.Equal(result.TotalFound, result.Count);
    }

    [Fact]
    public async Task FindSymbol_TypeQualifiedMethod_NarrowsToOwningType()
    {
        var bare = JsonSerializer.Deserialize<SymbolSearchResult>(
            await FindSymbolTool.ExecuteAsync(fixture.WorkspaceManager, "GetByIdAsync", "method",
                ct: TestContext.Current.CancellationToken))!;

        var qualified = JsonSerializer.Deserialize<SymbolSearchResult>(
            await FindSymbolTool.ExecuteAsync(fixture.WorkspaceManager, "IOrderRepository.GetByIdAsync", "method",
                ct: TestContext.Current.CancellationToken))!;

        Assert.True(bare.TotalFound > 1);
        Assert.Equal(1, qualified.TotalFound);
        Assert.Equal("SampleDomain", qualified.Symbols[0].Namespace);
    }

    [Fact]
    public async Task FindSymbol_FullyQualifiedType_Resolves()
    {
        var json = await FindSymbolTool.ExecuteAsync(
            fixture.WorkspaceManager, "SampleDomain.Order", "class", ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<SymbolSearchResult>(json)!;

        Assert.Single(result.Symbols);
        Assert.Equal("Order", result.Symbols[0].Name);
    }

    [Fact]
    public async Task FindSymbol_Record_ReturnsRecordKind()
    {
        var json = await FindSymbolTool.ExecuteAsync(fixture.WorkspaceManager, "OrderItem", "record", ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<SymbolSearchResult>(json)!;

        Assert.NotEmpty(result.Symbols);
    }
}
