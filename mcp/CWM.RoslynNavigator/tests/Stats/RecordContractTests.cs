using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using ServerContext = CWM.RoslynNavigator.Telemetry.TelemetryJsonContext;
using ServerRecord = CWM.RoslynNavigator.Telemetry.CallRecord;
using StatsContext = CWM.RoslynNavigator.Stats.StatsJsonContext;
using StatsRecord = CWM.RoslynNavigator.Stats.CallRecord;

namespace CWM.RoslynNavigator.Tests.Stats;

/// <summary>
/// The Stats CLI mirrors the server's record type rather than referencing the server, so
/// that a JSONL reader does not drag the Roslyn workspace stack with it. These tests are
/// what make that duplication safe: they round-trip a real server record through the
/// mirror, so any field added, renamed, or retyped on one side fails here rather than
/// silently reading as a default in production.
/// </summary>
public class RecordContractTests
{
    private static ServerRecord Sample() => new()
    {
        Timestamp = new DateTimeOffset(2026, 8, 7, 10, 22, 1, TimeSpan.Zero),
        Tool = "find_symbol",
        Ok = true,
        Results = 3,
        ResponseTokens = 180,
        BaselineTokens = 4200,
        Files = 3,
        ElapsedMs = 42,
        State = "ok",
        ErrorCode = null
    };

    [Fact]
    public void ServerRecord_DeserializesIntoStatsMirror_WithEveryFieldPreserved()
    {
        var json = JsonSerializer.Serialize(Sample(), ServerContext.Default.CallRecord);

        var mirrored = JsonSerializer.Deserialize(json, StatsContext.Default.CallRecord)!;

        Assert.Equal(Sample().Timestamp, mirrored.Timestamp);
        Assert.Equal("find_symbol", mirrored.Tool);
        Assert.True(mirrored.Ok);
        Assert.Equal(3, mirrored.Results);
        Assert.Equal(180, mirrored.ResponseTokens);
        Assert.Equal(4200, mirrored.BaselineTokens);
        Assert.Equal(3, mirrored.Files);
        Assert.Equal(42, mirrored.ElapsedMs);
        Assert.Equal("ok", mirrored.State);
        Assert.Null(mirrored.ErrorCode);
    }

    [Fact]
    public void StatsRecord_EveryServerPropertyName_IsMirrored()
    {
        // The contract is one-directional: the mirror only ever reads. A renamed property
        // would deserialize as a default rather than throw, so names are compared directly
        // instead of relying on the value round-trip to notice.
        var record = Sample() with { ErrorCode = "SymbolNotFound" }; // populate every field

        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(record, ServerContext.Default.CallRecord));

        var serverNames = document.RootElement.EnumerateObject()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        var mirrorNames = typeof(StatsRecord)
            .GetProperties()
            .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name)
            .Where(n => n is not null)
            .ToHashSet(StringComparer.Ordinal)!;

        var unreadable = serverNames.Except(mirrorNames).ToList();

        Assert.True(unreadable.Count == 0,
            $"The Stats mirror cannot read these server fields: {string.Join(", ", unreadable)}");
    }

    [Fact]
    public void Deserialize_ServerRecordWithErrorCode_KeepsErrorCode()
    {
        var record = Sample() with { Ok = false, State = "error", ErrorCode = "SymbolNotFound" };
        var json = JsonSerializer.Serialize(record, ServerContext.Default.CallRecord);

        var mirrored = JsonSerializer.Deserialize(json, StatsContext.Default.CallRecord)!;

        Assert.Equal("SymbolNotFound", mirrored.ErrorCode);
        Assert.Equal(0, mirrored.Saved);
    }

    [Theory]
    [InlineData(true, "ok", 4200, 180, 4020)]
    [InlineData(false, "ok", 4200, 180, 0)]       // no results: nothing was answered
    [InlineData(true, "loading", 4200, 180, 0)]   // workspace not ready: no work was done
    [InlineData(true, "ok", 100, 400, 0)]         // response cost more than the file: clamped
    public void Saved_MatchesTheServerDefinition(
        bool ok, string state, int baseline, int response, int expected)
    {
        var server = Sample() with
        {
            Ok = ok,
            State = state,
            BaselineTokens = baseline,
            ResponseTokens = response
        };
        var json = JsonSerializer.Serialize(server, ServerContext.Default.CallRecord);
        var mirrored = JsonSerializer.Deserialize(json, StatsContext.Default.CallRecord)!;

        Assert.Equal(expected, server.Saved);
        Assert.Equal(expected, mirrored.Saved);
    }
}
