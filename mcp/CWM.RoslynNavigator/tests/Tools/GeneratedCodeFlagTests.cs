using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;

namespace CWM.RoslynNavigator.Tests.Tools;

/// <summary>
/// Navigation results flag generated source so callers do not try to edit a file
/// that the next build will overwrite.
/// </summary>
public class GeneratedCodeFlagTests(TestSolutionFixture fixture) : IClassFixture<TestSolutionFixture>
{
    [Fact]
    public async Task FindSymbol_GeneratedType_IsFlagged()
    {
        var json = await FindSymbolTool.ExecuteAsync(
            fixture.WorkspaceManager, "GeneratedOrderSummary", "class",
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<SymbolSearchResult>(json)!;

        var symbol = Assert.Single(result.Symbols);
        Assert.True(symbol.IsGenerated, "GeneratedOrderSummary lives in a .g.cs file");
    }

    [Fact]
    public async Task FindSymbol_HandWrittenType_IsNotFlagged()
    {
        var json = await FindSymbolTool.ExecuteAsync(
            fixture.WorkspaceManager, "OrderService", "class",
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<SymbolSearchResult>(json)!;

        var symbol = Assert.Single(result.Symbols);
        Assert.False(symbol.IsGenerated);
    }

    [Fact]
    public async Task FindReferences_UsageInsideGeneratedFile_IsFlagged()
    {
        // Order.Status is read by GeneratedOrderSummary.Describe and by hand-written code,
        // so this asserts the flag varies per reference rather than per query.
        var json = await FindReferencesTool.ExecuteAsync(
            fixture.WorkspaceManager, "SampleDomain.Order.Status", maxResults: 100,
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<ReferencesResult>(json)!;

        Assert.Contains(result.References, r => r.IsGenerated && r.File.EndsWith(".g.cs"));
        Assert.Contains(result.References, r => !r.IsGenerated);
    }

    [Fact]
    public async Task FindCallers_HandWrittenCaller_IsNotFlagged()
    {
        var json = await FindCallersTool.ExecuteAsync(
            fixture.WorkspaceManager, "IOrderRepository.GetByIdAsync",
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<CallersResult>(json)!;

        Assert.NotEmpty(result.Callers);
        Assert.All(result.Callers, c => Assert.False(c.IsGenerated));
    }
}
