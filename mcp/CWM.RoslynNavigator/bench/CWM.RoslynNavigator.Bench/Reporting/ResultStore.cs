using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using CWM.RoslynNavigator.Bench.Corpus;

namespace CWM.RoslynNavigator.Bench.Reporting;

/// <summary>Reads and writes the bench's JSON: corpus definitions and run results.</summary>
internal static class ResultStore
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        NewLine = "\n", // Committed files: identical bytes on every OS.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static CorpusDefinition ReadCorpus(string path) =>
        JsonSerializer.Deserialize<CorpusDefinition>(File.ReadAllText(path), Options)
        ?? throw new InvalidDataException($"Empty corpus definition: {path}");

    public static void WriteCorpus(string path, CorpusDefinition corpus) =>
        File.WriteAllText(path, JsonSerializer.Serialize(corpus, Options) + "\n");

    public static string RunFileName(string corpus, string build) => $"{corpus}-{build}.json";

    public static void WriteRun(string directory, RunResult run)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, RunFileName(run.Corpus.Name, run.Build.Label)),
            JsonSerializer.Serialize(run, Options) + "\n");
    }

    public static List<RunResult> ReadRuns(string directory) =>
        [.. Directory.EnumerateFiles(directory, "*.json")
            .Order(StringComparer.Ordinal)
            .Select(f => JsonSerializer.Deserialize<RunResult>(File.ReadAllText(f), Options))
            .OfType<RunResult>()];
}
