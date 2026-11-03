using System.Reflection;
using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;

namespace CWM.RoslynNavigator.Tests;

/// <summary>
/// Drives the real server process over stdio with the MCP client SDK. In-process tests call
/// tool methods directly and so never exercise JSON argument binding, the host's DI graph,
/// the call-tool filters registered in Program.cs, or the background workspace load.
/// </summary>
[Collection(McpServerCollection.Name)]
public sealed class McpServerEndToEndTests(McpServerFixture server)
{
    private static readonly HashSet<string> ErrorCodeValues =
    [
        .. typeof(ErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (string)f.GetRawConstantValue()!)
    ];

    [Fact]
    public async Task ListTools_RealServer_ReturnsExactlyTheAssemblyTools()
    {
        // Act
        var tools = await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ToolCatalog.Names.Order(StringComparer.Ordinal), tools.Select(t => t.Name).Order(StringComparer.Ordinal));
        Assert.Equal(26, tools.Count);
        Assert.All(tools, t => Assert.False(string.IsNullOrWhiteSpace(t.Description), t.Name));
    }

    [Fact]
    public void FirstCall_BeforeWorkspaceReady_ReturnsStatusOrAnswerNeverError()
    {
        // Act — sent the moment initialize completed, while the solution was loading.
        using var json = JsonDocument.Parse(server.FirstCallResponse);

        // Assert
        var root = json.RootElement;
        if (root.TryGetProperty("State", out var state))
            Assert.Contains(state.GetString(), new[] { "Loading", "NotStarted" });
        else
            Assert.Equal("OrderService", root.GetProperty("Symbols")[0].GetProperty("Name").GetString());
    }

    [Theory]
    [MemberData(nameof(ToolCatalog.NameData), MemberType = typeof(ToolCatalog))]
    public async Task CallTool_ValidArguments_ReturnsSuccessShapedJson(string toolName)
    {
        // Act
        var result = await server.CallAsync(toolName, ToolArguments.Valid(toolName), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(true, result.IsError);
        using var json = JsonDocument.Parse(McpServerFixture.Text(result));
        var root = json.RootElement;
        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.False(root.TryGetProperty("Error", out _), root.ToString());
        Assert.False(root.TryGetProperty(IncompleteWorkspaceNote.PropertyName, out _), root.ToString());
        if (toolName != "get_workspace_status")
            Assert.False(root.TryGetProperty("State", out _), $"{toolName} answered with a not-ready status: {root}");
    }

    [Theory]
    [MemberData(nameof(ToolArguments.ToolsWithInvalidArguments), MemberType = typeof(ToolArguments))]
    public async Task CallTool_InvalidArguments_ReturnsStructuredErrorNotProtocolError(string toolName)
    {
        // Act
        var result = await server.CallAsync(toolName, ToolArguments.Invalid(toolName), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(true, result.IsError);
        using var json = JsonDocument.Parse(McpServerFixture.Text(result));
        var error = json.RootElement.GetProperty("Error").GetString();
        Assert.Contains(error, ErrorCodeValues);
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("Message").GetString()));
    }

    [Fact]
    public async Task CallTool_ArgumentOfWrongJsonType_ReturnsToolErrorAndSessionSurvives()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;

        // Act
        var bad = await server.CallAsync("find_symbol",
            new Dictionary<string, object?> { ["name"] = "Order", ["maxResults"] = "lots" }, ct);
        var after = await server.CallTextAsync("find_symbol", ToolArguments.Valid("find_symbol"), ct);

        // Assert
        Assert.True(bad.IsError);
        Assert.Contains("OrderService", after);
    }

    [Fact]
    public async Task CallTool_UnknownTool_ThrowsProtocolErrorAndSessionSurvives()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;

        // Act
        var call = () => server.CallAsync("no_such_tool", null, ct);

        // Assert
        await Assert.ThrowsAnyAsync<Exception>(call);
        Assert.Contains("Ready", await server.CallTextAsync("get_workspace_status", null, ct));
    }

    [Fact]
    public async Task GetTokenSavings_TelemetryOn_RecordsCallsThroughTheFilter()
    {
        // Arrange
        Assert.SkipUnless(McpServerFixture.TelemetryEnabled, "Telemetry folder cannot be redirected on macOS.");
        var ct = TestContext.Current.CancellationToken;
        await server.CallTextAsync("find_symbol", ToolArguments.Valid("find_symbol"), ct);

        // Act — the writer is a background service, so records land shortly after the call.
        JsonElement savings = default;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            using var json = JsonDocument.Parse(await server.CallTextAsync("get_token_savings", null, ct));
            savings = json.RootElement.Clone();
            if (savings.GetProperty("TotalCalls").GetInt32() > 0) break;
            await Task.Delay(250, ct);
        }

        // Assert
        Assert.True(savings.GetProperty("Enabled").GetBoolean());
        Assert.True(savings.GetProperty("TotalCalls").GetInt32() > 0, savings.ToString());
    }
}
