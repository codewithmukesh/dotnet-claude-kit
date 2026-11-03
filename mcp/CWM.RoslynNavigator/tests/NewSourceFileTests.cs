using CWM.RoslynNavigator.Tests.Fixtures;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CWM.RoslynNavigator.Tests;

/// <summary>
/// A new .cs file in a project that compiles through the SDK default glob is added in place;
/// only projects that customize their compile items pay for a reload. Uses isolated copies of
/// the sample solutions. The clock starts at the real time because "was this file on disk
/// when MSBuild evaluated?" compares it with file-system timestamps.
/// </summary>
public sealed class NewSourceFileTests : IDisposable
{
    private static readonly TimeSpan PastCooldown = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PastStructuralCooldown = TimeSpan.FromSeconds(90);

    private readonly string _root;
    private readonly TestClock _clock = new(DateTimeOffset.UtcNow);
    private readonly WorkspaceManager _workspace;

    public NewSourceFileTests()
    {
        TestSolutionFixture.RegisterMSBuild();

        _root = Path.Combine(Path.GetTempPath(), "cwm-rn-new-" + Guid.NewGuid().ToString("N"));
        BuildFileChangeDetectionTests.CopyDirectory(
            Path.GetDirectoryName(TestSolutionFixture.SampleSolutionPath)!, Path.Combine(_root, "SampleSolution"));
        BuildFileChangeDetectionTests.CopyDirectory(
            Path.GetDirectoryName(TestSolutionFixture.SecondSolutionPath)!, Path.Combine(_root, "SecondSolution"));

        _workspace = new WorkspaceManager(NullLoggerFactory.Instance.CreateLogger<WorkspaceManager>(), _clock);
    }

    private string SampleRoot => Path.Combine(_root, "SampleSolution");
    private string SampleSolutionPath => Path.Combine(SampleRoot, "SampleSolution.sln");
    private string SecondSolutionPath => Path.Combine(_root, "SecondSolution", "SecondSolution.slnx");

