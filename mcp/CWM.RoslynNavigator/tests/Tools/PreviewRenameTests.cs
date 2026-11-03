using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;

namespace CWM.RoslynNavigator.Tests.Tools;

[Collection(SampleSolutionCollection.Name)]
public class PreviewRenameTests(TestSolutionFixture fixture)
{
    [Fact]
    public async Task PreviewRename_InterfaceMethod_ReachesImplementationsAndCallSites()
    {
        var result = await PreviewAsync("IOrderRepository.GetByIdAsync", "FindByIdAsync");

        Assert.Equal("GetByIdAsync", result.OldName);
        Assert.Empty(result.Conflicts);
        Assert.Contains(result.Changes, c => c.File == "SampleDomain/IOrderRepository.cs");
        Assert.Contains(result.Changes, c => c.File == "SampleInfrastructure/InMemoryOrderRepository.cs");
        Assert.Contains(result.Changes, c => c.File == "SampleInfrastructure/CachedOrderRepository.cs");

        var call = Assert.Single(result.Changes, c => c.File == "SampleApi/OrderService.cs" && c.Line == 19);
        Assert.Equal("return await _repository.GetByIdAsync(id, ct);", call.Old);
        Assert.Equal("return await _repository.FindByIdAsync(id, ct);", call.New);

        // IProductRepository.GetByIdAsync is a different symbol and must be untouched.
        Assert.DoesNotContain(result.Changes, c => c.File.Contains("Product"));
    }

    [Fact]
    public async Task PreviewRename_AnyRename_NeverTouchesWorkspaceOrDisk()
    {
        var ct = TestContext.Current.CancellationToken;
        var workspace = fixture.WorkspaceManager;
        var solutionBefore = workspace.GetSolution();
        var versionBefore = workspace.SnapshotVersion;
        var path = Path.Combine(workspace.SolutionDirectory!, "SampleApi", "OrderService.cs");
        var diskBefore = await File.ReadAllTextAsync(path, ct);

        await PreviewAsync("IOrderRepository.GetByIdAsync", "FindByIdAsync");

        Assert.Same(solutionBefore, workspace.GetSolution());
        Assert.Equal(versionBefore, workspace.SnapshotVersion);
        Assert.Equal(diskBefore, await File.ReadAllTextAsync(path, ct));
        var stillThere = JsonSerializer.Deserialize<SymbolSearchResult>(
            await FindSymbolTool.ExecuteAsync(workspace, "IOrderRepository.GetByIdAsync", ct: ct))!;
        Assert.Equal(1, stillThere.Count);
    }

    [Fact]
    public async Task PreviewRename_ClashWithExistingMember_ReportsConflict()
    {
        // Order already has Ship(); renaming Cancel() to Ship duplicates the signature.
        var result = await PreviewAsync("Order.Cancel", "Ship");

        Assert.True(result.ConflictCount > 0);
        Assert.Contains(result.Conflicts, c => c.Id == "CS0111" && c.File == "SampleDomain/Order.cs");
    }

    [Fact]
    public async Task PreviewRename_Cap_TruncatesChanges()
    {
        var result = await PreviewAsync("IOrderRepository.GetByIdAsync", "FindByIdAsync", maxResults: 1);

        Assert.Equal(1, result.Count);
        Assert.True(result.Truncated);
        Assert.True(result.TotalFound > 1);
        Assert.True(result.Files > 1);
    }

    [Theory]
    [InlineData("1abc")]
    [InlineData("class")]
    [InlineData("has space")]
    [InlineData("")]
    public async Task PreviewRename_InvalidIdentifier_ReturnsInvalidArgument(string newName)
    {
        var json = await PreviewRenameTool.ExecuteAsync(
            fixture.WorkspaceManager, "Order.Cancel", newName, ct: TestContext.Current.CancellationToken);

        Assert.Equal(ErrorCodes.InvalidArgument, JsonSerializer.Deserialize<ErrorResponse>(json)!.Error);
    }

    [Theory]
    [InlineData("@class", true)]
    [InlineData("var", true)]
    [InlineData("_Value2", true)]
    [InlineData("int", false)]
    public void IsValidIdentifier_KeywordsAndNames_ClassifiesCorrectly(string name, bool expected) =>
        Assert.Equal(expected, PreviewRenameTool.IsValidIdentifier(name));

    [Fact]
    public async Task PreviewRename_MetadataSymbol_ReturnsNoSource()
    {
        var json = await PreviewRenameTool.ExecuteAsync(
            fixture.WorkspaceManager, "T:System.Guid", "Uuid", ct: TestContext.Current.CancellationToken);

        Assert.Equal(ErrorCodes.NoSource, JsonSerializer.Deserialize<ErrorResponse>(json)!.Error);
    }

    private async Task<RenamePreviewResult> PreviewAsync(string symbol, string newName, int maxResults = 100)
    {
        var json = await PreviewRenameTool.ExecuteAsync(
            fixture.WorkspaceManager, symbol, newName, maxResults: maxResults, ct: TestContext.Current.CancellationToken);
        Assert.DoesNotContain("\"Error\"", json);
        return JsonSerializer.Deserialize<RenamePreviewResult>(json)!;
    }
}
