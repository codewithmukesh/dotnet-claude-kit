using System.Text.Json.Serialization;

namespace CWM.RoslynNavigator.Stats;

/// <summary>
/// Mirror of the server's call record.
///
/// <para>Deliberately duplicated rather than shared by project reference: this CLI reads
/// JSONL, and referencing the server would drag the whole Roslyn workspace stack into a
/// tool that never opens a solution. The duplication is held in sync by a contract test
/// that round-trips a real server record through this type — if a field is added, renamed,
/// or retyped on either side, that test fails.</para>
/// </summary>
internal sealed record CallRecord
{
    [JsonPropertyName("ts")] public DateTimeOffset Timestamp { get; init; }
    [JsonPropertyName("tool")] public string Tool { get; init; } = "";
    [JsonPropertyName("ok")] public bool Ok { get; init; }
    [JsonPropertyName("results")] public int Results { get; init; }
    [JsonPropertyName("respTok")] public int ResponseTokens { get; init; }
    [JsonPropertyName("baseTok")] public int BaselineTokens { get; init; }
    [JsonPropertyName("files")] public int Files { get; init; }
    [JsonPropertyName("ms")] public long ElapsedMs { get; init; }
    [JsonPropertyName("state")] public string State { get; init; } = "";
    [JsonPropertyName("err")] public string? ErrorCode { get; init; }

    public int Saved => Ok && State == "ok" ? Math.Max(0, BaselineTokens - ResponseTokens) : 0;
}

/// <summary>Mirror of the server's repo.json.</summary>
internal sealed record RepoInfo
{
    [JsonPropertyName("solutionPath")] public string SolutionPath { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("projectCount")] public int ProjectCount { get; init; }
    [JsonPropertyName("totalSourceBytes")] public long TotalSourceBytes { get; init; }
    [JsonPropertyName("totalSourceTokens")] public int TotalSourceTokens { get; init; }
    [JsonPropertyName("bytesPerToken")] public double BytesPerToken { get; init; }
    [JsonPropertyName("calibrated")] public bool Calibrated { get; init; }
}

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(CallRecord))]
[JsonSerializable(typeof(RepoInfo))]
internal sealed partial class StatsJsonContext : JsonSerializerContext;
