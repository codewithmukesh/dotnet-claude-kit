using System.Diagnostics;
using CWM.RoslynNavigator.Bench;
using CWM.RoslynNavigator.Bench.Corpus;
using CWM.RoslynNavigator.Bench.Reporting;
using CWM.RoslynNavigator.Bench.Server;

// Black-box benchmark for CWM.RoslynNavigator. See bench/README.md for the method.
//   select  pick a corpus's targets once and write them into its JSON definition
//   run     benchmark corpora × server builds; writes JSON per run plus report.md
//   report  re-render report.md from a results folder

using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancel.Cancel();
};

var command = CommandLine.Parse(args);

try
{
    return command.Verb switch
    {
        "select" => await SelectAsync(command, cancel.Token),
        "run" => await RunAsync(command, cancel.Token),
        "report" => Report(command),
        "probe" => await ProbeAsync(command, cancel.Token),
        "call" => await CallAsync(command, cancel.Token),
        _ => Help(),
    };
}
catch (Exception e) when (e is ArgumentException or FileNotFoundException or InvalidOperationException or InvalidDataException)
{
    Console.Error.WriteLine($"error: {e.Message}");
    return 2;
}

static async Task<int> SelectAsync(CommandLine command, CancellationToken ct)
{
    var corpusFile = command.Required("corpus");
    var corpus = ResultStore.ReadCorpus(corpusFile);
    var solution = SolutionPath(command.Required("corpus-root"), corpus);
    var build = ServerBuild.Parse(command.Required("server"));

    Console.WriteLine($"[select {corpus.Name}] starting {build.Label}...");
    await using var session = await ServerSession.StartAsync(build, solution, ct);
    _ = await session.WaitForReadyAsync(TimeSpan.FromMinutes(20), ct)
        ?? throw new InvalidOperationException("Server never became ready.");
    _ = await session.WaitForWarmAsync(TimeSpan.FromMinutes(20), ct);

    var selector = new TargetSelector(session, SolutionScope.For(solution), Console.Out);
    var targets = await selector.SelectAsync($"{build.Label} {session.ServerVersion}", ct);

    ResultStore.WriteCorpus(corpusFile, corpus with { Targets = targets });
    Console.WriteLine($"[select {corpus.Name}] wrote targets to {corpusFile}");
    return 0;
}

static async Task<int> RunAsync(CommandLine command, CancellationToken ct)
{
    var root = command.Required("corpus-root");
    var corpora = command.All("corpus").Select(ResultStore.ReadCorpus).ToList();
    var builds = command.All("server").Select(ServerBuild.Parse).ToList();
    if (corpora.Count == 0 || builds.Count == 0) throw new ArgumentException("Need at least one --corpus and one --server.");

    var settings = new BenchSettings
    {
        WarmIterations = (int)command.Number("warm-iterations", 5),
        CallTimeout = TimeSpan.FromSeconds(command.Number("timeout-s", 300)),
        WarmSkipThreshold = TimeSpan.FromSeconds(command.Number("warm-skip-s", 60)),
        GrepHeadLimit = (int)command.Number("grep-head-limit", 250),
        PrefillTokensPerSecond = command.Number("prefill-tps", 10000),
        WarmTimeout = TimeSpan.FromMinutes(command.Number("warm-timeout-min", 20)),
    };

    var runDate = command.Optional("date") ?? DateTime.UtcNow.ToString("yyyy-MM-dd");
    var output = Path.Combine(command.Optional("out") ?? "results", runDate);
    var skipExisting = command.Flag("skip-existing");
    var runner = new BenchmarkRunner(settings, Console.Out);
    var total = Stopwatch.StartNew();

    foreach (var corpus in corpora)
    {
        var solution = SolutionPath(root, corpus);
        foreach (var build in builds)
        {
            if (skipExisting && File.Exists(Path.Combine(output, ResultStore.RunFileName(corpus.Name, build.Label))))
            {
                Console.WriteLine($"[{corpus.Name} × {build.Label}] exists, skipped");
                continue;
            }

            var watch = Stopwatch.StartNew();
            var run = await runner.RunAsync(corpus, root, solution, build, runDate, ct);
            ResultStore.WriteRun(output, run);
            Console.WriteLine($"[{corpus.Name} × {build.Label}] done in {watch.Elapsed.TotalMinutes:0.0} min");
        }
    }

    WriteReport(output, command.Optional("after"));
    Console.WriteLine($"All runs done in {total.Elapsed.TotalMinutes:0.0} min → {output}");
    return 0;
}

