using System.Diagnostics;
using System.Text.Json;
using CWM.RoslynNavigator.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;

namespace CWM.RoslynNavigator.Tests.Tools;

/// <summary>
/// Contract every tool must honour whatever the workspace state: a not-ready workspace yields
/// well-formed JSON carrying the state, never an exception that the MCP SDK would turn into an
/// opaque transport error. Tools are discovered by reflection, so a new tool is covered the
/// moment it is added.
/// </summary>
public sealed class ToolContractTests
{
    /// <summary>Tools that do not depend on the workspace and answer in any state.</summary>
    private static readonly HashSet<string> WorkspaceIndependentTools = ["get_token_savings"];

    [Fact]
    public void Discover_ServerAssembly_FindsEveryToolWithUniqueNameAndStringResult()
    {
        // Act
        var tools = ToolCatalog.All;

        // Assert
        Assert.Equal(26, tools.Count);
        Assert.Equal(tools.Count, tools.Select(t => t.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.All(tools, t => Assert.Equal(typeof(Task<string>), t.Method.ReturnType));
        Assert.All(tools, t => Assert.Contains(t.Method.GetParameters(), p => p.ParameterType == typeof(CancellationToken)));
    }

    [Theory]
    [MemberData(nameof(ToolCatalog.NameData), MemberType = typeof(ToolCatalog))]
    public async Task Invoke_WorkspaceNotStarted_ReturnsStatusJsonWithoutThrowing(string toolName)
    {
        // Arrange
        using var workspace = new WorkspaceManager(NullLogger<WorkspaceManager>.Instance, TimeProvider.System);

        // Act
        var json = await ToolCatalog.Get(toolName).InvokeAsync(workspace, ct: TestContext.Current.CancellationToken);

        // Assert
        AssertNotReadyContract(toolName, json, expectedState: "NotStarted");
    }

    [Theory]
    [MemberData(nameof(ToolCatalog.NameData), MemberType = typeof(ToolCatalog))]
    public async Task Invoke_WorkspaceInErrorState_ReturnsStatusJsonWithoutThrowing(string toolName)
    {
        // Arrange
        TestSolutionFixture.RegisterMSBuild();
        using var workspace = new WorkspaceManager(
            NullLogger<WorkspaceManager>.Instance, new TestClock(DateTimeOffset.UtcNow));
        var missing = Path.Combine(Path.GetTempPath(), $"cwm-missing-{Guid.NewGuid():N}.sln");
        await Assert.ThrowsAnyAsync<Exception>(() => workspace.LoadSolutionAsync(missing, TestContext.Current.CancellationToken));
        Assert.Equal(WorkspaceState.Error, workspace.State);

        // Act
        var json = await ToolCatalog.Get(toolName).InvokeAsync(workspace, ct: TestContext.Current.CancellationToken);

        // Assert
        AssertNotReadyContract(toolName, json, expectedState: "Error");
    }

    private static void AssertNotReadyContract(string toolName, string json, string expectedState)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(JsonValueKind.Object, root.ValueKind);

        if (WorkspaceIndependentTools.Contains(toolName))
        {
            Assert.False(root.GetProperty("Enabled").GetBoolean());
            return;
        }

        Assert.Equal(expectedState, root.GetProperty("State").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("Message").GetString()));
    }
}

/// <summary>
/// A cancelled call must end as cancellation — promptly, and never as a hang or a generic
/// error payload that hides the cancellation from the client.
/// </summary>
[Collection(SampleSolutionCollection.Name)]
public sealed class ToolCancellationTests(TestSolutionFixture fixture)
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Documented exceptions: these answer from in-memory metadata (project graph, project
    /// reference cycles, workspace health, the local telemetry store) without awaiting any
    /// Roslyn work, so unless the shared on-disk refresh happens to run (it observes the
    /// token) there is no point at which a cancelled token could stop them. They must still
    /// finish within the budget, either cancelled or with a normal, non-error answer. Any tool
    /// doing compilation or symbol work is NOT allowed here.
    /// </summary>
    private static readonly HashSet<string> CompletesWithoutObservingCancellation =
        ["detect_circular_dependencies", "get_project_graph", "get_token_savings", "get_workspace_status"];

    [Theory]
    [MemberData(nameof(ToolCatalog.NameData), MemberType = typeof(ToolCatalog))]
    public async Task Invoke_AlreadyCancelledToken_EndsAsCancellationPromptly(string toolName)
    {
        // Arrange
        await fixture.WorkspaceManager.WaitForWarmUpAsync();
        var tool = ToolCatalog.Get(toolName);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // Act
        var call = tool.InvokeAsync(fixture.WorkspaceManager, ToolArguments.Valid(toolName), cancelled.Token);
        var finished = await Task.WhenAny(call, Task.Delay(Budget, TestContext.Current.CancellationToken));

        // Assert
        Assert.True(finished == call, $"{toolName} did not finish within {Budget.TotalSeconds}s of a cancelled call.");
        if (CompletesWithoutObservingCancellation.Contains(toolName) && call.IsCompletedSuccessfully)
        {
            using var json = JsonDocument.Parse(await call);
            Assert.False(json.RootElement.TryGetProperty("Error", out _));
            return;
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
    }

    [Fact]
    public async Task DetectCircularDependencies_TypeScopeWithCancelledToken_ThrowsOperationCanceled()
    {
        // Arrange — the type-level scan compiles; only the project-level scan is exempt above.
        await fixture.WorkspaceManager.WaitForWarmUpAsync();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // Act
        var call = ToolCatalog.Get("detect_circular_dependencies").InvokeAsync(
            fixture.WorkspaceManager,
            new Dictionary<string, object?> { ["scope"] = "types", ["projectFilter"] = "SampleApi" },
            cancelled.Token);

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
    }
}
