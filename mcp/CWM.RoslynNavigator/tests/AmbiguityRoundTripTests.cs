using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;

namespace CWM.RoslynNavigator.Tests;

/// <summary>
/// An AmbiguousMatch error tells the caller to "pass file/line" and hands it each
/// candidate's File and Line. Feeding those back verbatim must resolve — on Windows too,
/// where the candidate path uses '/' and the syntax tree path uses '\'.
/// </summary>
[Collection(SampleSolutionCollection.Name)]
public class AmbiguityRoundTripTests(TestSolutionFixture fixture)
{
    public static TheoryData<string> Tools =>
        ["find_references", "get_symbol_source", "get_dependency_graph", "analyze_change_impact"];

    [Theory]
    [MemberData(nameof(Tools))]
    public async Task AmbiguousCandidate_FedBackAsFileAndLine_Resolves(string tool)
    {
        var ct = TestContext.Current.CancellationToken;
        var ambiguous = JsonSerializer.Deserialize<ErrorResponse>(
            await FindReferencesTool.ExecuteAsync(fixture.WorkspaceManager, "GetByIdAsync", ct: ct))!;

        Assert.Equal(ErrorCodes.AmbiguousMatch, ambiguous.Error);
        Assert.NotNull(ambiguous.Candidates);
        Assert.True(ambiguous.Candidates.Count > 1);

        foreach (var candidate in ambiguous.Candidates)
        {
            Assert.Contains('/', candidate.File);

            var json = await InvokeAsync(tool, "GetByIdAsync", candidate.File, candidate.Line, ct);

            using var document = JsonDocument.Parse(json);
            Assert.False(document.RootElement.TryGetProperty("Error", out var error),
                $"{tool} with {candidate.File}:{candidate.Line} returned {error}");
        }
    }

    [Fact]
    public async Task FileHint_PartialFileName_MatchesOnlyOnDirectoryBoundary()
    {
        // "OrderRepository.cs" is a suffix of IOrderRepository.cs, InMemoryOrderRepository.cs
        // and CachedOrderRepository.cs, but names none of them. A hint that matches nothing
        // is ignored rather than narrowing to a wrong subset.
        var ct = TestContext.Current.CancellationToken;
        var unhinted = JsonSerializer.Deserialize<ErrorResponse>(
            await FindReferencesTool.ExecuteAsync(fixture.WorkspaceManager, "GetByIdAsync", ct: ct))!;
        var hinted = JsonSerializer.Deserialize<ErrorResponse>(
            await FindReferencesTool.ExecuteAsync(
                fixture.WorkspaceManager, "GetByIdAsync", file: "OrderRepository.cs", ct: ct))!;

        Assert.Equal(ErrorCodes.AmbiguousMatch, hinted.Error);
        Assert.Equal(unhinted.Candidates!.Count, hinted.Candidates!.Count);
    }

    [Theory]
    [InlineData(@"C:\repo\SampleApi\OrderService.cs", "SampleApi/OrderService.cs", true)]
    [InlineData(@"C:\repo\SampleApi\OrderService.cs", @".\SampleApi\OrderService.cs", true)]
    [InlineData(@"C:\repo\SampleApi\OrderService.cs", "OrderService.cs", true)]
    [InlineData(@"C:\repo\SampleApi\OrderService.cs", "Service.cs", false)]
    [InlineData("/repo/SampleApi/OrderService.cs", "pi/OrderService.cs", false)]
    public void PathMatches_RequestedSuffix_RequiresDirectoryBoundary(string filePath, string requested, bool expected) =>
        Assert.Equal(expected, DocumentResolver.PathMatches(filePath, requested));

    private Task<string> InvokeAsync(string tool, string name, string file, int line, CancellationToken ct) => tool switch
    {
        "find_references" => FindReferencesTool.ExecuteAsync(fixture.WorkspaceManager, name, file, line, ct: ct),
        "get_symbol_source" => GetSymbolSourceTool.ExecuteAsync(fixture.WorkspaceManager, name, file: file, line: line, ct: ct),
        "get_dependency_graph" => GetDependencyGraphTool.ExecuteAsync(fixture.WorkspaceManager, name, file, line, ct: ct),
        "analyze_change_impact" => AnalyzeChangeImpactTool.ExecuteAsync(fixture.WorkspaceManager, name, file, line, ct: ct),
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, null)
    };
}
