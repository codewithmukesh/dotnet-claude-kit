using CWM.RoslynNavigator.Tests.Fixtures;
using Microsoft.CodeAnalysis;

namespace CWM.RoslynNavigator.Tests;

/// <summary>
/// An edit to a project must be visible to every project that references it. Uses an
/// isolated copy of the sample solution: SampleApi references SampleDomain.
/// </summary>
public sealed class DependentProjectRefreshTests : IDisposable
{
    private readonly string _root;
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly WorkspaceManager _workspace;

    public DependentProjectRefreshTests()
    {
        TestSolutionFixture.RegisterMSBuild();

        _root = Path.Combine(Path.GetTempPath(), "cwm-rn-dep-" + Guid.NewGuid().ToString("N"));
        TestDataCopy.CopyDirectory(Path.GetDirectoryName(TestSolutionFixture.SampleSolutionPath)!, _root);

        _workspace = TestWorkspace.Create(_clock);
    }

    private string OrderPath => Path.Combine(_root, "SampleDomain", "Order.cs");

    [Fact]
    public async Task Refresh_DependencyEdited_DependentCompilationSeesTheChange()
    {
        var ct = TestContext.Current.CancellationToken;
        await _workspace.LoadSolutionAsync(Path.Combine(_root, "SampleSolution.sln"), ct);

        // Compile everything first, so a compilation of the dependent project built against
        // the old dependency exists to be (wrongly) reused.
        await _workspace.GetAllCompilationsAsync(ct);
        // The temp copy is not restored, so package-reference errors are expected; only
        // errors at the Order.Create call site matter here.
        Assert.DoesNotContain(await GetApiErrorsAsync(ct), IsCreateCallError);

        // SampleApi.OrderService calls Order.Create(). Renaming it in SampleDomain must break
        // SampleApi — the edit is in SampleDomain only, never in SampleApi itself.
        await EditOrderAsync(text => text.Replace("public static Order Create(", "public static Order CreateNew("), ct);

        _clock.Advance(TimeSpan.FromSeconds(10));
        await _workspace.RefreshChangedDocumentsAsync(ct);

        Assert.Contains(await GetApiErrorsAsync(ct), IsCreateCallError);

        var order = (await GetApiCompilationAsync(ct)).GetTypeByMetadataName("SampleDomain.Order");
        Assert.NotNull(order);
        Assert.NotEmpty(order.GetMembers("CreateNew"));
        Assert.Empty(order.GetMembers("Create"));
    }

    [Fact]
    public async Task Refresh_DependencyEdited_SymbolLookupReturnsTheNewMember()
    {
        var ct = TestContext.Current.CancellationToken;
        await _workspace.LoadSolutionAsync(Path.Combine(_root, "SampleSolution.sln"), ct);
        await _workspace.GetAllCompilationsAsync(ct);

        await EditOrderAsync(text => text.Replace("public void Ship()", "public void ShipExpressZq()"), ct);

        _clock.Advance(TimeSpan.FromSeconds(10));
        await _workspace.RefreshChangedDocumentsAsync(ct);

        Assert.Single(await SymbolResolver.FindSymbolsByNameAsync(_workspace, "ShipExpressZq", ct: ct));
        Assert.Empty(await SymbolResolver.FindSymbolsByNameAsync(_workspace, "Order.Ship", ct: ct));
    }

    /// <summary>An error reported at OrderService's call to Order.Create.</summary>
    private static bool IsCreateCallError(Diagnostic diagnostic) =>
        diagnostic.Location.GetLineSpan().Path.EndsWith("OrderService.cs", StringComparison.OrdinalIgnoreCase)
        && diagnostic.GetMessage().Contains("Create", StringComparison.Ordinal);

    private async Task EditOrderAsync(Func<string, string> edit, CancellationToken ct)
    {
        var text = await File.ReadAllTextAsync(OrderPath, ct);
        await File.WriteAllTextAsync(OrderPath, edit(text), ct);
        File.SetLastWriteTimeUtc(OrderPath, DateTime.UtcNow.AddMinutes(1));
    }

    private async Task<Compilation> GetApiCompilationAsync(CancellationToken ct)
    {
        var api = _workspace.GetSolution()!.Projects.Single(p => p.Name == "SampleApi");
        var compilation = await _workspace.GetCompilationAsync(api.Id, ct);
        Assert.NotNull(compilation);
        return compilation;
    }

    private async Task<IReadOnlyList<Diagnostic>> GetApiErrorsAsync(CancellationToken ct)
    {
        var compilation = await GetApiCompilationAsync(ct);
        return compilation.GetDiagnostics(ct)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
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
