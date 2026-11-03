using CWM.RoslynNavigator.Tests.Fixtures;

namespace CWM.RoslynNavigator.Tests;

/// <summary>
/// A source file deleted on disk must leave the workspace — before, its symbols lingered for
/// the life of the process. Uses an isolated copy of the sample solution.
/// </summary>
public sealed class DocumentDeletionTests : IDisposable
{
    private static readonly TimeSpan PastCooldown = TimeSpan.FromSeconds(10);

    private readonly string _root;
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly WorkspaceManager _workspace;

    public DocumentDeletionTests()
    {
        TestSolutionFixture.RegisterMSBuild();

        _root = Path.Combine(Path.GetTempPath(), "cwm-rn-del-" + Guid.NewGuid().ToString("N"));
        TestDataCopy.CopyDirectory(Path.GetDirectoryName(TestSolutionFixture.SampleSolutionPath)!, _root);

        _workspace = TestWorkspace.Create(_clock);
    }

    [Fact]
    public async Task Refresh_DocumentDeleted_RemovedWithoutReload()
    {
        var ct = TestContext.Current.CancellationToken;
        await _workspace.LoadSolutionAsync(Path.Combine(_root, "SampleSolution.sln"), ct);
        var loadsAfterInitial = _workspace.LoadCount;
        Assert.Single(await SymbolResolver.FindSymbolsByNameAsync(_workspace, "UnusedHelper", ct: ct));

        var path = Path.Combine(_root, "SampleDomain", "UnusedHelper.cs");
        File.Delete(path);

        _clock.Advance(PastCooldown);
        await _workspace.RefreshChangedDocumentsAsync(ct);

        Assert.Empty(await SymbolResolver.FindSymbolsByNameAsync(_workspace, "UnusedHelper", ct: ct));
        Assert.DoesNotContain(_workspace.GetSolution()!.Projects.SelectMany(p => p.Documents), d => d.Name == "UnusedHelper.cs");
        Assert.DoesNotContain(path, _workspace.KnownDocumentPaths, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(loadsAfterInitial, _workspace.LoadCount);
    }

    [Fact]
    public async Task Refresh_DocumentDeletedAndEditedTogether_BothApplied()
    {
        var ct = TestContext.Current.CancellationToken;
        await _workspace.LoadSolutionAsync(Path.Combine(_root, "SampleSolution.sln"), ct);

        File.Delete(Path.Combine(_root, "SampleDomain", "UnusedHelper.cs"));
        var orderPath = Path.Combine(_root, "SampleDomain", "Order.cs");
        var text = await File.ReadAllTextAsync(orderPath, ct);
        await File.WriteAllTextAsync(orderPath, text.Replace("public void Ship()", "public void ShipLaterZq()"), ct);
        File.SetLastWriteTimeUtc(orderPath, DateTime.UtcNow.AddMinutes(1));

        _clock.Advance(PastCooldown);
        await _workspace.RefreshChangedDocumentsAsync(ct);

        Assert.Empty(await SymbolResolver.FindSymbolsByNameAsync(_workspace, "UnusedHelper", ct: ct));
        Assert.Single(await SymbolResolver.FindSymbolsByNameAsync(_workspace, "ShipLaterZq", ct: ct));
    }

    [Fact]
    public async Task Refresh_DocumentFolderDeleted_AllItsDocumentsRemoved()
    {
        var ct = TestContext.Current.CancellationToken;
        await _workspace.LoadSolutionAsync(Path.Combine(_root, "SampleSolution.sln"), ct);
        Assert.NotEmpty(await SymbolResolver.FindSymbolsByNameAsync(_workspace, "InitialCreate", ct: ct));

        Directory.Delete(Path.Combine(_root, "SampleApi", "Migrations"), recursive: true);

        _clock.Advance(PastCooldown);
        await _workspace.RefreshChangedDocumentsAsync(ct);

        Assert.Empty(await SymbolResolver.FindSymbolsByNameAsync(_workspace, "InitialCreate", ct: ct));
    }

    [Fact]
    public async Task Refresh_NothingChanged_SolutionSnapshotUntouched()
    {
        var ct = TestContext.Current.CancellationToken;
        await _workspace.LoadSolutionAsync(Path.Combine(_root, "SampleSolution.sln"), ct);
        var before = _workspace.GetSolution();

        _clock.Advance(PastCooldown);
        await _workspace.RefreshChangedDocumentsAsync(ct);

        Assert.Same(before, _workspace.GetSolution());
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