    [Fact]
    public async Task Refresh_NewFileBesideExistingDocuments_AddedOnNextCallWithoutReload()
    {
        var ct = TestContext.Current.CancellationToken;
        await _workspace.LoadSolutionAsync(SampleSolutionPath, ct);
        var loads = _workspace.LoadCount;

        var path = await WriteTypeAsync(Path.Combine(SampleRoot, "SampleDomain", "Invoice.cs"), "SampleDomain", "InvoiceZq", ct);

        _clock.Advance(PastCooldown);
        await _workspace.RefreshChangedDocumentsAsync(ct);

        Assert.Single(await SymbolResolver.FindSymbolsByNameAsync(_workspace, "InvoiceZq", ct: ct));
        var document = Assert.Single(_workspace.GetSolution()!.Projects.SelectMany(p => p.Documents), d => d.FilePath == path);
        Assert.Equal("SampleDomain", document.Project.Name);
        Assert.Empty(document.Folders);
        Assert.Contains(path, _workspace.KnownDocumentPaths, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(loads, _workspace.LoadCount);
    }

    [Fact]
    public async Task Refresh_NewFileInNewFolder_AddedAfterBackgroundWalkWithFolders()
    {
        var ct = TestContext.Current.CancellationToken;
        await _workspace.LoadSolutionAsync(SampleSolutionPath, ct);
        var loads = _workspace.LoadCount;

        var path = await WriteTypeAsync(
            Path.Combine(SampleRoot, "SampleDomain", "Billing", "Tax", "TaxRule.cs"), "SampleDomain.Billing.Tax", "TaxRuleZq", ct);

        await RefreshThroughBackgroundWalkAsync(ct);

        Assert.Single(await SymbolResolver.FindSymbolsByNameAsync(_workspace, "TaxRuleZq", ct: ct));
        var document = Assert.Single(_workspace.GetSolution()!.Projects.SelectMany(p => p.Documents), d => d.FilePath == path);
        Assert.Equal(["Billing", "Tax"], document.Folders);
        Assert.Equal(loads, _workspace.LoadCount);
    }

    [Fact]
    public async Task Refresh_NewFileThenEdited_EditIsPickedUp()
    {
        var ct = TestContext.Current.CancellationToken;
        await _workspace.LoadSolutionAsync(SampleSolutionPath, ct);

        var path = await WriteTypeAsync(Path.Combine(SampleRoot, "SampleDomain", "Invoice.cs"), "SampleDomain", "InvoiceZq", ct);
        _clock.Advance(PastCooldown);
        await _workspace.RefreshChangedDocumentsAsync(ct);

        await WriteTypeAsync(path, "SampleDomain", "InvoiceRenamedZq", ct);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        _clock.Advance(PastCooldown);
        await _workspace.RefreshChangedDocumentsAsync(ct);

        Assert.Empty(await SymbolResolver.FindSymbolsByNameAsync(_workspace, "InvoiceZq", ct: ct));
        Assert.Single(await SymbolResolver.FindSymbolsByNameAsync(_workspace, "InvoiceRenamedZq", ct: ct));
    }

    [Fact]
    public async Task Refresh_NewFileInProjectWithCompileRemove_FallsBackToReload()
    {
        var ct = TestContext.Current.CancellationToken;
        await AddCompileRemoveAsync("Legacy/**", ct);
        await _workspace.LoadSolutionAsync(SampleSolutionPath, ct);
        var loads = _workspace.LoadCount;

        await WriteTypeAsync(
            Path.Combine(SampleRoot, "SampleInfrastructure", "AuditLog.cs"), "SampleInfrastructure", "AuditLogZq", ct);

        _clock.Advance(PastCooldown);
        await _workspace.RefreshChangedDocumentsAsync(ct);

        Assert.Equal(loads + 1, _workspace.LoadCount);
        Assert.Single(await SymbolResolver.FindSymbolsByNameAsync(_workspace, "AuditLogZq", ct: ct));
    }

    [Fact]
    public async Task Refresh_FileTheProjectExcluded_NeverTriggersAReload()
    {
        var ct = TestContext.Current.CancellationToken;
        await AddCompileRemoveAsync("Legacy/**", ct);
        var excluded = await WriteTypeAsync(
            Path.Combine(SampleRoot, "SampleInfrastructure", "Legacy", "OldRepo.cs"), "SampleInfrastructure", "OldRepoZq", ct);
        MakeOld(excluded);

        await _workspace.LoadSolutionAsync(SampleSolutionPath, ct);
        var loads = _workspace.LoadCount;

        // Two full cycles: before, every background walk re-found the file and reloaded.
        await RefreshThroughBackgroundWalkAsync(ct);
        await RefreshThroughBackgroundWalkAsync(ct);

        Assert.Equal(loads, _workspace.LoadCount);
        Assert.Empty(await SymbolResolver.FindSymbolsByNameAsync(_workspace, "OldRepoZq", ct: ct));
    }

    [Fact]
    public async Task Refresh_NewFilesUnderObjOrDotFolder_Ignored()
    {
        var ct = TestContext.Current.CancellationToken;
        await _workspace.LoadSolutionAsync(SampleSolutionPath, ct);
        var loads = _workspace.LoadCount;

        await WriteTypeAsync(Path.Combine(SampleRoot, "SampleDomain", "obj", "Stray.cs"), "SampleDomain", "StrayZq", ct);
        await WriteTypeAsync(Path.Combine(SampleRoot, "SampleDomain", ".scratch", "Hidden.cs"), "SampleDomain", "HiddenZq", ct);

        await RefreshThroughBackgroundWalkAsync(ct);
        await RefreshThroughBackgroundWalkAsync(ct);

        Assert.Equal(loads, _workspace.LoadCount);
        Assert.Empty(await SymbolResolver.FindSymbolsByNameAsync(_workspace, "StrayZq", ct: ct));
        Assert.Empty(await SymbolResolver.FindSymbolsByNameAsync(_workspace, "HiddenZq", ct: ct));
    }

    [Fact]
    public async Task Refresh_NewFileInSecondSolution_AddedToItsProjectWithoutReload()
    {
        var ct = TestContext.Current.CancellationToken;
        await _workspace.LoadSolutionsAsync([SampleSolutionPath, SecondSolutionPath], ct);
        var loads = _workspace.LoadCount;

        var path = await WriteTypeAsync(
            Path.Combine(_root, "SecondSolution", "ShippingService", "Carrier.cs"), "ShippingService", "CarrierZq", ct);

        _clock.Advance(PastCooldown);
        await _workspace.RefreshChangedDocumentsAsync(ct);

        var document = Assert.Single(_workspace.GetSolution()!.Projects.SelectMany(p => p.Documents), d => d.FilePath == path);
        Assert.Equal("ShippingService", document.Project.Name);
        Assert.Single(await SymbolResolver.FindSymbolsByNameAsync(_workspace, "CarrierZq", ct: ct));
        Assert.Equal(loads, _workspace.LoadCount);
        Assert.Equal(2, _workspace.LoadedSolutions.Count);
    }

    private async Task RefreshThroughBackgroundWalkAsync(CancellationToken ct)
    {
        _clock.Advance(PastStructuralCooldown);
        await _workspace.RefreshChangedDocumentsAsync(ct); // schedules the walk
        await _workspace.WaitForStructuralScanAsync();
        _clock.Advance(PastCooldown);
        await _workspace.RefreshChangedDocumentsAsync(ct); // acts on what it found
    }

    private async Task AddCompileRemoveAsync(string pattern, CancellationToken ct)
    {
        var projectPath = Path.Combine(SampleRoot, "SampleInfrastructure", "SampleInfrastructure.csproj");
        var content = await File.ReadAllTextAsync(projectPath, ct);
        await File.WriteAllTextAsync(
            projectPath,
            content.Replace("</Project>", $"  <ItemGroup><Compile Remove=\"{pattern}\" /></ItemGroup>\n</Project>"),
            ct);
    }

    private static async Task<string> WriteTypeAsync(string path, string ns, string typeName, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, $"namespace {ns};\n\npublic sealed class {typeName};\n", ct);
        return path;
    }

    /// <summary>Back-dates a file so it reads as already on disk when MSBuild evaluated.</summary>
    private static void MakeOld(string path)
    {
        var past = DateTime.UtcNow.AddHours(-1);
        File.SetCreationTimeUtc(path, past);
        File.SetLastWriteTimeUtc(path, past);
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
