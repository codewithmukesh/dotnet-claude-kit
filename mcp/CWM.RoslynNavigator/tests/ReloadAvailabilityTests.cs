using System.Diagnostics;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CWM.RoslynNavigator.Tests;

/// <summary>
/// Availability under load: rounds of 32 parallel tool calls, each round racing a real csproj
/// edit, with no test seam holding anything open. Before reloads moved to the background,
/// this saw 10 of 192 calls return Loading, 30 calls block for over a second (up to 23 s), and
/// 31 loads for 6 edits because racing callers each started their own reload.
/// </summary>
public sealed class ReloadAvailabilityTests : IDisposable
{
    private const int Rounds = 6;
    private const int CallsPerRound = 32;

    private readonly string _root;
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly WorkspaceManager _workspace;

    public ReloadAvailabilityTests()
    {
        TestSolutionFixture.RegisterMSBuild();
        _root = Path.Combine(Path.GetTempPath(), "cwm-rn-avail-" + Guid.NewGuid().ToString("N"));
        BuildFileChangeDetectionTests.CopyDirectory(Path.GetDirectoryName(TestSolutionFixture.SampleSolutionPath)!, _root);
        _workspace = new WorkspaceManager(NullLoggerFactory.Instance.CreateLogger<WorkspaceManager>(), _clock);
    }

    [Fact]
    public async Task ParallelCalls_RacingCsprojEdits_AlwaysAnswerAndCoalesceReloads()
    {
        var ct = TestContext.Current.CancellationToken;
        await _workspace.LoadSolutionAsync(Path.Combine(_root, "SampleSolution.sln"), ct);
        var project = Path.Combine(_root, "SampleApi", "SampleApi.csproj");

        var answered = 0;
        var slow = 0;
        for (var round = 0; round < Rounds; round++)
        {
            await BackgroundReloadTests.EditProjectAsync(project, $"CS{round + 1:0000}", ct, minutesAhead: round + 1);
            _clock.Advance(TimeSpan.FromSeconds(10));

            var results = await Task.WhenAll(Enumerable.Range(0, CallsPerRound).Select(_ => Task.Run(async () =>
            {
                var started = Stopwatch.GetTimestamp();
                var json = await FindSymbolTool.ExecuteAsync(_workspace, "OrderService", ct: ct);
                return (Json: json, Elapsed: Stopwatch.GetElapsedTime(started));
            }, ct)));

            answered += results.Count(r => r.Json.Contains("OrderService.cs"));
            slow += results.Count(r => r.Elapsed > TimeSpan.FromSeconds(1));
        }

        // Latency is reported, not asserted: on a loaded CI agent an on-demand compilation can
        // legitimately exceed any fixed bound. Every call answering from a snapshot is the contract.
        TestContext.Current.SendDiagnosticMessage($"answered {answered}/{Rounds * CallsPerRound}, over 1s: {slow}");
        Assert.Equal(Rounds * CallsPerRound, answered);

        // Settle: whatever the last reload missed is picked up by the next one.
        await _workspace.WaitForReloadAsync();
        _clock.Advance(TimeSpan.FromSeconds(10));
        await _workspace.EnsureReadyOrStatusAsync(ct);
        await _workspace.WaitForReloadAsync();

        Assert.InRange(_workspace.LoadCount, 2, 1 + Rounds);
        Assert.Equal(1, _workspace.LiveWorkspaceCount);
        var options = _workspace.GetSolution()!.Projects.Single(p => p.Name == "SampleApi").CompilationOptions!;
        Assert.Equal(ReportDiagnostic.Suppress, options.SpecificDiagnosticOptions[$"CS{Rounds:0000}"]);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A locked file in a temp copy is not worth failing a test over.
        }
    }
}
