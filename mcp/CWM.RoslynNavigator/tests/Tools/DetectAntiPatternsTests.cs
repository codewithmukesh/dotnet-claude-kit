using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;

namespace CWM.RoslynNavigator.Tests.Tools;

[Collection(SampleSolutionCollection.Name)]
public class DetectAntiPatternsTests(TestSolutionFixture fixture)
{
    private async Task<AntiPatternsResult> RunAsync(
        string? file = null,
        string? projectFilter = null,
        string severity = "warning",
        int maxResults = 100)
    {
        var json = await DetectAntiPatternsTool.ExecuteAsync(
            fixture.WorkspaceManager,
            file: file,
            projectFilter: projectFilter,
            severity: severity,
            maxResults: maxResults);
        return JsonSerializer.Deserialize<AntiPatternsResult>(json)!;
    }

    [Fact]
    public async Task DetectAntiPatterns_AsyncVoidMethod_ReportsAP001()
    {
        var result = await RunAsync(file: "AntiPatternExamples.cs");

        var violations = result.Violations.Where(v => v.Id == "AP001").ToList();
        Assert.Single(violations);
        Assert.Contains("FireAndForget", violations[0].Message);
        Assert.Equal("error", violations[0].Severity);
    }

    [Fact]
    public async Task DetectAntiPatterns_AsyncVoidEventHandler_IsNotReported()
    {
        var result = await RunAsync(file: "AntiPatternExamples.cs");

        var violations = result.Violations.Where(v => v.Id == "AP001").ToList();
        Assert.DoesNotContain(violations, v => v.Message.Contains("OnButtonClick"));
    }

    [Fact]
    public async Task DetectAntiPatterns_SyncOverAsync_ReportsAP002()
    {
        var result = await RunAsync(file: "AntiPatternExamples.cs");

        var violations = result.Violations.Where(v => v.Id == "AP002").ToList();
        Assert.True(violations.Count >= 2, $"Expected at least 2 AP002 violations, got {violations.Count}");
        Assert.Equal("error", violations[0].Severity);
    }

    [Fact]
    public async Task DetectAntiPatterns_NewHttpClient_ReportsAP003()
    {
        var result = await RunAsync(file: "AntiPatternExamples.cs");

        var violations = result.Violations.Where(v => v.Id == "AP003").ToList();
        Assert.Single(violations);
        Assert.Equal("warning", violations[0].Severity);
    }

    [Fact]
    public async Task DetectAntiPatterns_DateTimeNow_ReportsAP004()
    {
        var result = await RunAsync(file: "AntiPatternExamples.cs");

        var violations = result.Violations.Where(v => v.Id == "AP004").ToList();
        Assert.True(violations.Count >= 2, $"Expected at least 2 AP004 violations, got {violations.Count}");
    }

    [Fact]
    public async Task DetectAntiPatterns_BroadCatch_ReportsAP005()
    {
        var result = await RunAsync(file: "AntiPatternExamples.cs");

        var violations = result.Violations.Where(v => v.Id == "AP005").ToList();
        Assert.Single(violations);
        Assert.Equal("warning", violations[0].Severity);
    }

    [Fact]
    public async Task DetectAntiPatterns_LoggingInterpolation_ReportsAP006()
    {
        var result = await RunAsync(file: "AntiPatternExamples.cs");

        var violations = result.Violations.Where(v => v.Id == "AP006").ToList();
        Assert.True(violations.Count >= 1, $"Expected at least 1 AP006 violation, got {violations.Count}");
    }

    [Fact]
    public async Task DetectAntiPatterns_EmptyCatch_ReportsAP007()
    {
        var result = await RunAsync(file: "AntiPatternExamples.cs");

        var violations = result.Violations.Where(v => v.Id == "AP007").ToList();
        Assert.Single(violations);
        Assert.Equal("error", violations[0].Severity);
    }

    [Fact]
    public async Task DetectAntiPatterns_PragmaWithoutRestore_ReportsAP008()
    {
        var result = await RunAsync(file: "AntiPatternExamples.cs");

        var violations = result.Violations.Where(v => v.Id == "AP008").ToList();
        Assert.Single(violations);
        Assert.Contains("CS0219", violations[0].Snippet);
    }

    [Fact]
    public async Task DetectAntiPatterns_MissingCancellationToken_ReportsAP009()
    {
        var result = await RunAsync(file: "AntiPatternExamples.cs");

        var violations = result.Violations.Where(v => v.Id == "AP009").ToList();
        Assert.Single(violations);
        Assert.Contains("DoWorkAsync", violations[0].Message);
    }

    [Fact]
    public async Task DetectAntiPatterns_SeverityError_OnlyReturnsErrors()
    {
        var result = await RunAsync(file: "AntiPatternExamples.cs", severity: "error");

        Assert.All(result.Violations, v => Assert.Equal("error", v.Severity));
        Assert.True(result.Count > 0);
    }

    [Fact]
    public async Task DetectAntiPatterns_ProjectFilter_LimitsToProject()
    {
        var result = await RunAsync(projectFilter: "SampleDomain");

        // SampleDomain has no intentional anti-patterns
        // Should return 0 or very few violations
        Assert.All(result.Violations, v =>
            Assert.DoesNotContain("AntiPatternExamples", v.File));
    }

    [Fact]
    public async Task DetectAntiPatterns_MaxResults_LimitsOutput()
    {
        var result = await RunAsync(file: "AntiPatternExamples.cs", maxResults: 3);

        Assert.True(result.Count <= 3);
        Assert.True(result.TotalFound >= result.Count);
    }

    [Fact]
    public async Task DetectAntiPatterns_FileFilter_LimitsToFile()
    {
        var result = await RunAsync(file: "AntiPatternExamples.cs");

        Assert.All(result.Violations, v =>
            Assert.Contains("AntiPatternExamples", v.File));
    }
}
