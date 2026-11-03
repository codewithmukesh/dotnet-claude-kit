using System.Text.Json;
using CWM.RoslynNavigator.Analyzers;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;

namespace CWM.RoslynNavigator.Tests.Tools;

/// <summary>
/// SampleTests is the sample solution's test project. Before it existed every tool's
/// "is this a test?" branch only ever saw production code, so the test-project paths
/// (classification, test-reference counts, test-code exclusion) ran on no input at all.
/// </summary>
[Collection(SampleSolutionCollection.Name)]
public sealed class SampleTestProjectTests(TestSolutionFixture fixture)
{
    [Fact]
    public void IsTestProject_SampleTests_IsClassifiedAsTestProject()
    {
        // Arrange
        var projects = fixture.WorkspaceManager.GetSolution()!.Projects.ToList();

        // Act
        var testProjects = projects.Where(SourceClassifier.IsTestProject).Select(p => p.Name);

        // Assert
        Assert.Equal(["SampleTests"], testProjects);
    }

    [Fact]
    public async Task AnalyzeChangeImpact_SymbolUsedByTests_CountsTestReferencesAndTestProject()
    {
        // Act — OrderTests calls Order.Cancel; production code (OrderService) calls it too.
        var json = await AnalyzeChangeImpactTool.ExecuteAsync(
            fixture.WorkspaceManager, "Order.Cancel", ct: TestContext.Current.CancellationToken);

        // Assert
        var result = JsonSerializer.Deserialize<ChangeImpactResult>(json)!;
        Assert.Equal(1, result.TestReferences);
        Assert.Contains(result.Projects, p => p is { Name: "SampleTests", IsTest: true });
        Assert.Contains(result.Projects, p => p is { Name: "SampleApi", IsTest: false });
        Assert.Contains(result.Files, f => f is { File: "SampleTests/OrderTests.cs", IsTest: true });
    }

    [Fact]
    public async Task DetectAntiPatterns_ProductionScope_IgnoresTestProject()
    {
        // Act
        var json = await DetectAntiPatternsTool.ExecuteAsync(
            fixture.WorkspaceManager, projectFilter: "SampleTests", ct: TestContext.Current.CancellationToken);

        // Assert
        var result = JsonSerializer.Deserialize<AntiPatternsResult>(json)!;
        Assert.Empty(result.Violations);
    }

    [Fact]
    public async Task FindReferences_OrderCreate_IncludesCallsFromTestProject()
    {
        // Act
        var json = await FindReferencesTool.ExecuteAsync(
            fixture.WorkspaceManager, "Order.Create", ct: TestContext.Current.CancellationToken);

        // Assert
        var result = JsonSerializer.Deserialize<ReferencesResult>(json)!;
        Assert.Equal(3, result.References.Count(r => r.File == "SampleTests/OrderTests.cs"));
    }
}
