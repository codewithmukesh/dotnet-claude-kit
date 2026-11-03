using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;

namespace CWM.RoslynNavigator.Tests.Tools;

[Collection(SampleSolutionCollection.Name)]
public class GetWorkspaceStatusTests(TestSolutionFixture fixture)
{
    [Fact]
    public async Task GetWorkspaceStatus_LoadedSampleSolution_ReportsSolutionAndProjects()
    {
        await fixture.WorkspaceManager.WaitForWarmUpAsync();

        var result = JsonSerializer.Deserialize<WorkspaceStatusResult>(
            await GetWorkspaceStatusTool.ExecuteAsync(fixture.WorkspaceManager, ct: TestContext.Current.CancellationToken))!;

        Assert.Equal("Ready", result.State);
        Assert.Equal("SampleSolution.sln", Assert.Single(result.Solutions).Name);
        Assert.Equal(fixture.WorkspaceManager.ProjectCount, result.ProjectCount);
        Assert.True(result.DocumentCount > 0);
        Assert.True(result.SnapshotVersion > 0);
        Assert.Equal("completed", result.WarmUp.State);
        Assert.Equal(result.WarmUp.Total, result.WarmUp.Compiled);
    }

    [Fact]
    public async Task GetWorkspaceStatus_CompleteLoad_IsHealthyWithNoIncompleteProjects()
    {
        await fixture.WorkspaceManager.WaitForWarmUpAsync();

        var result = JsonSerializer.Deserialize<WorkspaceStatusResult>(
            await GetWorkspaceStatusTool.ExecuteAsync(fixture.WorkspaceManager, ct: TestContext.Current.CancellationToken))!;

        Assert.True(result.Healthy, string.Join("; ", result.IncompleteProjects.SelectMany(p => p.Issues)));
        Assert.Empty(result.IncompleteProjects);
        Assert.Null(IncompleteWorkspaceNote.BuildWarning(fixture.WorkspaceManager));
    }
}

public class GetWorkspaceStatusBrokenSolutionTests(BrokenSolutionFixture fixture) : IClassFixture<BrokenSolutionFixture>
{
    [Fact]
    public async Task GetWorkspaceStatus_PartialLoad_IsUnhealthyAndNamesTheBrokenProjects()
    {
        var result = JsonSerializer.Deserialize<WorkspaceStatusResult>(
            await GetWorkspaceStatusTool.ExecuteAsync(fixture.WorkspaceManager, ct: TestContext.Current.CancellationToken))!;

        Assert.False(result.Healthy);
        Assert.NotEmpty(result.LoadDiagnostics);

        var empty = Assert.Single(result.IncompleteProjects, p => p.Name == "Empty");
        Assert.Equal(0, empty.Documents);
        Assert.Contains(empty.Issues, i => i.Contains("no source documents"));

        var ghost = Assert.Single(result.IncompleteProjects, p => p.Name == "Ghost");
        Assert.Contains(ghost.Issues, i => i.Contains("not loaded"));

        var good = Assert.Single(result.IncompleteProjects, p => p.Name == "Good");
        Assert.Contains(good.Issues, i => i.Contains("Missing.csproj"));
        Assert.Contains(good.Issues, i => i.Contains("NotThere.Generators.dll"));
    }

    [Fact]
    public async Task EmptyResult_PartialLoad_CarriesWorkspaceWarning()
    {
        var json = await FindSymbolTool.ExecuteAsync(
            fixture.WorkspaceManager, "NoSuchTypeAnywhere", ct: TestContext.Current.CancellationToken);

        var warning = IncompleteWorkspaceNote.BuildWarning(fixture.WorkspaceManager);
        Assert.NotNull(warning);
        Assert.Contains("Empty", warning);
        Assert.StartsWith("3 project(s)", warning);

        using var annotated = JsonDocument.Parse(IncompleteWorkspaceNote.Annotate(json, warning));
        Assert.Equal(warning, annotated.RootElement.GetProperty(IncompleteWorkspaceNote.PropertyName).GetString());
    }

    [Fact]
    public async Task NonEmptyResult_PartialLoad_IsLeftUnchanged()
    {
        var json = await FindSymbolTool.ExecuteAsync(
            fixture.WorkspaceManager, "Widget", ct: TestContext.Current.CancellationToken);

        Assert.Same(json, IncompleteWorkspaceNote.Annotate(json, "warning"));
    }
}

public class IncompleteWorkspaceNoteTests
{
    [Theory]
    [InlineData("""{"Symbols":[],"Count":0,"TotalFound":0,"Truncated":false,"Limit":50}""", true)]
    [InlineData("""{"Error":"SymbolNotFound","Message":"x"}""", true)]
    [InlineData("""{"Error":"FileNotFound","Message":"x"}""", true)]
    [InlineData("""{"Error":"AmbiguousMatch","Message":"x"}""", false)]
    [InlineData("""{"State":"Loading","Message":"x"}""", false)]
    [InlineData("""{"Symbols":[{}],"Count":1,"TotalFound":1}""", false)]
    [InlineData("""{"Count":0,"TotalFound":3}""", false)]
    [InlineData("""{"Solution":"a.sln","Projects":[]}""", false)]
    [InlineData("not json", false)]
    public void Annotate_AddsWarningOnlyToEmptyResults(string json, bool expectAnnotated)
    {
        var result = IncompleteWorkspaceNote.Annotate(json, "partial");

        Assert.Equal(expectAnnotated, result.Contains(IncompleteWorkspaceNote.PropertyName));
        if (!expectAnnotated) Assert.Same(json, result);
    }
}
