using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;

namespace CWM.RoslynNavigator.Tests.Tools;

/// <summary>
/// Not-found and truncation paths that had no test. Truncation is the contract that tells an
/// agent "there is more — re-query": a cap applied without Truncated/TotalFound reads as a
/// complete answer, and a missing cap floods the context window.
/// </summary>
[Collection(SampleSolutionCollection.Name)]
public sealed class ToolGapTests(TestSolutionFixture fixture)
{
    private const string ThreeFrameTrace = """
        System.InvalidOperationException: Order 42 not found
           at SampleApi.OrderService.CancelOrderAsync(Guid id, CancellationToken ct)
           at SampleApi.OrderService.GetOrderAsync(Guid id, CancellationToken ct)
           at SampleDomain.Order.Cancel()
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WorkspaceManager Workspace => fixture.WorkspaceManager;

    [Fact]
    public async Task FindImplementations_UnknownInterface_ReturnsSymbolNotFound()
    {
        // Act
        var json = await FindImplementationsTool.ExecuteAsync(Workspace, "IDoesNotExist12345", ct: Ct);

        // Assert
        Assert.Equal(ErrorCodes.SymbolNotFound, JsonSerializer.Deserialize<ErrorResponse>(json)!.Error);
    }

    [Fact]
    public async Task GetTypeHierarchy_UnknownType_ReturnsSymbolNotFound()
    {
        // Act
        var json = await GetTypeHierarchyTool.ExecuteAsync(Workspace, "DoesNotExist12345", ct: Ct);

        // Assert
        Assert.Equal(ErrorCodes.SymbolNotFound, JsonSerializer.Deserialize<ErrorResponse>(json)!.Error);
    }

    [Fact]
    public async Task GetTypeHierarchy_MethodName_ReturnsErrorNotHierarchy()
    {
        // Act — a member, not a type: there is no hierarchy to report.
        var json = await GetTypeHierarchyTool.ExecuteAsync(Workspace, "OrderService.CancelOrderAsync", ct: Ct);

        // Assert
        Assert.NotNull(JsonSerializer.Deserialize<ErrorResponse>(json)!.Error);
    }

    [Fact]
    public async Task FindCallers_MaxResultsBelowTotal_TruncatesAndReportsTotal()
    {
        // Act — GetByIdAsync has three callers in SampleSolution.
        var json = await FindCallersTool.ExecuteAsync(Workspace, "GetByIdAsync", "IOrderRepository", maxResults: 1, ct: Ct);

        // Assert
        var result = JsonSerializer.Deserialize<CallersResult>(json)!;
        AssertTruncated(result.Callers.Count, result.Count, result.TotalFound, result.Truncated, result.Limit, total: 3);
    }

    [Fact]
    public async Task FindImplementations_MaxResultsBelowTotal_TruncatesAndReportsTotal()
    {
        // Act
        var json = await FindImplementationsTool.ExecuteAsync(Workspace, "IOrderRepository", maxResults: 1, ct: Ct);

        // Assert
        var result = JsonSerializer.Deserialize<ImplementationsResult>(json)!;
        AssertTruncated(result.Implementations.Count, result.Count, result.TotalFound, result.Truncated, result.Limit, total: 2);
    }

    [Fact]
    public async Task FindImplementations_MaxResultsEqualsTotal_IsNotTruncated()
    {
        // Act
        var json = await FindImplementationsTool.ExecuteAsync(Workspace, "IOrderRepository", maxResults: 2, ct: Ct);

        // Assert
        var result = JsonSerializer.Deserialize<ImplementationsResult>(json)!;
        Assert.False(result.Truncated);
        Assert.Equal(2, result.Count);
        Assert.Equal(2, result.TotalFound);
    }

    [Fact]
    public async Task GetTypeHierarchy_MaxResultsBelowDerivedCount_TruncatesDerivedTypes()
    {
        // Act — IOrderRepository has two implementing types.
        var json = await GetTypeHierarchyTool.ExecuteAsync(Workspace, "IOrderRepository", maxResults: 1, ct: Ct);

        // Assert
        var result = JsonSerializer.Deserialize<TypeHierarchyResult>(json)!;
        Assert.Single(result.DerivedTypes);
        Assert.Equal(2, result.TotalDerived);
        Assert.True(result.Truncated);
        Assert.Equal(1, result.Limit);
    }

    [Fact]
    public async Task GetDependencyGraph_MaxResultsBelowNodeCount_IsTruncated()
    {
        // Arrange — CancelOrderAsync calls GetByIdAsync, Cancel and UpdateAsync.
        var full = JsonSerializer.Deserialize<DependencyGraphResult>(
            await GetDependencyGraphTool.ExecuteAsync(Workspace, "OrderService.CancelOrderAsync", depth: 1, ct: Ct))!;

        // Act
        var json = await GetDependencyGraphTool.ExecuteAsync(Workspace, "OrderService.CancelOrderAsync", depth: 1, maxResults: 1, ct: Ct);

        // Assert
        var result = JsonSerializer.Deserialize<DependencyGraphResult>(json)!;
        Assert.False(full.Truncated);
        Assert.Equal(3, full.Dependencies.Count);
        Assert.True(result.Truncated);
        Assert.Single(result.Dependencies);
    }

    [Fact]
    public async Task GetDiagnostics_MaxResultsBelowTotal_TruncatesButKeepsSeverityTotals()
    {
        // Arrange — SampleApi's unresolved logging package yields several errors.
        var full = JsonSerializer.Deserialize<DiagnosticsResult>(
            await GetDiagnosticsTool.ExecuteAsync(Workspace, "project", "SampleApi", maxResults: 1000, ct: Ct))!;

        // Act
        var json = await GetDiagnosticsTool.ExecuteAsync(Workspace, "project", "SampleApi", maxResults: 1, ct: Ct);

        // Assert
        var result = JsonSerializer.Deserialize<DiagnosticsResult>(json)!;
        Assert.True(full.TotalFound > 1);
        AssertTruncated(result.Diagnostics.Count, result.Count, result.TotalFound, result.Truncated, result.Limit, full.TotalFound);
        Assert.Equal(full.Errors, result.Errors);
        Assert.Equal(full.Warnings, result.Warnings);
    }

    [Fact]
    public async Task ResolveStackTrace_MaxResultsBelowFrameCount_TruncatesFrames()
    {
        // Act
        var json = await ResolveStackTraceTool.ExecuteAsync(Workspace, ThreeFrameTrace, maxResults: 2, ct: Ct);

        // Assert
        var result = JsonSerializer.Deserialize<StackTraceResult>(json)!;
        AssertTruncated(result.Frames.Count, result.Count, result.TotalFound, result.Truncated, result.Limit, total: 3, cap: 2);
        Assert.Equal([0, 1], result.Frames.Select(f => f.Index));
    }

    /// <summary>The shared paging contract: exactly <paramref name="cap"/> listed, the true total reported.</summary>
    internal static void AssertTruncated(
        int listed, int count, int totalFound, bool truncated, int appliedLimit, int total, int cap = 1)
    {
        Assert.Equal(cap, listed);
        Assert.Equal(cap, count);
        Assert.Equal(total, totalFound);
        Assert.True(truncated);
        Assert.Equal(cap, appliedLimit);
    }
}
