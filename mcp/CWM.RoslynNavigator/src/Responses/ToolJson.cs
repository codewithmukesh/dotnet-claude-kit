using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace CWM.RoslynNavigator.Responses;

/// <summary>
/// The one way tools serialize their responses. Every byte of a response is a token the
/// agent pays for, so all tools share these settings:
/// <list type="bullet">
/// <item>Null properties are omitted (a missing optional field reads the same as null).</item>
/// <item>Relaxed escaping: an apostrophe or a non-ASCII character is written as itself, not
/// as a six-character <c>'</c> escape. The payload travels inside a JSON-RPC string and
/// is never embedded in HTML, which is what the default escaping guards against.</item>
/// <item>Source-generated metadata (<see cref="ToolJsonContext"/>): no reflection on the
/// first call of each tool.</item>
/// </list>
/// </summary>
internal static class ToolJson
{
    private static readonly ToolJsonContext Context = new(new JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    public static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, (JsonTypeInfo<T>)(Context.GetTypeInfo(typeof(T))
            ?? throw new InvalidOperationException($"{typeof(T).Name} is not registered in {nameof(ToolJsonContext)}.")));
}

[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(StatusResponse))]
[JsonSerializable(typeof(SymbolSearchResult))]
[JsonSerializable(typeof(ReferencesResult))]
[JsonSerializable(typeof(ImplementationsResult))]
[JsonSerializable(typeof(CallersResult))]
[JsonSerializable(typeof(OverridesResult))]
[JsonSerializable(typeof(TypeHierarchyResult))]
[JsonSerializable(typeof(ProjectGraphResult))]
[JsonSerializable(typeof(ProjectGraphSummaryResult))]
[JsonSerializable(typeof(PublicApiResult))]
[JsonSerializable(typeof(DiagnosticsResult))]
[JsonSerializable(typeof(WorkspaceStatusResult))]
[JsonSerializable(typeof(SymbolDetail))]
[JsonSerializable(typeof(AntiPatternsResult))]
[JsonSerializable(typeof(DeadCodeResult))]
[JsonSerializable(typeof(CircularDependenciesResult))]
[JsonSerializable(typeof(DependencyGraphResult))]
[JsonSerializable(typeof(TestCoverageMapResult))]
[JsonSerializable(typeof(TokenSavingsResult))]
[JsonSerializable(typeof(ChangeImpactResult))]
[JsonSerializable(typeof(StackTraceResult))]
[JsonSerializable(typeof(SymbolAtResult))]
[JsonSerializable(typeof(RenamePreviewResult))]
[JsonSerializable(typeof(SymbolSourceResult))]
[JsonSerializable(typeof(FileOutlineResult))]
[JsonSerializable(typeof(NugetPackagesResult))]
[JsonSerializable(typeof(EndpointMapResult))]
[JsonSerializable(typeof(DiRegistrationsResult))]
internal sealed partial class ToolJsonContext : JsonSerializerContext;
