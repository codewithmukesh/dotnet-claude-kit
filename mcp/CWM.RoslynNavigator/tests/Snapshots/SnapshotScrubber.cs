using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CWM.RoslynNavigator.Tests.Snapshots;

/// <summary>
/// Makes tool responses comparable across machines and operating systems before they are
/// snapshotted: absolute sample paths become "{SampleSolution}/…" with forward slashes,
/// timings become 0, CRLF becomes LF, and arrays are sorted canonically (file enumeration
/// order differs between file systems; the snapshot pins the contract — field names, types,
/// values — not ordering, which the per-tool tests own).
/// </summary>
public static class SnapshotScrubber
{
    private static readonly HashSet<string> VolatileNumbers = ["ElapsedMs", "MedianMs"];

    /// <summary>Indented, and without '-style escaping so snapshots read as plain text.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Scrub(string json, string sampleRoot)
    {
        var node = JsonNode.Parse(json);
        return Normalize(node, sampleRoot, propertyName: null)?.ToJsonString(SnapshotScrubber.JsonOptions)
            ?? "null";
    }

    private static JsonNode? Normalize(JsonNode? node, string sampleRoot, string? propertyName)
    {
        switch (node)
        {
            case JsonObject obj:
                var copy = new JsonObject();
                foreach (var (key, value) in obj.ToList())
                    copy[key] = Normalize(value, sampleRoot, key);
                return copy;

            case JsonArray array:
                var items = array.Select(item => Normalize(item, sampleRoot, propertyName)).ToList();
                var sorted = new JsonArray();
                foreach (var item in items.OrderBy(i => i?.ToJsonString() ?? "", StringComparer.Ordinal))
                    sorted.Add(item);
                return sorted;

            case JsonValue value when value.TryGetValue<string>(out var text):
                return JsonValue.Create(ScrubText(text, sampleRoot));

            case JsonValue value when propertyName is not null && VolatileNumbers.Contains(propertyName):
                return value.GetValueKind() == JsonValueKind.Null ? null : JsonValue.Create(0);

            default:
                return node?.DeepClone();
        }
    }

    private static string ScrubText(string text, string sampleRoot)
    {
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal);

        var index = text.IndexOf(sampleRoot, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return text;

        var rest = text[(index + sampleRoot.Length)..].Replace('\\', '/');
        return text[..index] + "{SampleSolution}" + rest;
    }
}
