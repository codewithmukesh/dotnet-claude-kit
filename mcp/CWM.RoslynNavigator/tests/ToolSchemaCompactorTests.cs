using System.Text.Json;

namespace CWM.RoslynNavigator.Tests;

public sealed class ToolSchemaCompactorTests
{
    [Fact]
    public void Compact_NullableOptionalParameter_CollapsesTypeAndDropsNullDefault()
    {
        // Arrange
        var schema = Parse("""{"type":"object","properties":{"file":{"description":"d","type":["string","null"],"default":null}}}""");

        // Act
        var compacted = ToolSchemaCompactor.Compact(schema);

        // Assert
        Assert.Equal("""{"type":"object","properties":{"file":{"description":"d","type":"string"}}}""", compacted.GetRawText());
    }

    [Fact]
    public void Compact_RequiredAndDefaultedParameters_LeavesThemUnchanged()
    {
        // Arrange
        const string json = """{"type":"object","properties":{"name":{"type":"string"},"maxResults":{"type":"integer","default":50}},"required":["name"]}""";

        // Act
        var compacted = ToolSchemaCompactor.Compact(Parse(json));

        // Assert
        Assert.Equal(json, compacted.GetRawText());
    }

    [Fact]
    public void Compact_SchemaWithoutProperties_ReturnsItAsIs()
    {
        // Arrange
        var schema = Parse("""{"type":"object"}""");

        // Act
        var compacted = ToolSchemaCompactor.Compact(schema);

        // Assert
        Assert.Equal("""{"type":"object"}""", compacted.GetRawText());
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
