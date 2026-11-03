using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;

namespace CWM.RoslynNavigator.Tests.Tools;

/// <summary>
/// Regression suite for detector false positives. Every case here is correct code that a
/// naive detector reports. These tests are the contract that keeps health-check output
/// trustworthy enough to grade directly.
/// </summary>
[Collection(SampleSolutionCollection.Name)]
public class DetectAntiPatternsFalsePositiveTests(TestSolutionFixture fixture)
{
    private async Task<AntiPatternsResult> RunAsync(
        string? file = null,
        string scope = "production",
        string confidence = "medium",
        int maxResults = 500)
    {
        var json = await DetectAntiPatternsTool.ExecuteAsync(
            fixture.WorkspaceManager,
            file: file,
            maxResults: maxResults,
            scope: scope,
            confidence: confidence,
            ct: TestContext.Current.CancellationToken);
        return JsonSerializer.Deserialize<AntiPatternsResult>(json)!;
    }

    // ---- Source classification -------------------------------------------------------

    [Fact]
    public async Task DetectAntiPatterns_TestClassifiedFile_ProducesNoProductionFindings()
    {
        var result = await RunAsync(file: "CustomerWorkflowTests.cs");

        Assert.Empty(result.Violations);
    }

    [Fact]
    public async Task DetectAntiPatterns_MigrationFile_ProducesNoProductionFindings()
    {
        var result = await RunAsync(file: "20240101000000_InitialCreate.cs");

        Assert.Empty(result.Violations);
    }

