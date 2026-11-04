using System.Text.Json;
using System.Text.Json.Nodes;

namespace CWM.RoslynNavigator;

/// <summary>
/// Shrinks the input schemas the SDK generates before tools/list sends them. An optional
/// parameter such as <c>string? file = null</c> comes out as
/// <c>"type":["string","null"],"default":null</c>; every optional parameter is already absent
/// from <c>required</c>, so <c>"type":"string"</c> says the same thing to a client. Across the
/// tools that is roughly a thousand bytes the model would otherwise read on every request.
/// Binding is unaffected: arguments are bound from the method signature, not this schema.
/// </summary>
internal static class ToolSchemaCompactor
{
    public static JsonElement Compact(JsonElement schema)
    {
        if (JsonNode.Parse(schema.GetRawText()) is not JsonObject root
            || root["properties"] is not JsonObject properties)
            return schema;

        foreach (var (_, node) in properties)
        {
            if (node is JsonObject property)
                CompactProperty(property);
        }

        return JsonSerializer.SerializeToElement(root);
    }

    private static void CompactProperty(JsonObject property)
    {
        if (property["type"] is JsonArray { Count: 2 } types
            && types.Select(t => t?.GetValue<string>()).ToList() is var names
            && names.Contains("null"))
        {
            property["type"] = names.First(n => n != "null");
        }

        if (property.TryGetPropertyValue("default", out var value) && value is null)
            property.Remove("default");
    }
}
