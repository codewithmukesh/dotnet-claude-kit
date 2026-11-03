using System.Text.Json;
using System.Text.Json.Nodes;
using CWM.RoslynNavigator.Tests.Fixtures;

namespace CWM.RoslynNavigator.Tests.Snapshots;

/// <summary>
/// What an agent sees of each tool: its input schema and description (how it decides to call
/// the tool and with what), served by the real server over tools/list. A renamed parameter,
/// a dropped default, or a reworded description changes how agents call the tool, so it must
/// show up as a reviewed snapshot diff rather than slip through.
/// </summary>
[Collection(McpServerCollection.Name)]
public sealed class ToolSchemaSnapshotTests(McpServerFixture server)
{
    [Fact]
    public async Task ListTools_RealServer_MatchesVerifiedSchemas()
    {
        // Act
        var tools = await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var snapshot = new JsonArray([
            .. tools.OrderBy(t => t.Name, StringComparer.Ordinal).Select(t => new JsonObject
            {
                ["name"] = t.Name,
                ["description"] = t.Description,
                ["inputSchema"] = JsonNode.Parse(t.JsonSchema.GetRawText()),
            })
        ]);
        Snapshot.Match(snapshot.ToJsonString(SnapshotScrubber.JsonOptions), "tools-list");
    }
}

/// <summary>
/// One representative response per tool against SampleSolution, scrubbed of paths and
/// timings. Pins the response contract — field names such as Truncated / TotalFound / Limit,
/// value shapes, error codes — that agents branch on.
/// </summary>
[Collection(SampleSolutionCollection.Name)]
public sealed class ToolResponseSnapshotTests(TestSolutionFixture fixture)
{
    [Theory]
    [MemberData(nameof(ToolCatalog.NameData), MemberType = typeof(ToolCatalog))]
    public async Task Invoke_ValidArguments_MatchesVerifiedResponse(string toolName)
    {
        // Arrange
        await fixture.WorkspaceManager.WaitForWarmUpAsync();
        var sampleRoot = Path.GetDirectoryName(TestSolutionFixture.SampleSolutionPath)!;

        // Act
        var json = await ToolCatalog.Get(toolName).InvokeAsync(
            fixture.WorkspaceManager, ToolArguments.Valid(toolName), TestContext.Current.CancellationToken);

        // Assert
        Snapshot.Match(SnapshotScrubber.Scrub(json, sampleRoot), $"response.{toolName}");
    }
}
