using CWM.RoslynNavigator.Caching;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;
using System.Text.Json;

namespace CWM.RoslynNavigator.Tests.Caching;

/// <summary>
/// A private copy of the sample solutions and a load cache folder inside it, so cache tests
/// never touch the user's real cache (%LOCALAPPDATA%) or the shared TestData. Every
/// <see cref="WorkspaceManager"/> it creates uses that cache and is disposed with it.
/// </summary>
internal sealed class LoadCacheTestbed : IDisposable
{
    private readonly List<WorkspaceManager> _workspaces = [];

    public LoadCacheTestbed()
    {
        TestSolutionFixture.RegisterMSBuild();
        Data = TestDataCopy.Create("cwm-rn-cache");
        Store = new LoadCacheStore(Path.Combine(Data.Root, "load-cache"));
    }

    public TestDataCopy Data { get; }

    public LoadCacheStore Store { get; }

    public string SampleRoot => Data.SampleRoot;

    public string SolutionPath => Data.SampleSolutionPath;

    public LoadCacheHeader Header => LoadCacheStore.CreateHeader([SolutionPath], allTargetFrameworks: false);

    /// <summary>A workspace using this testbed's cache (or <paramref name="store"/>).</summary>
    public WorkspaceManager CreateWorkspace(LoadCacheStore? store = null, TimeProvider? clock = null)
    {
        var workspace = TestWorkspace.Create(clock ?? TimeProvider.System);
        workspace.LoadCache = store ?? Store;
        lock (_workspaces) _workspaces.Add(workspace);
        return workspace;
    }

    /// <summary>Loads <paramref name="solutionPaths"/> (default: the sample) and waits until its cache entry is written.</summary>
    public async Task<WorkspaceManager> LoadAndCacheAsync(CancellationToken ct, params string[] solutionPaths)
    {
        var workspace = CreateWorkspace();
        await workspace.LoadSolutionsAsync(solutionPaths.Length > 0 ? solutionPaths : [SolutionPath], ct);
        await workspace.WaitForCacheWriteAsync();
        return workspace;
    }

    public static async Task<WorkspaceStatusResult> GetStatusAsync(WorkspaceManager workspace, CancellationToken ct) =>
        JsonSerializer.Deserialize<WorkspaceStatusResult>(await GetWorkspaceStatusTool.ExecuteAsync(workspace, ct: ct))!;

    public string SamplePath(params string[] segments) => Path.Combine([SampleRoot, .. segments]);

    public void Dispose()
    {
        foreach (var workspace in _workspaces)
            workspace.Dispose();
        Data.Dispose();
    }
}

/// <summary>
/// Holds a load (or a cache verification) just before it would compare or swap, until
/// released — so a test can observe the server while MSBuild is still "running".
/// </summary>
internal sealed class SwapGate
{
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static SwapGate Attach(WorkspaceManager workspace)
    {
        var gate = new SwapGate();
        workspace.BeforeSwap = gate.WaitAsync;
        return gate;
    }

    public Task Reached => _reached.Task;

    public void Release() => _release.TrySetResult();

    private async Task WaitAsync(CancellationToken ct)
    {
        _reached.TrySetResult();
        await _release.Task.WaitAsync(ct);
    }
}
