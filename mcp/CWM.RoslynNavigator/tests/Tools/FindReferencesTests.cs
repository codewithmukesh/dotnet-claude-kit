using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;

namespace CWM.RoslynNavigator.Tests.Tools;

[Collection(SampleSolutionCollection.Name)]
public class FindReferencesTests(TestSolutionFixture fixture)
{
    [Fact]
    public async Task FindReferences_CrossProjectInterface_ReturnsMultipleReferences()
    {
        var json = await FindReferencesTool.ExecuteAsync(fixture.WorkspaceManager, "IOrderRepository", ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<ReferencesResult>(json)!;

        // IOrderRepository is referenced in Infrastructure (implementations) and Api (OrderService)
        Assert.True(result.Count > 0, "Expected references to IOrderRepository across projects");
    }

    [Fact]
    public async Task FindReferences_PagedByTwo_WalksEveryReferenceOnceInStableOrder()
    {
        var ct = TestContext.Current.CancellationToken;
        var all = JsonSerializer.Deserialize<ReferencesResult>(
            await FindReferencesTool.ExecuteAsync(fixture.WorkspaceManager, "Order", format: "full", ct: ct))!;
        var walked = new List<string>();
        int? offset = 0;

        while (offset is { } next)
        {
            var page = JsonSerializer.Deserialize<ReferencesResult>(
                await FindReferencesTool.ExecuteAsync(fixture.WorkspaceManager, "Order", maxResults: 2, offset: next, format: "full", ct: ct))!;
            Assert.Equal(all.TotalFound, page.TotalFound);
            Assert.Equal(all.TotalFound, page.ByProject!.Sum(p => p.Count));
            walked.AddRange(page.References!.Select(r => $"{r.File}:{r.Line}:{r.Kind}"));
            offset = page.NextOffset;
        }

        Assert.True(all.TotalFound > 2);
        Assert.Equal(all.References!.Select(r => $"{r.File}:{r.Line}:{r.Kind}"), walked);
    }

    [Fact]
    public async Task FindReferences_CompactFormat_GroupsEveryEntryByFile()
    {
        var json = await FindReferencesTool.ExecuteAsync(
            fixture.WorkspaceManager, "Order", format: "compact", ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<ReferencesResult>(json)!;

        Assert.Null(result.References);
        Assert.Equal(result.Count, result.Files!.Sum(f => f.Entries.Count));
        Assert.Equal(result.Files!.Count, result.Files!.Select(f => f.File).Distinct().Count());
        Assert.All(result.Files!.SelectMany(f => f.Entries), e => Assert.Matches(@"^\d+ [a-z-]+", e));
    }

    [Fact]
    public async Task FindReferences_ClassUsedInSameFile_ReturnsReferences()
    {
        var json = await FindReferencesTool.ExecuteAsync(fixture.WorkspaceManager, "Order", ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<ReferencesResult>(json)!;

        // Order is referenced in many places
        Assert.True(result.Count > 0);
    }

    [Fact]
    public async Task FindReferences_NonexistentSymbol_ReturnsSymbolNotFound()
    {
        var json = await FindReferencesTool.ExecuteAsync(fixture.WorkspaceManager, "ZZZNonExistentXXX", ct: TestContext.Current.CancellationToken);
        var error = JsonSerializer.Deserialize<ErrorResponse>(json)!;

        Assert.Equal(ErrorCodes.SymbolNotFound, error.Error);
    }

    [Fact]
    public async Task FindReferences_UnreferencedSymbol_ReturnsEmptyResultNotAnError()
    {
        // The distinction that matters: a real symbol with no references is a valid answer
        // ("this is dead"), while a misspelled one is not an answer at all.
        var json = await FindReferencesTool.ExecuteAsync(
            fixture.WorkspaceManager, "UnusedHelper", ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<ReferencesResult>(json)!;

        Assert.Equal(0, result.TotalFound);
        Assert.Empty(result.References!);
    }

    [Fact]
    public async Task FindReferences_ReportsTotalFound_ConsistentWithReturnedCount()
    {
        var json = await FindReferencesTool.ExecuteAsync(fixture.WorkspaceManager, "IOrderRepository", ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<ReferencesResult>(json)!;

        Assert.True(result.TotalFound > 0, "Expected a positive total for a referenced symbol");
        Assert.True(result.TotalFound >= result.Count, "TotalFound must never be less than the returned Count");
    }

    [Fact]
    public async Task FindReferences_RespectsMaxResults_AndSurfacesFullTotal()
    {
        var json = await FindReferencesTool.ExecuteAsync(fixture.WorkspaceManager, "Order", maxResults: 1, ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<ReferencesResult>(json)!;

        Assert.True(result.Count <= 1, "Count must respect maxResults");
        Assert.True(result.TotalFound >= result.Count, "TotalFound must reflect the uncapped total");
    }

    [Fact]
    public async Task FindReferences_AnySymbol_ReturnsSolutionRelativePaths()
    {
        var json = await FindReferencesTool.ExecuteAsync(fixture.WorkspaceManager, "Order", ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<ReferencesResult>(json)!;

        Assert.NotEmpty(result.References!);
        Assert.All(result.References!, r =>
        {
            Assert.DoesNotContain(":", r.File);       // no Windows drive prefix (e.g. C:\)
            Assert.False(r.File.StartsWith('/'));      // no Unix absolute root
            Assert.DoesNotContain('\\', r.File);       // forward-slashed
        });
    }
}
