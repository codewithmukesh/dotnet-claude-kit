using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;

namespace CWM.RoslynNavigator.Tests.Tools;

public class GetSymbolSourceTests(TestSolutionFixture fixture) : IClassFixture<TestSolutionFixture>
{
    [Fact]
    public async Task GetSymbolSource_Method_ReturnsFullBodyWithLines()
    {
        var json = await GetSymbolSourceTool.ExecuteAsync(
            fixture.WorkspaceManager, "OrderService.GetOrderAsync", ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<SymbolSourceResult>(json)!;

        Assert.Equal("GetOrderAsync", result.Name);
        Assert.Equal("method", result.Kind);
        Assert.Equal("SampleApi/OrderService.cs", result.File);
        Assert.Contains("GetByIdAsync", result.Source);
        Assert.True(result.StartLine > 0);
        Assert.True(result.EndLine >= result.StartLine);
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task GetSymbolSource_Type_DefaultsToSignaturesOnlySkeleton()
    {
        var json = await GetSymbolSourceTool.ExecuteAsync(
            fixture.WorkspaceManager, "OrderService", ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<SymbolSourceResult>(json)!;

        Assert.Equal("class", result.Kind);
        Assert.Contains("class OrderService", result.Source);
        Assert.Contains("GetOrderAsync", result.Source);
        // Bodies are stripped in skeleton mode
        Assert.DoesNotContain("GetByIdAsync", result.Source);
    }

    [Fact]
    public async Task GetSymbolSource_Type_IncludeBodies_ReturnsFullSource()
    {
        var json = await GetSymbolSourceTool.ExecuteAsync(
            fixture.WorkspaceManager, "OrderService", includeBodies: true, ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<SymbolSourceResult>(json)!;

        Assert.Contains("GetByIdAsync", result.Source);
    }

    [Fact]
    public async Task GetSymbolSource_MaxChars_TruncatesAndFlags()
    {
        var json = await GetSymbolSourceTool.ExecuteAsync(
            fixture.WorkspaceManager, "OrderService", includeBodies: true, maxChars: 200,
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<SymbolSourceResult>(json)!;

        Assert.True(result.Truncated);
        Assert.Equal(200, result.Source.Length);
    }

    [Fact]
    public async Task GetSymbolSource_UnknownSymbol_ReturnsSymbolNotFound()
    {
        var json = await GetSymbolSourceTool.ExecuteAsync(
            fixture.WorkspaceManager, "NoSuchSymbolXyz", ct: TestContext.Current.CancellationToken);
        var error = JsonSerializer.Deserialize<ErrorResponse>(json)!;

        Assert.Equal(ErrorCodes.SymbolNotFound, error.Error);
    }
}
