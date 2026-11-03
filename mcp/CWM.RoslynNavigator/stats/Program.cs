using System.Text.Json;
using CWM.RoslynNavigator.Stats;

// Arguments are parsed by hand rather than with a parser library: there are five flags,
// and a preview dependency in a reporting tool is not worth the convenience.
var arguments = new CliArguments(args);

if (arguments.Has("--help") || arguments.Has("-h"))
{
    Console.WriteLine(CliArguments.HelpText);
    return 0;
}

if (arguments.Command == "calibrate")
    return await RunCalibrationAsync(arguments);

return RunReport(arguments);

static int RunReport(CliArguments arguments)
{
    var telemetryRoot = arguments.Value("--telemetry-root") ?? TelemetryReader.DefaultRoot;
    var transcriptRoot = arguments.Value("--transcript-root")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

    var telemetry = new TelemetryReader(telemetryRoot).Read();
    var transcripts = new TranscriptMiner(transcriptRoot).Mine();

    if (arguments.Value("--repo") is { } filter)
    {
        telemetry = telemetry
            .Where(t => t.Info.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                     || t.Info.SolutionPath.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        transcripts = transcripts
            .Where(t => t.RepoPath.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    // The divisor is read from whatever the server recorded, so the report cannot silently
    // disagree with the data that produced it.
    var first = telemetry.FirstOrDefault();
    var bytesPerToken = first?.Info.BytesPerToken is > 0 ? first.Info.BytesPerToken : 3.5;
    var calibrated = first?.Info.Calibrated ?? false;

    var rate = decimal.TryParse(arguments.Value("--input-rate"), out var parsed) ? parsed : 3.00m;

    var report = ReportBuilder.Build(telemetry, transcripts, bytesPerToken, calibrated, rate);

    Console.WriteLine(arguments.Has("--json")
        ? JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })
        : arguments.Has("--markdown")
            ? Renderers.Markdown(report)
            : Renderers.Table(report));

    return 0;
}

static async Task<int> RunCalibrationAsync(CliArguments arguments)
{
    var apiKey = arguments.Value("--api-key") ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        Console.Error.WriteLine("calibrate needs an API key: --api-key <key> or ANTHROPIC_API_KEY.");
        Console.Error.WriteLine("count_tokens is free; the key is only used to reach it.");
        return 1;
    }

    var root = arguments.Value("--path") ?? Directory.GetCurrentDirectory();
    var sampleSize = int.TryParse(arguments.Value("--sample"), out var n) ? n : 200;

    var files = Calibrator.SampleFiles(root, sampleSize);
    if (files.Count == 0)
    {
        Console.Error.WriteLine($"No .cs files found under {root}.");
        return 1;
    }

    Console.WriteLine($"Calibrating against {files.Count} C# file(s) under {root}...");

    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    var result = await new Calibrator(http, apiKey).RunAsync(files, Console.WriteLine);

    if (result.Files == 0)
    {
        Console.Error.WriteLine("No files were successfully counted. Check the API key and connectivity.");
        return 1;
    }

    Console.WriteLine();
    Console.WriteLine($"  Files sampled   {result.Files}");
    Console.WriteLine($"  Total bytes     {result.TotalBytes:N0}");
    Console.WriteLine($"  Total tokens    {result.TotalTokens:N0}");
    Console.WriteLine($"  Bytes per token {result.BytesPerToken:N4}");
    Console.WriteLine($"  R²              {result.RSquared:N4}");
    Console.WriteLine();
    Console.WriteLine("To adopt this, update TokenEstimator in the server:");
    Console.WriteLine($"    BytesPerToken           = {result.BytesPerToken:N2}");
    Console.WriteLine("    IsCalibrated            = true");
    Console.WriteLine($"    CalibrationSampleSize   = {result.Files}");
    Console.WriteLine();
    Console.WriteLine("Record the sample size and R² next to the constant so the figure stays checkable.");

    return 0;
}

namespace CWM.RoslynNavigator.Stats
{
    internal sealed class CliArguments(string[] args)
    {
        public const string HelpText = """
            CWM.RoslynNavigator.Stats — token savings across repositories

            Usage:
              stats [options]              Aggregate report (default)
              stats calibrate [options]    Derive the bytes-per-token divisor

            Report options:
              --json                   Emit JSON
              --markdown               Emit markdown
              --repo <substring>       Limit to matching repositories
              --input-rate <usd>       Input price per million tokens (default 3.00)
              --telemetry-root <path>  Override the telemetry directory
              --transcript-root <path> Override the Claude Code projects directory

            Calibrate options:
              --api-key <key>          Anthropic API key (or ANTHROPIC_API_KEY)
              --path <dir>             Directory to sample C# files from (default: cwd)
              --sample <n>             Files to sample (default 200)

            Telemetry is opt-in and local. Set CWM_ROSLYN_TELEMETRY=on for the MCP server
            to record; nothing is ever transmitted.
            """;

        public string? Command { get; } = args.Length > 0 && !args[0].StartsWith('-') ? args[0] : null;

        public bool Has(string flag) => Array.Exists(args, a => a == flag);

        public string? Value(string flag)
        {
            var index = Array.IndexOf(args, flag);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
    }
}
