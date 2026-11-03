using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Protocol;

namespace CWM.RoslynNavigator.Tests;

/// <summary>
/// A build-file change reloads in the background: tool calls keep answering from the last
/// good snapshot until the new one swaps in, concurrent triggers share one reload, a failed
/// reload keeps the old snapshot, and the replaced workspace is disposed.
/// </summary>
public sealed class BackgroundReloadTests : IDisposable
{
    private static readonly TimeSpan PastCooldown = TimeSpan.FromSeconds(10);

    private readonly string _root;
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly WorkspaceManager _workspace;

    public BackgroundReloadTests()
    {
        TestSolutionFixture.RegisterMSBuild();
        _root = Path.Combine(Path.GetTempPath(), "cwm-rn-bg-" + Guid.NewGuid().ToString("N"));
        TestDataCopy.CopyDirectory(Path.GetDirectoryName(TestSolutionFixture.SampleSolutionPath)!, _root);
        _workspace = TestWorkspace.Create(_clock);
    }

    private string SolutionPath => Path.Combine(_root, "SampleSolution.sln");
    private string ApiProjectPath => Path.Combine(_root, "SampleApi", "SampleApi.csproj");

    [Fact]
    public async Task ToolCalls_WhileReloadHeldOpen_AnswerFromPreviousSnapshot()
    {
        var ct = TestContext.Current.CancellationToken;
        await _workspace.LoadSolutionAsync(SolutionPath, ct);
        var version = _workspace.SnapshotVersion;
        var gate = HoldReloads();

        await EditProjectAsync(ApiProjectPath, "CS0219", ct);
        _clock.Advance(PastCooldown);
        Assert.Null(await _workspace.EnsureReadyOrStatusAsync(ct)); // starts the reload, does not wait
        await gate.Reached.WaitAsync(ct);

        var json = await FindSymbolTool.ExecuteAsync(_workspace, "OrderService", ct: ct);
        Assert.DoesNotContain("\"State\"", json);
        Assert.Contains("OrderService.cs", json);
        Assert.Equal(WorkspaceState.Ready, _workspace.State);
        Assert.True(_workspace.IsReloading);
        Assert.Equal(version, _workspace.SnapshotVersion);

        var status = await GetStatusAsync(ct);
        Assert.Equal("Ready", status.State);
        Assert.True(status.Reloading);
        Assert.Contains("SampleApi.csproj", status.ReloadReason);
        Assert.Contains("reloading in the background", status.Message);

        gate.Release();
        await _workspace.WaitForReloadAsync();

        Assert.False(_workspace.IsReloading);
        Assert.Equal(2, _workspace.LoadCount);
        Assert.True(_workspace.SnapshotVersion > version);
        Assert.Equal(ReportDiagnostic.Suppress, ApiProject().CompilationOptions!.SpecificDiagnosticOptions["CS0219"]);
    }

    [Fact]
    public async Task ToolResult_WhileReloading_CarriesStalenessWarning()
    {
        var ct = TestContext.Current.CancellationToken;
        await _workspace.LoadSolutionAsync(SolutionPath, ct);
        var gate = HoldReloads();

        await EditProjectAsync(ApiProjectPath, "CS0219", ct);
        _clock.Advance(PastCooldown);
        await _workspace.EnsureReadyOrStatusAsync(ct);
        await gate.Reached.WaitAsync(ct);

        var json = await FindSymbolTool.ExecuteAsync(_workspace, "OrderService", ct: ct);
        var result = IncompleteWorkspaceNote.Apply(
            new CallToolResult { Content = [new TextContentBlock { Text = json }] }, _workspace);

        using var annotated = JsonDocument.Parse(((TextContentBlock)result.Content[0]).Text);
        Assert.Contains("previous snapshot",
            annotated.RootElement.GetProperty(IncompleteWorkspaceNote.PropertyName).GetString());
        Assert.True(annotated.RootElement.GetProperty("TotalFound").GetInt32() > 0); // non-empty results are flagged too

        gate.Release();
        await _workspace.WaitForReloadAsync();
        Assert.Null(_workspace.StalenessWarning);
    }