    [Fact]
    public async Task DetectAntiPatterns_ScopeAll_NeverReportsGeneratedCode()
    {
        var result = await RunAsync(scope: "all");

        Assert.DoesNotContain(result.Violations, v =>
            v.File.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || v.File.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DetectAntiPatterns_ScopeAllOnTestFile_SurfacesFindingsThatApplyToTests()
    {
        var production = await RunAsync(file: "CustomerWorkflowTests.cs");
        var all = await RunAsync(file: "CustomerWorkflowTests.cs", scope: "all");

        Assert.Empty(production.Violations);

        // async void is a defect anywhere, so it is one of the few detectors scoped to tests.
        var violation = Assert.Single(all.Violations);
        Assert.Equal("AP001", violation.Id);
        Assert.Equal("test", violation.SourceKind);
    }

    [Fact]
    public async Task DetectAntiPatterns_ScopeAllOnTestFile_ExcludesDetectorsMeaninglessInTests()
    {
        var all = await RunAsync(file: "CustomerWorkflowTests.cs", scope: "all");

        // new HttpClient(), .Result, DateTime.UtcNow and a missing token are all correct
        // in a test harness — widening the scope must not resurrect them.
        Assert.DoesNotContain(all.Violations, v => v.Id is "AP002" or "AP003" or "AP004" or "AP009");
    }

    // ---- AP006: logging templates ----------------------------------------------------

    [Fact]
    public async Task AdjacentStringLiterals_AreNotLoggingConcatenation_AP006()
    {
        var result = await RunAsync(file: "FalsePositiveExamples.cs");

        Assert.DoesNotContain(result.Violations, v =>
            v.Id == "AP006" && v.Member!.Contains("LogWithWrappedTemplate"));
    }

    [Fact]
    public async Task ConstPrefixTemplate_IsNotLoggingConcatenation_AP006()
    {
        var result = await RunAsync(file: "FalsePositiveExamples.cs");

        Assert.DoesNotContain(result.Violations, v =>
            v.Id == "AP006" && v.Member!.Contains("LogWithConstPrefix"));
    }

    [Fact]
    public async Task ConcatenationInValueArgument_IsNotFlagged_AP006()
    {
        var result = await RunAsync(file: "FalsePositiveExamples.cs");

        Assert.DoesNotContain(result.Violations, v =>
            v.Id == "AP006" && v.Member!.Contains("LogWithConcatenatedValue"));
    }

    // ---- AP005 / AP007: catch blocks -------------------------------------------------

    [Fact]
    public async Task EmptyCatchWithComment_IsCleared_AP007()
    {
        var result = await RunAsync(file: "FalsePositiveExamples.cs");

        Assert.DoesNotContain(result.Violations, v => v.Id == "AP007");
    }

    [Fact]
    public async Task CatchThatLogsAndRethrows_IsMediumConfidence_AP005()
    {
        var result = await RunAsync(file: "FalsePositiveExamples.cs");

        var violation = Assert.Single(result.Violations, v =>
            v.Id == "AP005" && v.Member!.Contains("BoundedResilienceWrapper"));
        Assert.Equal("medium", violation.Confidence);
    }

    [Fact]
    public async Task FilteredCatch_IsMediumConfidence_AP005()
    {
        var result = await RunAsync(file: "FalsePositiveExamples.cs");

        var violation = Assert.Single(result.Violations, v =>
            v.Id == "AP005" && v.Member!.Contains("FilteredCatch"));
        Assert.Equal("medium", violation.Confidence);
    }

    [Fact]
    public async Task DetectAntiPatterns_HighConfidence_ExcludesResilienceWrappers()
    {
        var result = await RunAsync(file: "FalsePositiveExamples.cs", confidence: "high");

        Assert.DoesNotContain(result.Violations, v => v.Id == "AP005");
        Assert.All(result.Violations, v => Assert.Equal("high", v.Confidence));
    }

    // ---- AP002: sync over async ------------------------------------------------------

    [Fact]
    public async Task DomainResultProperty_IsNotSyncOverAsync_AP002()
    {
        var result = await RunAsync(file: "FalsePositiveExamples.cs");

        Assert.DoesNotContain(result.Violations, v => v.Id == "AP002");
    }

    // ---- AP010: EF Core tracking -----------------------------------------------------

    [Fact]
    public async Task AggregateQueries_AreNotTrackingViolations_AP010()
    {
        var result = await RunAsync(file: "FalsePositiveExamples.cs");

        Assert.DoesNotContain(result.Violations, v =>
            v.Id == "AP010" && (v.Member!.Contains("CountCustomers") || v.Member.Contains("AnyCustomers")));
    }

    [Fact]
    public async Task LoadToMutateQuery_IsNotTrackingViolation_AP010()
    {
        var result = await RunAsync(file: "FalsePositiveExamples.cs");

        Assert.DoesNotContain(result.Violations, v =>
            v.Id == "AP010" && v.Member!.Contains("ResetTwoFactorAsync"));
    }

    [Fact]
    public async Task RawSqlQuery_IsNotTrackingViolation_AP010()
    {
        var result = await RunAsync(file: "FalsePositiveExamples.cs");

        Assert.DoesNotContain(result.Violations, v =>
            v.Id == "AP010" && v.Member!.Contains("RawSqlScalarQuery"));
    }

    [Fact]
    public async Task ChangeTrackerEntries_IsNotTrackingViolation_AP010()
    {
        var result = await RunAsync(file: "FalsePositiveExamples.cs");

        Assert.DoesNotContain(result.Violations, v =>
            v.Id == "AP010" && v.Member!.Contains("ReadChangeTrackerEntries"));
    }

    [Fact]
    public async Task EntityMutatedWithoutLocalSaveChanges_IsNotFlagged_AP010()
    {
        var result = await RunAsync(file: "FalsePositiveExamples.cs");

        Assert.DoesNotContain(result.Violations, v =>
            v.Id == "AP010" && v.Member!.Contains("RenameCustomerAsync"));
    }

    [Fact]
    public async Task DbSetMutationInScope_IsNotFlagged_AP010()
    {
        var result = await RunAsync(file: "FalsePositiveExamples.cs");

        Assert.DoesNotContain(result.Violations, v =>
            v.Id == "AP010" && v.Member!.Contains("ArchiveCustomerAsync"));
    }

    [Fact]
    public async Task CommandShapedMethod_IsMediumConfidence_AP010()
    {
        var result = await RunAsync(file: "FalsePositiveExamples.cs");

        var violation = Assert.Single(result.Violations, v =>
            v.Id == "AP010" && v.Member!.Contains("ProcessCustomerBatch"));
        Assert.Equal("medium", violation.Confidence);
    }

    [Fact]
    public async Task EmptyCatchOfCancellation_IsCleared_AP007()
    {
        var result = await RunAsync(file: "FalsePositiveExamples.cs");

        Assert.DoesNotContain(result.Violations, v =>
            v.Id == "AP007" && v.Member!.Contains("RunUntilStoppedAsync"));
    }

    [Fact]
    public async Task ReadShapedQueryWithoutTracking_IsStillFlagged_AP010()
    {
        var result = await RunAsync(file: "AntiPatternExamples.cs");

        var violation = Assert.Single(result.Violations, v => v.Id == "AP010");
        Assert.Contains("ListCustomersAsync", violation.Member);
        Assert.Equal("high", violation.Confidence);
    }

    // ---- AP009: CancellationToken ----------------------------------------------------

    [Fact]
    public async Task HttpContextParameter_SatisfiesCancellation_AP009()
    {
        var result = await RunAsync(file: "FalsePositiveExamples.cs");

        Assert.DoesNotContain(result.Violations, v =>
            v.Id == "AP009" && v.Member!.Contains("HandleRequestAsync"));
    }

    [Fact]
    public async Task MiddlewareInvokeAsync_IsNotFlagged_AP009()
    {
        var result = await RunAsync(file: "FalsePositiveExamples.cs");

        Assert.DoesNotContain(result.Violations, v =>
            v.Id == "AP009" && v.Member!.Contains("AuditMiddleware"));
    }

    [Fact]
    public async Task AmbientTokenSource_IsNotFlagged_AP009()
    {
        var result = await RunAsync(file: "FalsePositiveExamples.cs");

        Assert.DoesNotContain(result.Violations, v =>
            v.Id == "AP009" && v.Member!.Contains("RunStartupTasksAsync"));
    }

    [Fact]
    public async Task MissingCancellationToken_IsAdvisoryOnly_AP009()
    {
        var result = await RunAsync(file: "AntiPatternExamples.cs");

        var violation = Assert.Single(result.Violations, v => v.Id == "AP009");
        Assert.Equal("medium", violation.Confidence);
    }

    // ---- AP003: HttpClient -----------------------------------------------------------

    [Fact]
    public async Task HttpClientOverInjectedHandler_IsMediumConfidence_AP003()
    {
        var result = await RunAsync(file: "FalsePositiveExamples.cs");

        var violation = Assert.Single(result.Violations, v => v.Id == "AP003");
        Assert.Equal("medium", violation.Confidence);
    }

    // ---- Suppression ---------------------------------------------------------------------

    [Fact]
    public async Task DetectAntiPatterns_InlineIgnoreMarker_RemovesFindingButCountsIt()
    {
        var result = await RunAsync(file: "FalsePositiveExamples.cs");

        Assert.DoesNotContain(result.Violations, v =>
            v.Id == "AP004" && v.Member!.Contains("PresignedUrlExpiry"));

        var ap004 = Assert.Single(result.Summary!.ById, e => e.Id == "AP004");
        Assert.Equal(1, ap004.Suppressed);
        Assert.Equal(0, ap004.High);
    }

    // ---- Summary -----------------------------------------------------------------------

    [Fact]
    public async Task DetectAntiPatterns_Truncated_SummaryStaysComplete()
    {
        var full = await RunAsync();
        var truncated = await RunAsync(maxResults: 1);

        Assert.NotNull(full.Summary);
        Assert.NotNull(truncated.Summary);

        Assert.Single(truncated.Violations);
        Assert.Equal(full.Summary!.High, truncated.Summary!.High);
        Assert.Equal(full.Summary.Medium, truncated.Summary.Medium);
        Assert.Equal(
            full.Summary.ById.Sum(e => e.Total),
            truncated.Summary.ById.Sum(e => e.Total));
    }

    [Fact]
    public async Task DetectAntiPatterns_ScopeAll_SummaryCountsGeneratedFilesSeparately()
    {
        var result = await RunAsync(scope: "all");

        Assert.NotNull(result.Summary);
        Assert.True(result.Summary!.GeneratedFiles > 0, "Fixture solution has obj/ generated sources");
        Assert.True(result.Summary.TestFiles > 0, "Fixture has *Tests.cs files");
        Assert.True(result.Summary.MigrationFiles > 0, "Fixture has a Migrations/ folder");
    }

    [Fact]
    public async Task DetectAntiPatterns_AnyViolation_CarriesEnclosingMember()
    {
        var result = await RunAsync(file: "AntiPatternExamples.cs");

        Assert.All(
            result.Violations.Where(v => v.Id != "AP008"),
            v => Assert.False(string.IsNullOrWhiteSpace(v.Member)));
    }
}
