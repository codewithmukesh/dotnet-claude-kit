using System.Text.Json.Serialization;

namespace CWM.RoslynNavigator.Telemetry;

/// <summary>
/// One tool call. Property names are short because this file grows by a line per call and
/// is read back in bulk.
/// </summary>
internal sealed record CallRecord
{
    [JsonPropertyName("ts")] public required DateTimeOffset Timestamp { get; init; }
    [JsonPropertyName("tool")] public required string Tool { get; init; }
    [JsonPropertyName("ok")] public required bool Ok { get; init; }
    [JsonPropertyName("results")] public required int Results { get; init; }

    /// <summary>Tokens the response actually cost.</summary>
    [JsonPropertyName("respTok")] public required int ResponseTokens { get; init; }

    /// <summary>Tokens reading the source files would have cost, after all dedup guards.</summary>
    [JsonPropertyName("baseTok")] public required int BaselineTokens { get; init; }

    /// <summary>Files that contributed to the baseline — those not already counted this session.</summary>
    [JsonPropertyName("files")] public required int Files { get; init; }

    [JsonPropertyName("ms")] public required long ElapsedMs { get; init; }

    /// <summary>"ok", "error", or "loading".</summary>
    [JsonPropertyName("state")] public required string State { get; init; }

    [JsonPropertyName("err")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorCode { get; init; }

    /// <summary>
    /// Savings for this call. Only a successful call against a ready workspace can have
    /// saved anything: an error returned no answer, and a not-ready call did no work.
    /// </summary>
    [JsonIgnore]
    public int Saved => Ok && State == TelemetryStates.Ok
        ? Math.Max(0, BaselineTokens - ResponseTokens)
        : 0;
}

internal static class TelemetryStates
{
    public const string Ok = "ok";
    public const string Error = "error";
    public const string Loading = "loading";
}

/// <summary>
/// Source-generated serialization. Avoids reflection on the write path and keeps the server
/// trim- and AOT-friendly.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(CallRecord))]
internal sealed partial class TelemetryJsonContext : JsonSerializerContext;