    [Fact]
    public async Task RacingCallers_SameChange_CoalesceIntoOneReload()
    {
        var ct = TestContext.Current.CancellationToken;
        await _workspace.LoadSolutionAsync(SolutionPath, ct);
        var gate = HoldReloads();

        await EditProjectAsync(ApiProjectPath, "CS0219", ct);
        _clock.Advance(PastCooldown);

        // Two waves, the second past the refresh cooldown, while the first reload is held open:
        // neither may start a second reload of the change already being handled.
        var first = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ =>
            Task.Run(() => _workspace.EnsureReadyOrStatusAsync(ct), ct)));
        await gate.Reached.WaitAsync(ct);
        _clock.Advance(PastCooldown);
        var second = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ =>
            Task.Run(() => _workspace.EnsureReadyOrStatusAsync(ct), ct)));

        Assert.All(first.Concat(second), Assert.Null); // every call answered, none got a status
        gate.Release();
        await _workspace.WaitForReloadAsync();

        Assert.Equal(1, gate.Entered);
        Assert.Equal(2, _workspace.LoadCount);
    }

    [Fact]
    public async Task Swap_ReplacedWorkspace_IsDisposed()
    {
        var ct = TestContext.Current.CancellationToken;
        await _workspace.LoadSolutionAsync(SolutionPath, ct);
        Assert.Equal(1, _workspace.LiveWorkspaceCount);

        await ReloadAsync("CS0219", ct);
        Assert.Equal(1, _workspace.LiveWorkspaceCount);

        await ReloadAsync("CS0168", ct);
        Assert.Equal(1, _workspace.LiveWorkspaceCount);
        Assert.Equal(3, _workspace.LoadCount);
    }

    [Fact]
    public async Task Swap_WhileCallHoldsLease_DisposesOldWorkspaceAfterCallEnds()
    {
        var ct = TestContext.Current.CancellationToken;
        await _workspace.LoadSolutionAsync(SolutionPath, ct);
        var oldSolution = _workspace.GetSolution()!;

        var lease = _workspace.AcquireSnapshotLease();
        await ReloadAsync("CS0219", ct);

        // The in-flight call can still compile the snapshot it started on.
        Assert.Equal(2, _workspace.LiveWorkspaceCount);
        var compilation = await oldSolution.Projects.First(p => p.Name == "SampleDomain").GetCompilationAsync(ct);
        Assert.NotNull(compilation);

        lease!.Dispose();
        Assert.Equal(1, _workspace.LiveWorkspaceCount);
    }

    [Fact]
    public async Task FailedReload_KeepsPreviousSnapshotAndReportsError_ThenRecovers()
    {
        var ct = TestContext.Current.CancellationToken;
        await _workspace.LoadSolutionAsync(SolutionPath, ct);
        var version = _workspace.SnapshotVersion;
        var goodSolution = await File.ReadAllTextAsync(SolutionPath, ct);

        await File.WriteAllTextAsync(SolutionPath, "this is not a solution file", ct);
        await ReloadAsync("CS0219", ct);

        Assert.Equal(WorkspaceState.Ready, _workspace.State);
        Assert.Equal(version, _workspace.SnapshotVersion);
        Assert.Equal(1, _workspace.LoadCount);
        Assert.Equal(1, _workspace.LiveWorkspaceCount);
        Assert.NotNull(_workspace.LastReloadError);
        Assert.Contains("OrderService.cs", await FindSymbolTool.ExecuteAsync(_workspace, "OrderService", ct: ct));

        var status = await GetStatusAsync(ct);
        Assert.False(status.Healthy);
        Assert.Equal(_workspace.LastReloadError, status.LastReloadError);
        Assert.Contains("reload failed", _workspace.StalenessWarning);

        // Not retried inside the cooldown; retried once it has passed.
        await File.WriteAllTextAsync(SolutionPath, goodSolution, ct);
        _clock.Advance(PastCooldown);
        await _workspace.EnsureReadyOrStatusAsync(ct);
        await _workspace.WaitForReloadAsync();
        Assert.Equal(1, _workspace.LoadCount);

        _clock.Advance(TimeSpan.FromSeconds(31));
        await _workspace.EnsureReadyOrStatusAsync(ct);
        await _workspace.WaitForReloadAsync();

        Assert.Equal(2, _workspace.LoadCount);
        Assert.Null(_workspace.LastReloadError);
        Assert.True((await GetStatusAsync(ct)).Healthy);
    }

    [Fact]
    public async Task Refresh_BuildFileEditedDuringReload_IsPickedUpByTheNextReload()
    {
        var ct = TestContext.Current.CancellationToken;
        await _workspace.LoadSolutionAsync(SolutionPath, ct);
        var gate = HoldReloads();

        await EditProjectAsync(ApiProjectPath, "CS0219", ct, minutesAhead: 1);
        _clock.Advance(PastCooldown);
        await _workspace.EnsureReadyOrStatusAsync(ct);
        await gate.Reached.WaitAsync(ct);

        // Saved again after MSBuild evaluated the first edit.
        await EditProjectAsync(ApiProjectPath, "CS0168", ct, minutesAhead: 2);
        gate.Release();
        await _workspace.WaitForReloadAsync();
        Assert.Equal(2, _workspace.LoadCount);

        _workspace.BeforeSwap = null;
        _clock.Advance(PastCooldown);
        await _workspace.EnsureReadyOrStatusAsync(ct);
        await _workspace.WaitForReloadAsync();

        Assert.Equal(3, _workspace.LoadCount);
        Assert.True(ApiProject().CompilationOptions!.SpecificDiagnosticOptions.ContainsKey("CS0168"));
    }

    [Fact]
    public async Task DocumentEdit_WhileReloading_IsAppliedToCurrentSnapshot()
    {
        var ct = TestContext.Current.CancellationToken;
        await _workspace.LoadSolutionAsync(SolutionPath, ct);
        var gate = HoldReloads();

        await EditProjectAsync(ApiProjectPath, "CS0219", ct);
        _clock.Advance(PastCooldown);
        await _workspace.EnsureReadyOrStatusAsync(ct);
        await gate.Reached.WaitAsync(ct);

        var file = Path.Combine(_root, "SampleDomain", "UnusedHelper.cs");
        await File.AppendAllTextAsync(file, "\nnamespace SampleDomain { public sealed class AddedDuringReloadZq; }\n", ct);
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(5));
        _clock.Advance(PastCooldown);
        await _workspace.EnsureReadyOrStatusAsync(ct);

        Assert.Single(await SymbolResolver.FindSymbolsByNameAsync(_workspace, "AddedDuringReloadZq", ct: ct));

        gate.Release();
        await _workspace.WaitForReloadAsync();
        Assert.Single(await SymbolResolver.FindSymbolsByNameAsync(_workspace, "AddedDuringReloadZq", ct: ct));
    }

    private Project ApiProject() => _workspace.GetSolution()!.Projects.Single(p => p.Name == "SampleApi");

    private async Task ReloadAsync(string noWarn, CancellationToken ct)
    {
        await EditProjectAsync(ApiProjectPath, noWarn, ct);
        _clock.Advance(PastCooldown);
        await _workspace.EnsureReadyOrStatusAsync(ct);
        await _workspace.WaitForReloadAsync();
    }

    private async Task<WorkspaceStatusResult> GetStatusAsync(CancellationToken ct) =>
        JsonSerializer.Deserialize<WorkspaceStatusResult>(await GetWorkspaceStatusTool.ExecuteAsync(_workspace, ct: ct))!;

    internal static async Task EditProjectAsync(string projectPath, string noWarn, CancellationToken ct, int minutesAhead = 1)
    {
        var content = await File.ReadAllTextAsync(projectPath, ct);
        var marker = content.IndexOf("  <PropertyGroup><NoWarn>", StringComparison.Ordinal);
        if (marker >= 0) content = content[..marker] + "</Project>\n";
        await File.WriteAllTextAsync(
            projectPath,
            content.Replace("</Project>", $"  <PropertyGroup><NoWarn>{noWarn}</NoWarn></PropertyGroup>\n</Project>"),
            ct);
        File.SetLastWriteTimeUtc(projectPath, DateTime.UtcNow.AddMinutes(minutesAhead));
    }

    private ReloadGate HoldReloads()
    {
        var gate = new ReloadGate();
        _workspace.BeforeSwap = gate.WaitAsync;
        return gate;
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

    /// <summary>Holds every load at the swap until released, counting how many arrived.</summary>
    private sealed class ReloadGate
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _entered;

        public int Entered => Volatile.Read(ref _entered);

        public Task Reached => _reached.Task;

        public async Task WaitAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref _entered);
            _reached.TrySetResult();
            await _release.Task.WaitAsync(ct);
        }

        public void Release() => _release.TrySetResult();
    }
}