static async Task<int> ProbeAsync(CommandLine command, CancellationToken ct)
{
    var corpus = ResultStore.ReadCorpus(command.Required("corpus"));
    var solution = SolutionPath(command.Required("corpus-root"), corpus);
    var build = ServerBuild.Parse(command.Required("server"));

    await using var session = await ServerSession.StartAsync(build, solution, ct);
    var ready = await session.WaitForReadyAsync(TimeSpan.FromMinutes(20), ct);
    var graph = await session.CallAsync("get_project_graph", [], TimeSpan.FromMinutes(2), ct);
    Console.WriteLine($"server {session.ServerVersion}, ready after {ready?.ToString() ?? "n/a"} ms, tools: {session.ToolNames.Count}");
    Console.WriteLine(graph.Payload is { Length: > 1500 } p ? p[..1500] + "…" : graph.Payload);
    foreach (var problem in session.LoadProblems) Console.WriteLine($"load problem: {problem}");
    return 0;
}

// One tool call against a corpus, printing the raw response — for reproducing a bug report.
static async Task<int> CallAsync(CommandLine command, CancellationToken ct)
{
    var corpus = ResultStore.ReadCorpus(command.Required("corpus"));
    var solution = SolutionPath(command.Required("corpus-root"), corpus);
    var build = ServerBuild.Parse(command.Required("server"));
    var tool = command.Required("tool");
    var arguments = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(command.Optional("args-file") is { } argsFile ? File.ReadAllText(argsFile) : command.Optional("args") ?? "{}")!
        .ToDictionary(a => a.Key, a => (object?)a.Value);

    await using var session = await ServerSession.StartAsync(build, solution, ct);
    _ = await session.WaitForReadyAsync(TimeSpan.FromMinutes(20), ct);
    if (command.Flag("warm")) _ = await session.WaitForWarmAsync(TimeSpan.FromMinutes(20), ct);
    var result = await session.CallAsync(tool, arguments, TimeSpan.FromSeconds(command.Number("timeout-s", 300)), ct);
    Console.WriteLine($"{tool}: {result.Status} in {result.ElapsedMs:0} ms, {result.Bytes} bytes");
    Console.WriteLine(result.Payload ?? result.ErrorMessage);
    if (!result.Answered)
    {
        Console.WriteLine("--- server stderr (tail) ---");
        foreach (var line in session.StderrTail.TakeLast(60)) Console.WriteLine(line);
    }

    return 0;
}

static int Report(CommandLine command)
{
    WriteReport(command.Required("in"), command.Optional("after"));
    return 0;
}

static void WriteReport(string directory, string? after)
{
    var runs = ResultStore.ReadRuns(directory);
    if (runs.Count == 0) throw new InvalidOperationException($"No run results in {directory}.");
    var path = Path.Combine(directory, "report.md");
    File.WriteAllText(path, MarkdownReport.Render(runs, after).ReplaceLineEndings("\n"));
    Console.WriteLine($"Report: {path}");
}

static string SolutionPath(string corpusRoot, CorpusDefinition corpus)
{
    var path = Path.GetFullPath(Path.Combine(corpusRoot, corpus.CloneDirectory, corpus.Solution));
    return File.Exists(path) ? path : throw new FileNotFoundException($"Solution not found: {path}", path);
}

static int Help()
{
    Console.WriteLine("""
        CWM.RoslynNavigator.Bench — black-box benchmark over MCP stdio

          select --corpus <def.json> --corpus-root <dir> --server <label=path>
          run    --corpus-root <dir> --corpus <def.json>... --server <label=path>...
                 [--out results] [--date yyyy-MM-dd] [--warm-iterations 5] [--timeout-s 300]
                 [--warm-skip-s 60] [--warm-timeout-min 20] [--grep-head-limit 250] [--prefill-tps 10000]
                 [--after <label>] [--skip-existing]
          report --in <results/yyyy-MM-dd> [--after <label>]

        A server path ending in .dll runs under the dotnet host; anything else runs directly.
        """);
    return 1;
}
