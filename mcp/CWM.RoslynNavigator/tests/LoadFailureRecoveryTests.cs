using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CWM.RoslynNavigator.Tests;

/// <summary>
/// The first load failing, recovering, and being cancelled — and a solution whose projects
/// fail in three different ways while the healthy one still loads.
/// </summary>
public sealed class LoadFailureRecoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cwm-rn-fail-" + Guid.NewGuid().ToString("N"));
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly WorkspaceManager _workspace;

    public LoadFailureRecoveryTests()
    {
        TestSolutionFixture.RegisterMSBuild();
        _workspace = TestWorkspace.Create(_clock);
    }

    [Fact]
    public async Task CorruptPrimarySolution_ReportsError_ThenRecoversAfterFixAndCooldown()
    {
        var ct = TestContext.Current.CancellationToken;
        TestDataCopy.CopyDirectory(Path.GetDirectoryName(TestSolutionFixture.SampleSolutionPath)!, _root);
        var solution = Path.Combine(_root, "SampleSolution.sln");
        var good = await File.ReadAllTextAsync(solution, ct);
        await File.WriteAllTextAsync(solution, "this is not a solution file", ct);

        await Assert.ThrowsAnyAsync<Exception>(() => _workspace.LoadSolutionAsync(solution, ct));
        Assert.Equal(WorkspaceState.Error, _workspace.State);
        Assert.False(string.IsNullOrEmpty(_workspace.ErrorMessage));
        Assert.Null(_workspace.GetSolution());

        // First tool call retries at once (still broken) and reports the error gracefully.
        var status = JsonSerializer.Deserialize<StatusResponse>((await _workspace.EnsureReadyOrStatusAsync(ct))!)!;
        Assert.Equal("Error", status.State);
        Assert.Contains(_workspace.ErrorMessage!, status.Message);

        // Fixed, but inside the retry cooldown: no hammering.
        await File.WriteAllTextAsync(solution, good, ct);
        Assert.NotNull(await _workspace.EnsureReadyOrStatusAsync(ct));
        Assert.Equal(WorkspaceState.Error, _workspace.State);

        _clock.Advance(TimeSpan.FromSeconds(31));
        Assert.Null(await _workspace.EnsureReadyOrStatusAsync(ct));
        Assert.Equal(WorkspaceState.Ready, _workspace.State);
        Assert.Null(_workspace.ErrorMessage);
        Assert.Equal(4, _workspace.ProjectCount); // SampleSolution: Domain, Infrastructure, Api, Tests
        Assert.Equal(1, _workspace.LiveWorkspaceCount);
    }

    [Fact]
    public async Task FirstLoad_Cancelled_IsNotReportedAsError()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var reachedSwap = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _workspace.BeforeSwap = async token =>
        {
            reachedSwap.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        };

        var load = _workspace.LoadSolutionAsync(TestSolutionFixture.SampleSolutionPath, cts.Token);
        await reachedSwap.Task.WaitAsync(ct);
        Assert.Equal(WorkspaceState.Loading, _workspace.State);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => load);
        Assert.Equal(WorkspaceState.NotStarted, _workspace.State);
        Assert.Null(_workspace.ErrorMessage);
        Assert.Equal(0, _workspace.LiveWorkspaceCount);
    }

    [Fact]
    public async Task RetryAfterError_Cancelled_KeepsTheOriginalError()
    {
        var ct = TestContext.Current.CancellationToken;
        TestDataCopy.CopyDirectory(Path.GetDirectoryName(TestSolutionFixture.SampleSolutionPath)!, _root);
        var solution = Path.Combine(_root, "SampleSolution.sln");
        var good = await File.ReadAllTextAsync(solution, ct);
        await File.WriteAllTextAsync(solution, "this is not a solution file", ct);
        await Assert.ThrowsAnyAsync<Exception>(() => _workspace.LoadSolutionAsync(solution, ct));
        var error = _workspace.ErrorMessage;

        // The retry opens the fixed solution, then is cancelled before it can swap in.
        await File.WriteAllTextAsync(solution, good, ct);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _workspace.BeforeSwap = async token =>
        {
            await cts.CancelAsync();
            token.ThrowIfCancellationRequested();
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _workspace.LoadSolutionAsync(solution, cts.Token));

        Assert.Equal(WorkspaceState.Error, _workspace.State);
        Assert.Equal(error, _workspace.ErrorMessage);
        Assert.Equal(0, _workspace.LiveWorkspaceCount);
    }

    [Fact]
    public async Task DescribePrimary_UnparseableSolutionFile_FallsBackToLoadedProjects()
    {
        var ct = TestContext.Current.CancellationToken;
        using var adhoc = new AdhocWorkspace();
        var projectPath = Path.Combine(_root, "Lib", "Lib.csproj");
        var solution = adhoc.CurrentSolution.AddProject(Microsoft.CodeAnalysis.ProjectInfo.Create(
            ProjectId.CreateNewId(), VersionStamp.Default, "Lib", "Lib", LanguageNames.CSharp, filePath: projectPath));

        var described = await WorkspaceManager.DescribePrimaryAsync(
            Path.Combine(_root, "Gone.sln"), solution, NullLogger.Instance, ct);

        Assert.Null(described.Error);
        Assert.Equal([SolutionFileReader.NormalizePath(projectPath)], described.ProjectPaths);
    }

    [Fact]
    public async Task MixedFailures_HealthyProjectLoads_EachFailureReportedPerProject()
    {
        var ct = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(_root);
        foreach (var name in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props" })
            await File.WriteAllTextAsync(Path.Combine(_root, name), "<Project />", ct);

        await WriteProjectAsync("Healthy", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>", ct);
        await WriteProjectAsync("BadXml", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>", ct);
        await WriteProjectAsync("NoSdk", "<Project Sdk=\"Cwm.Does.Not.Exist.Sdk/1.0.0\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>", ct);
        var solution = Path.Combine(_root, "Mixed.slnx");
        await File.WriteAllTextAsync(solution, """
            <Solution>
              <Project Path="Healthy/Healthy.csproj" />
              <Project Path="BadXml/BadXml.csproj" />
              <Project Path="Missing/Missing.csproj" />
              <Project Path="NoSdk/NoSdk.csproj" />
            </Solution>
            """, ct);

        await _workspace.LoadSolutionAsync(solution, ct);
        await _workspace.WaitForWarmUpAsync();

        Assert.Equal(WorkspaceState.Ready, _workspace.State);
        Assert.Contains(_workspace.GetSolution()!.Projects, p => p.Name == "Healthy");
        Assert.Single(await SymbolResolver.FindSymbolsByNameAsync(_workspace, "HealthyThing", ct: ct));

        var json = await GetWorkspaceStatusTool.ExecuteAsync(_workspace, ct: ct);
        TestContext.Current.SendDiagnosticMessage(json);
        var status = JsonSerializer.Deserialize<WorkspaceStatusResult>(json)!;

        Assert.False(status.Healthy);
        Assert.DoesNotContain(status.IncompleteProjects, p => p.Name == "Healthy");
        Assert.Contains(Assert.Single(status.IncompleteProjects, p => p.Name == "Missing").Issues, i => i.Contains("file not found"));
        Assert.Contains(Assert.Single(status.IncompleteProjects, p => p.Name == "BadXml").Issues, i => i.Contains("load failure"));
        Assert.Contains(Assert.Single(status.IncompleteProjects, p => p.Name == "NoSdk").Issues, i => i.Contains("load failure"));
    }

    private async Task WriteProjectAsync(string name, string csproj, CancellationToken ct)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        await File.WriteAllTextAsync(Path.Combine(directory, $"{name}.csproj"), csproj, ct);
        await File.WriteAllTextAsync(Path.Combine(directory, $"{name}Thing.cs"), $"namespace {name};\n\npublic sealed class {name}Thing;\n", ct);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Temp folder; leaving it behind only costs disk space.
        }
    }
}
