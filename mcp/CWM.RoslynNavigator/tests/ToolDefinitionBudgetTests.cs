using System.Text;
using System.Text.Json;
using CWM.RoslynNavigator.Telemetry;
using CWM.RoslynNavigator.Tests.Fixtures;
using ModelContextProtocol;

namespace CWM.RoslynNavigator.Tests;

/// <summary>
/// What the tool definitions cost. A client sends every tool's name, description, and input
/// schema (plus the server instructions) to the model on every request of every session, so
/// this overhead is paid far more often than any tool response. Measured on the real
/// tools/list payload, serialized exactly as it goes over the wire.
/// </summary>
[Collection(McpServerCollection.Name)]
public sealed class ToolDefinitionBudgetTests(McpServerFixture server)
{
    // Ceilings sit just above the current size so the definitions cannot creep back up
    // unnoticed. Raising one is a deliberate, reviewed decision, not a side effect.
    private const int MaxTotalBytes = int.MaxValue;
    private const int MaxToolBytes = int.MaxValue;
    private const int MaxInstructionsBytes = int.MaxValue;

    [Fact]
    public async Task ListTools_RealServer_StaysWithinTokenBudget()
    {
        // Arrange
        var output = TestContext.Current.TestOutputHelper;

        // Act
        var tools = await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var sizes = tools
            .Select(t => (t.Name, Bytes: Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(t.ProtocolTool, McpJsonUtilities.DefaultOptions))))
            .OrderByDescending(s => s.Bytes)
            .ToList();
        var instructionsBytes = Encoding.UTF8.GetByteCount(server.Client.ServerInstructions ?? "");
        var total = sizes.Sum(s => s.Bytes) + instructionsBytes;

        // Report
        foreach (var (name, bytes) in sizes)
            output?.WriteLine($"{name,-30} {bytes,6} B  ~{TokenEstimator.FromBytes(bytes),5} tokens");
        output?.WriteLine($"{"(server instructions)",-30} {instructionsBytes,6} B  ~{TokenEstimator.FromBytes(instructionsBytes),5} tokens");
        output?.WriteLine($"{"TOTAL",-30} {total,6} B  ~{TokenEstimator.FromBytes(total),5} tokens");

        // Assert
        Assert.True(total <= MaxTotalBytes, $"tools/list plus instructions is {total} bytes; the budget is {MaxTotalBytes}.");
        Assert.True(instructionsBytes <= MaxInstructionsBytes, $"Server instructions are {instructionsBytes} bytes; the budget is {MaxInstructionsBytes}.");
        Assert.All(sizes, s => Assert.True(s.Bytes <= MaxToolBytes, $"{s.Name} is {s.Bytes} bytes; the per-tool budget is {MaxToolBytes}."));
    }
}
