using System.Text.Json;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FindSymbols;

namespace CWM.RoslynNavigator.Tests;

/// <summary>
/// A fresh clone after only <c>dotnet restore</c>: a project references an in-solution analyzer
/// project (<c>OutputItemType="Analyzer"</c>) that has never been built, so MSBuildWorkspace
/// hands Roslyn an <see cref="UnresolvedAnalyzerReference"/>. Roslyn's derived-type index
/// cannot checksum that reference and every implementation search threw. The workspace now
/// drops such references at load and reports them as incomplete instead.
/// </summary>
public sealed class UnresolvedAnalyzerReferenceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cwm-rn-unresolved-" + Guid.NewGuid().ToString("N"));
    private readonly WorkspaceManager _workspace;

    public UnresolvedAnalyzerReferenceTests()
    {
        _workspace = TestWorkspace.Create(TimeProvider.System);
        TestDataCopy.WriteGuardFiles(_root);

        Write("Gen/Gen.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        Write("Gen/Generator.cs", "namespace Gen; public sealed class Generator { }");

        Write("App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="../Gen/Gen.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        Write("App/Shapes.cs", """
            namespace App;
            public interface IShapeZq { double Area(); }
            public sealed class SquareZq : IShapeZq { public double Area() => 1; }
            public sealed class CircleZq : IShapeZq { public double Area() => 3.14; }
            """);

        Write("Fresh.slnx", """
            <Solution>
              <Project Path="Gen/Gen.csproj" />
              <Project Path="App/App.csproj" />
            </Solution>
            """);
    }

    private string SolutionPath => Path.Combine(_root, "Fresh.slnx");

    [Fact]
    public async Task Load_UnbuiltInSolutionAnalyzer_NoUnresolvedReferenceLeftInSnapshot()
    {
        var ct = TestContext.Current.CancellationToken;

        await _workspace.LoadSolutionAsync(SolutionPath, ct);

        var solution = _workspace.GetSolution()!;
        Assert.DoesNotContain(solution.Projects.SelectMany(p => p.AnalyzerReferences), r => r is UnresolvedAnalyzerReference);
    }

    [Fact]
    public async Task FindImplementationsAsync_UnbuiltInSolutionAnalyzer_ReturnsImplementations()
    {
        var ct = TestContext.Current.CancellationToken;
        await _workspace.LoadSolutionAsync(SolutionPath, ct);
        var solution = _workspace.GetSolution()!;
        var shape = Assert.IsAssignableFrom<INamedTypeSymbol>(
            Assert.Single(await SymbolResolver.FindSymbolsByNameAsync(_workspace, "IShapeZq", ct: ct)));

        var implementations = await SymbolFinder.FindImplementationsAsync(shape, solution, cancellationToken: ct);

        Assert.Equal(["CircleZq", "SquareZq"], implementations.Select(i => i.Name).Order());
    }

    [Fact]
    public async Task FindImplementationsTool_UnbuiltInSolutionAnalyzer_AnswersWithoutError()
    {
        var ct = TestContext.Current.CancellationToken;
        await _workspace.LoadSolutionAsync(SolutionPath, ct);

        var json = await FindImplementationsTool.ExecuteAsync(_workspace, "IShapeZq", ct: ct);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(2, doc.RootElement.GetProperty("TotalFound").GetInt32());
    }

    [Fact]
    public async Task GetProjectProblems_UnbuiltInSolutionAnalyzer_ReportsRemovedReference()
    {
        var ct = TestContext.Current.CancellationToken;
        await _workspace.LoadSolutionAsync(SolutionPath, ct);

        var problem = Assert.Single(_workspace.GetProjectProblems(), p => p.Name == "App");

        var issue = Assert.Single(problem.Issues, i => i.Contains("Gen.dll", StringComparison.Ordinal));
        Assert.Contains("analyzer/generator not found", issue, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetWorkspaceStatus_UnbuiltInSolutionAnalyzer_UnhealthyWithIncompleteAnalyzer()
    {
        var ct = TestContext.Current.CancellationToken;
        await _workspace.LoadSolutionAsync(SolutionPath, ct);

        var json = await GetWorkspaceStatusTool.ExecuteAsync(_workspace, ct: ct);

        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("Healthy").GetBoolean());
        Assert.Contains("Gen.dll", doc.RootElement.GetProperty("IncompleteProjects").GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reload_UnbuiltInSolutionAnalyzer_StillStrippedAndReported()
    {
        var ct = TestContext.Current.CancellationToken;
        await _workspace.LoadSolutionAsync(SolutionPath, ct);

        await _workspace.LoadSolutionAsync(SolutionPath, ct);

        Assert.Equal(2, _workspace.LoadCount);
        Assert.DoesNotContain(_workspace.GetSolution()!.Projects.SelectMany(p => p.AnalyzerReferences), r => r is UnresolvedAnalyzerReference);
        Assert.Contains(_workspace.GetProjectProblems(), p => p.Name == "App");
    }

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
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
