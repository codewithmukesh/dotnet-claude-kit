using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;

namespace CWM.RoslynNavigator.Tests.Tools;

public class AnalyzeChangeImpactTests(TestSolutionFixture fixture) : IClassFixture<TestSolutionFixture>
{
    [Fact]
    public async Task AnalyzeChangeImpact_Interface_CountsImplementationsThatMustChange()
    {
        var json = await AnalyzeChangeImpactTool.ExecuteAsync(
            fixture.WorkspaceManager, "IOrderRepository", ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<ChangeImpactResult>(json)!;

        // InMemoryOrderRepository and CachedOrderRepository both implement it.
        Assert.Equal(2, result.ImplementationsToUpdate);
        Assert.Equal("high", result.Risk);
        Assert.Contains("implementation", result.Rationale);
    }

    [Fact]
    public async Task AnalyzeChangeImpact_CrossProjectSymbol_ReportsEveryAffectedProject()
    {
        var json = await AnalyzeChangeImpactTool.ExecuteAsync(
            fixture.WorkspaceManager, "SampleDomain.Order", ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<ChangeImpactResult>(json)!;

        Assert.True(result.AffectedProjects > 1,
            "Order is referenced from SampleApi and SampleInfrastructure as well as SampleDomain");
        Assert.Equal(result.AffectedProjects, result.Projects.Count);
        Assert.All(result.Projects, p => Assert.True(p.References > 0));
    }

    [Fact]
    public async Task AnalyzeChangeImpact_ReferenceCountsAgreeAcrossGroupings()
    {
        var json = await AnalyzeChangeImpactTool.ExecuteAsync(
            fixture.WorkspaceManager, "SampleDomain.Order", maxResults: 500,
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<ChangeImpactResult>(json)!;

        Assert.Equal(result.DirectReferences, result.Projects.Sum(p => p.References));
        Assert.Equal(result.DirectReferences, result.Files.Sum(f => f.References));
    }

    [Fact]
    public async Task AnalyzeChangeImpact_Method_WalksTransitiveCallers()
    {
        // GetByIdAsync <- OrderService.GetOrderAsync <- (nothing further)
        var shallow = JsonSerializer.Deserialize<ChangeImpactResult>(
            await AnalyzeChangeImpactTool.ExecuteAsync(
                fixture.WorkspaceManager, "IOrderRepository.GetByIdAsync", depth: 1,
                ct: TestContext.Current.CancellationToken))!;

        Assert.NotEmpty(shallow.TransitiveCallers);
        Assert.Contains(shallow.TransitiveCallers, c => c.EndsWith("OrderService.GetOrderAsync"));
    }

    [Fact]
    public async Task AnalyzeChangeImpact_DeeperWalk_NeverReturnsFewerCallers()
    {
        var shallow = JsonSerializer.Deserialize<ChangeImpactResult>(
            await AnalyzeChangeImpactTool.ExecuteAsync(
                fixture.WorkspaceManager, "IOrderRepository.GetByIdAsync", depth: 1, maxResults: 500,
                ct: TestContext.Current.CancellationToken))!;

        var deep = JsonSerializer.Deserialize<ChangeImpactResult>(
            await AnalyzeChangeImpactTool.ExecuteAsync(
                fixture.WorkspaceManager, "IOrderRepository.GetByIdAsync", depth: 4, maxResults: 500,
                ct: TestContext.Current.CancellationToken))!;

        Assert.True(deep.TransitiveCallerCount >= shallow.TransitiveCallerCount);
    }

    [Fact]
    public async Task AnalyzeChangeImpact_IsolatedInternalSymbol_IsLowRisk()
    {
        var json = await AnalyzeChangeImpactTool.ExecuteAsync(
            fixture.WorkspaceManager, "TrulyUnusedCalculator", ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<ChangeImpactResult>(json)!;

        Assert.Equal("low", result.Risk);
        Assert.False(result.CrossesAssemblyBoundary);
        Assert.Equal(0, result.DirectReferences);
        Assert.Contains("reflection", result.Rationale);
    }

    [Fact]
    public async Task AnalyzeChangeImpact_PublicSymbol_FlagsAssemblyBoundary()
    {
        var json = await AnalyzeChangeImpactTool.ExecuteAsync(
            fixture.WorkspaceManager, "SampleApi.OrderService", ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<ChangeImpactResult>(json)!;

        Assert.True(result.CrossesAssemblyBoundary);
        Assert.Equal("public", result.Accessibility);
    }

    [Fact]
    public async Task AnalyzeChangeImpact_UnknownSymbol_ReturnsSymbolNotFound()
    {
        var json = await AnalyzeChangeImpactTool.ExecuteAsync(
            fixture.WorkspaceManager, "ZZZNoSuchSymbol", ct: TestContext.Current.CancellationToken);
        var error = JsonSerializer.Deserialize<ErrorResponse>(json)!;

        Assert.Equal(ErrorCodes.SymbolNotFound, error.Error);
    }

    [Fact]
    public async Task AnalyzeChangeImpact_AmbiguousName_ReportsCandidates()
    {
        var json = await AnalyzeChangeImpactTool.ExecuteAsync(
            fixture.WorkspaceManager, "GetByIdAsync", ct: TestContext.Current.CancellationToken);
        var error = JsonSerializer.Deserialize<ErrorResponse>(json)!;

        Assert.Equal(ErrorCodes.AmbiguousMatch, error.Error);
        Assert.NotNull(error.Candidates);
    }
}
