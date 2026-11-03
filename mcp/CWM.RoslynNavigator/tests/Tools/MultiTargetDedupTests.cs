using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;

namespace CWM.RoslynNavigator.Tests.Tools;

/// <summary>
/// A project multi-targeting net10.0 and net8.0 loads as two Roslyn projects, so every search
/// finds each source location twice. Each tool must report it once, count it once in
/// TotalFound, and name the frameworks only for code that exists in some of them.
/// </summary>
public sealed class MultiTargetDedupTests(MultiTargetSolutionFixture fixture) : IClassFixture<MultiTargetSolutionFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WorkspaceManager Workspace => fixture.WorkspaceManager;

    [Fact]
    public void Load_MultiTargetedProject_LoadsOneProjectPerFramework()
    {
        // Assert — the premise every other test here relies on.
        Assert.Equal(2, Workspace.ProjectCount);
    }

    [Fact]
    public async Task FindSymbol_TypeInEveryFramework_ReturnsOneEntryWithoutFrameworks()
    {
        // Act
        var json = await FindSymbolTool.ExecuteAsync(Workspace, "Greeter", "class", ct: Ct);

        // Assert
        var result = JsonSerializer.Deserialize<SymbolSearchResult>(json)!;
        var symbol = Assert.Single(result.Symbols);
        Assert.Equal(1, result.TotalFound);
        Assert.Null(symbol.TargetFrameworks);
    }

    [Fact]
    public async Task FindSymbol_TypeUnderIfDirective_ReportsOnlyItsFramework()
    {
        // Act
        var json = await FindSymbolTool.ExecuteAsync(Workspace, "LegacyShim", ct: Ct);

        // Assert
        var symbol = Assert.Single(JsonSerializer.Deserialize<SymbolSearchResult>(json)!.Symbols);
        Assert.Equal(["net8.0"], symbol.TargetFrameworks);
    }

    [Fact]
    public async Task FindReferences_MultiTargeted_CountsEachPositionOnce()
    {
        // Act
        var json = await FindReferencesTool.ExecuteAsync(Workspace, "Lib.IGreeter.Greet", ct: Ct);

        // Assert — Welcome, Farewell, LegacyShim.Run (net8.0 only), and base.Greet in LoudGreeter.
        var result = JsonSerializer.Deserialize<ReferencesResult>(json)!;
        Assert.Equal(result.References.Count, result.References.DistinctBy(r => (r.File, r.Line, r.Snippet)).Count());
        Assert.Equal(result.References.Count, result.TotalFound);
        Assert.False(result.Truncated);
        Assert.Equal(["net8.0"], Assert.Single(result.References, r => r.Snippet.Contains("legacy")).TargetFrameworks);
        Assert.All(result.References.Where(r => !r.Snippet.Contains("legacy")), r => Assert.Null(r.TargetFrameworks));
    }

    [Fact]
    public async Task FindReferences_CapBelowDistinctTotal_TruncatesOnDistinctCount()
    {
        // Act
        var all = JsonSerializer.Deserialize<ReferencesResult>(
            await FindReferencesTool.ExecuteAsync(Workspace, "Lib.IGreeter.Greet", ct: Ct))!;
        var capped = JsonSerializer.Deserialize<ReferencesResult>(
            await FindReferencesTool.ExecuteAsync(Workspace, "Lib.IGreeter.Greet", maxResults: 1, ct: Ct))!;

        // Assert
        ToolGapTests.AssertTruncated(capped.References.Count, capped.Count, capped.TotalFound, capped.Truncated, capped.Limit, total: all.TotalFound);
    }

    [Fact]
    public async Task FindImplementations_MultiTargeted_ReturnsEachTypeOnce()
    {
        // Act
        var json = await FindImplementationsTool.ExecuteAsync(Workspace, "IGreeter", ct: Ct);

        // Assert
        var result = JsonSerializer.Deserialize<ImplementationsResult>(json)!;
        Assert.Equal(["Greeter", "LoudGreeter"], result.Implementations.Select(i => i.Type).Order());
        Assert.Equal(2, result.TotalFound);
    }

    [Fact]
    public async Task FindOverrides_MultiTargeted_ReturnsEachOverrideOnce()
    {
        // Act
        var json = await FindOverridesTool.ExecuteAsync(Workspace, "Greet", "Greeter", ct: Ct);

        // Assert
        var result = JsonSerializer.Deserialize<OverridesResult>(json)!;
        Assert.Equal("LoudGreeter", Assert.Single(result.Overrides).ContainingType);
    }

    [Fact]
    public async Task GetTypeHierarchy_MultiTargeted_ListsEachDerivedTypeOnce()
    {
        // Act
        var json = await GetTypeHierarchyTool.ExecuteAsync(Workspace, "Greeter", ct: Ct);

        // Assert
        var result = JsonSerializer.Deserialize<TypeHierarchyResult>(json)!;
        Assert.Equal("LoudGreeter", Assert.Single(result.DerivedTypes));
        Assert.Equal(1, result.TotalDerived);
    }

    [Fact]
    public async Task FindCallers_MultiTargeted_ReturnsEachCallerOnce()
    {
        // Act
        var json = await FindCallersTool.ExecuteAsync(Workspace, "Greet", "IGreeter", ct: Ct);

        // Assert
        var result = JsonSerializer.Deserialize<CallersResult>(json)!;
        Assert.Equal(result.Callers.Count, result.Callers.DistinctBy(c => (c.File, c.Line, c.Method)).Count());
        Assert.Equal(["Farewell", "Run", "Welcome"], result.Callers.Where(c => c.Via == "direct").Select(c => c.Method).Order());
    }

    [Fact]
    public async Task AnalyzeChangeImpact_MultiTargeted_CountsOneProjectAndDistinctReferences()
    {
        // Act
        var json = await AnalyzeChangeImpactTool.ExecuteAsync(Workspace, "Lib.IGreeter.Greet", ct: Ct);

        // Assert
        var result = JsonSerializer.Deserialize<ChangeImpactResult>(json)!;
        Assert.Equal("Lib", Assert.Single(result.Projects).Name);
        Assert.Equal(4, result.DirectReferences);
        Assert.Equal(1, result.ImplementationsToUpdate);
        Assert.Equal(result.TransitiveCallers.Count, result.TransitiveCallers.Distinct().Count());
    }

    [Fact]
    public async Task DetectAntiPatterns_MultiTargeted_ReportsFindingAndFileOnce()
    {
        // Act
        var json = await DetectAntiPatternsTool.ExecuteAsync(Workspace, file: "Greeting.cs", ct: Ct);

        // Assert
        var result = JsonSerializer.Deserialize<AntiPatternsResult>(json)!;
        Assert.Single(result.Violations, v => v.Id == "AP004");
        Assert.Equal(1, result.Summary!.ScannedFiles);
    }
}
