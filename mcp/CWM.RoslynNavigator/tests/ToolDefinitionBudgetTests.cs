using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using CWM.RoslynNavigator.Telemetry;
using CWM.RoslynNavigator.Tests.Fixtures;

namespace CWM.RoslynNavigator.Tests;

/// <summary>
/// What the tool definitions cost. A client sends every tool's name, description, and input
/// schema, plus the server instructions, to the model on every request of every session, so
/// this overhead is paid far more often than any tool response. Measured from the real
/// tools/list and initialize responses.
///
/// <para>Counts what reaches the model: name, description, and input schema, serialized
/// without the SDK's HTML-safe escaping (a client decodes the JSON before handing it on).
/// Wire-only fields such as <c>execution</c> are not forwarded to the model and not counted.</para>
/// </summary>
[Collection(McpServerCollection.Name)]
public sealed class ToolDefinitionBudgetTests(McpServerFixture server)
{
    // Before the 2026-11-04 trim: 28,116 bytes (~8,033 tokens), no instructions.
    // After: 14,308 bytes (~4,088 tokens) including 871 bytes of instructions; largest tool 918.
    // Ceilings sit just above the trimmed size so the definitions cannot creep back up
    // unnoticed. Raising one is a deliberate, reviewed decision, not a side effect: a rule
    // that applies to every tool belongs in ServerInstructions, not in each description.
    private const int MaxTotalBytes = 15_000;
    private const int MaxToolBytes = 1_000;
    private const int MaxInstructionsBytes = 1_000;

    private static readonly JsonSerializerOptions ModelView = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    [Fact]
    public async Task ListTools_RealServer_StaysWithinTokenBudget()
    {
        // Arrange
        var output = TestContext.Current.TestOutputHelper;

        // Act
        var tools = await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var sizes = tools
            .Select(t => (t.Name, Bytes: Bytes(new JsonObject
            {
                ["name"] = t.Name,
                ["description"] = t.Description,
                ["inputSchema"] = JsonNode.Parse(t.JsonSchema.GetRawText()),
            }.ToJsonString(ModelView))))
            .OrderByDescending(s => s.Bytes)
            .ToList();
        var instructionsBytes = Bytes(server.Client.ServerInstructions);
        var total = sizes.Sum(s => s.Bytes) + instructionsBytes;

        foreach (var (name, bytes) in sizes)
            output?.WriteLine($"{name,-30} {bytes,6} B  ~{TokenEstimator.FromBytes(bytes),5} tokens");
        output?.WriteLine($"{"(server instructions)",-30} {instructionsBytes,6} B  ~{TokenEstimator.FromBytes(instructionsBytes),5} tokens");
        output?.WriteLine($"{"TOTAL",-30} {total,6} B  ~{TokenEstimator.FromBytes(total),5} tokens");

        // Assert
        Assert.True(total <= MaxTotalBytes, $"Tool definitions plus instructions are {total} bytes; the budget is {MaxTotalBytes}.");
        Assert.True(instructionsBytes <= MaxInstructionsBytes, $"Server instructions are {instructionsBytes} bytes; the budget is {MaxInstructionsBytes}.");
        Assert.All(sizes, s => Assert.True(s.Bytes <= MaxToolBytes, $"{s.Name} is {s.Bytes} bytes; the per-tool budget is {MaxToolBytes}."));
    }

    [Fact]
    public void Initialize_RealServer_SendsSharedConventions()
    {
        // Act — trimmed descriptions rely on these being stated once, server-wide.
        var instructions = server.Client.ServerInstructions;

        // Assert
        Assert.NotNull(instructions);
        Assert.Contains("SymbolId", instructions);
        Assert.Contains("TotalFound", instructions);
        Assert.Contains("get_workspace_status", instructions);
    }

    private static int Bytes(string? text) => Encoding.UTF8.GetByteCount(text ?? "");
}
