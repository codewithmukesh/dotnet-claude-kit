using System.Diagnostics;
using System.Text;
using CWM.RoslynNavigator.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;

namespace CWM.RoslynNavigator.Tests.Scale;

/// <summary>One heavy tool call measured at one solution size; ResponseChars is what Claude Code counts.</summary>
public sealed record ToolMeasurement(string Tool, TimeSpan Elapsed, int ResponseChars);

/// <summary>Everything measured for one generated solution size.</summary>
public sealed record ScaleMeasurement(
    int Projects,
    TimeSpan Load,
    TimeSpan WarmUp,
    long ManagedBytesAfterWarmUp,
    long WorkingSetBytes,
    IReadOnlyList<ToolMeasurement> Tools,
    IReadOnlyList<string> ContractFailures)
{
    /// <summary>The heavy tools and the arguments each is timed with.</summary>
    public static readonly IReadOnlyList<(string Tool, Dictionary<string, object?> Args)> HeavyCalls =
    [
        ("find_references", new() { ["symbolName"] = "IWidgetHandler" }),
        ("find_implementations", new() { ["interfaceName"] = "IWidgetHandler" }),
        ("find_dead_code", new()),
        ("detect_antipatterns", new()),
        ("get_diagnostics", new()),
        ("get_project_graph", new()),
        ("detect_circular_dependencies", new()),
        ("find_symbol", new() { ["name"] = "CWT", ["match"] = "camelhump" }),
        ("find_callers", new() { ["methodName"] = "WidgetRegistry.Register" }),
        ("get_type_hierarchy", new() { ["typeName"] = "IWidgetHandler" }),
        ("get_endpoint_map", new()),
        ("get_di_registrations", new()),
        ("get_workspace_status", new()),
    ];

    public ToolMeasurement this[string tool] => Tools.Single(t => t.Tool == tool);

    /// <summary>
    /// Generates <paramref name="projects"/> projects in temp, loads and warms them, then times
    /// each heavy tool (best of two runs, so one GC pause or disk hiccup is not the answer).
    /// </summary>
    public static async Task<ScaleMeasurement> MeasureAsync(int projects, CancellationToken ct)
    {
        TestSolutionFixture.RegisterMSBuild();
        var root = Path.Combine(Path.GetTempPath(), $"cwm-rn-scale{projects}-{Guid.NewGuid():N}");

        try
        {
            var solutionPath = LargeSolutionGenerator.Generate(root, projects);
            using var workspace = new WorkspaceManager(NullLogger<WorkspaceManager>.Instance, TimeProvider.System);

            var timer = Stopwatch.StartNew();
            await workspace.LoadSolutionAsync(solutionPath, ct);
            var load = timer.Elapsed;

            Assert.Equal(WorkspaceState.Ready, workspace.State);
            Assert.Equal(projects + 1, workspace.ProjectCount);

            timer.Restart();
            await workspace.WaitForWarmUpAsync();
            var warmUp = timer.Elapsed;

            var tools = new List<ToolMeasurement>();
            foreach (var (tool, args) in HeavyCalls)
                tools.Add(await TimeAsync(workspace, tool, args, ct));

            var contractFailures = await ResponseContract.CheckAsync(workspace, projects, ct);

            var managed = GC.GetTotalMemory(forceFullCollection: true);
            using var process = Process.GetCurrentProcess();
            var measurement = new ScaleMeasurement(projects, load, warmUp, managed, process.WorkingSet64, tools, contractFailures);

            Report(measurement.Describe());
            return measurement;
        }
        finally
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Temp folder; leaving it behind only costs disk space.
            }
        }
    }

    public string Describe()
    {
        var text = new StringBuilder()
            .Append($"{Projects} projects: load {Load.TotalSeconds:F1}s, warm-up {WarmUp.TotalSeconds:F1}s, ")
            .Append($"managed {ManagedBytesAfterWarmUp / 1_048_576} MB, working set {WorkingSetBytes / 1_048_576} MB");
        foreach (var tool in Tools)
            text.Append($"\n  {tool.Tool,-30} {tool.Elapsed.TotalMilliseconds,9:F0} ms {tool.ResponseChars,8} chars");
        return text.ToString();
    }

    /// <summary>
    /// Test output, plus — when CWM_SCALE_REPORT names a file — an appended plain-text report
    /// the CI job uploads, so numbers are visible even when every budget holds.
    /// </summary>
    private static void Report(string text)
    {
        TestContext.Current.TestOutputHelper?.WriteLine(text);
        if (Environment.GetEnvironmentVariable("CWM_SCALE_REPORT") is { Length: > 0 } path)
            File.AppendAllText(path, text + Environment.NewLine + Environment.NewLine);
    }

    private static async Task<ToolMeasurement> TimeAsync(
        WorkspaceManager workspace, string tool, Dictionary<string, object?> args, CancellationToken ct)
    {
        var method = ToolCatalog.Get(tool);
        var best = TimeSpan.MaxValue;
        var bytes = 0;

        for (var run = 0; run < 2; run++)
        {
            var timer = Stopwatch.StartNew();
            var json = await method.InvokeAsync(workspace, args, ct);
            timer.Stop();

            Assert.DoesNotContain("\"Error\"", json, StringComparison.Ordinal);
            best = timer.Elapsed < best ? timer.Elapsed : best;
            bytes = json.Length;
        }

        return new ToolMeasurement(tool, best, bytes);
    }
}
