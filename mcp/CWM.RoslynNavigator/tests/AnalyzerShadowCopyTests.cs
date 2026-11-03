using CWM.RoslynNavigator.Tests.Fixtures;
using Microsoft.CodeAnalysis;

namespace CWM.RoslynNavigator.Tests;

/// <summary>
/// End-to-end coverage for #25: analyzers and source generators must load from shadow copies,
/// so the original files stay writable, while generated code keeps resolving.
/// </summary>
public sealed class AnalyzerShadowCopyTests(GeneratorSolutionFixture fixture) : IClassFixture<GeneratorSolutionFixture>
{
    [Fact]
    public async Task GetCompilationAsync_ProjectWithNuGetSourceGenerator_ResolvesGeneratedSymbol()
    {
        var hello = await GetHelloAsync();

        // The declaration is hand-written; its implementation only exists if the generator ran.
        Assert.True(hello.IsPartialDefinition);
        Assert.NotNull(hello.PartialImplementationPart);
    }

    [Fact]
    public async Task GetCompilationAsync_AfterGeneratorRuns_OriginalAnalyzerFileIsNotLocked()
    {
        var hello = await GetHelloAsync();
        Assert.NotNull(hello.PartialImplementationPart); // Proves the generator assembly was loaded.

        // An exclusive read/write open is what NuGet effectively needs to overwrite the file.
        // On Windows it fails while any process has the assembly mapped; elsewhere it passes trivially.
        var exception = Record.Exception(() =>
        {
            using var stream = new FileStream(fixture.GeneratorPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        });

        Assert.Null(exception);
    }

    [Fact]
    public void AnalyzerReferences_AfterLoad_ReportOriginalPath()
    {
        var project = fixture.WorkspaceManager.GetSolution()!.Projects.Single();

        // Shadow copying is an implementation detail; consumers still see the real location.
        Assert.Contains(project.AnalyzerReferences,
            reference => string.Equals(reference.FullPath, fixture.GeneratorPath, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<IMethodSymbol> GetHelloAsync()
    {
        var project = fixture.WorkspaceManager.GetSolution()!.Projects.Single();
        var compilation = await fixture.WorkspaceManager.GetCompilationAsync(project.Id, TestContext.Current.CancellationToken);

        var log = compilation!.GetTypeByMetadataName("GenDemo.Log");
        Assert.NotNull(log);
        return log.GetMembers("Hello").OfType<IMethodSymbol>().Single();
    }
}
