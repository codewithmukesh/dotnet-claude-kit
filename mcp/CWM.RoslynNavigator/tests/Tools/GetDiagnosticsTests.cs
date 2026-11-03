using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;

namespace CWM.RoslynNavigator.Tests.Tools;

[Collection(SampleSolutionCollection.Name)]
public class GetDiagnosticsTests(TestSolutionFixture fixture)
{
    [Fact]
    public async Task GetDiagnostics_SolutionScope_ReturnsDiagnostics()
    {
        var json = await GetDiagnosticsTool.ExecuteAsync(fixture.WorkspaceManager, scope: "solution", ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<DiagnosticsResult>(json)!;

        // The sample solution has an intentional unused variable in ProductService
        // which should generate a CS0219 warning
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetDiagnostics_FileScope_FiltersToSpecificFile()
    {
        var json = await GetDiagnosticsTool.ExecuteAsync(
            fixture.WorkspaceManager,
            scope: "file",
            path: "ProductService.cs",
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<DiagnosticsResult>(json)!;

        // ProductService.cs has the intentional unused variable
        Assert.NotNull(result);
    }

    [Theory]
    [InlineData("Missing.cs")]
    [InlineData("./SampleApi/Missing.cs")]
    public async Task GetDiagnostics_FileScope_UnmatchedPath_ReturnsFileNotFound(string path)
    {
        var json = await GetDiagnosticsTool.ExecuteAsync(
            fixture.WorkspaceManager,
            scope: "file",
            path: path,
            ct: TestContext.Current.CancellationToken);
        var error = JsonSerializer.Deserialize<ErrorResponse>(json)!;

        Assert.Equal(ErrorCodes.FileNotFound, error.Error);
    }

    [Theory]
    [InlineData("./SampleApi/ProductService.cs")]
    [InlineData(".\\SampleApi\\ProductService.cs")]
    public async Task GetDiagnostics_FileScope_DotSlashAndBackslashPaths_Resolve(string path)
    {
        var json = await GetDiagnosticsTool.ExecuteAsync(
            fixture.WorkspaceManager,
            scope: "file",
            path: path,
            ct: TestContext.Current.CancellationToken);

        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty("Error", out _));
        var result = JsonSerializer.Deserialize<DiagnosticsResult>(json)!;
        Assert.All(result.Diagnostics, d => Assert.EndsWith("SampleApi/ProductService.cs", d.File));
    }

    [Fact]
    public async Task GetDiagnostics_SeverityFilter_Error_FiltersCorrectly()
    {
        var json = await GetDiagnosticsTool.ExecuteAsync(
            fixture.WorkspaceManager,
            scope: "solution",
            severityFilter: "error",
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<DiagnosticsResult>(json)!;

        // All returned diagnostics should be errors
        Assert.All(result.Diagnostics, d => Assert.Equal("error", d.Severity));
    }

    [Fact]
    public async Task GetDiagnostics_ProjectScope_FiltersToProject()
    {
        var json = await GetDiagnosticsTool.ExecuteAsync(
            fixture.WorkspaceManager,
            scope: "project",
            path: "SampleDomain",
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<DiagnosticsResult>(json)!;

        // SampleDomain should have clean compilations (no intentional issues)
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetDiagnostics_FileScope_MatchesSolutionScopeForEveryFile()
    {
        // Arrange — file scope answers from the file's semantic model (or, for files that
        // can carry compilation-end diagnostics, from the compilation); it must agree with the
        // solution-wide answer filtered to that file.
        var ct = TestContext.Current.CancellationToken;
        var all = JsonSerializer.Deserialize<DiagnosticsResult>(await GetDiagnosticsTool.ExecuteAsync(
            fixture.WorkspaceManager, scope: "solution", maxResults: 10_000, ct: ct))!;
        var files = fixture.WorkspaceManager.GetSolution()!.Projects
            .SelectMany(p => p.Documents)
            .Select(d => fixture.WorkspaceManager.ToRelativePath(d.FilePath!))
            .Where(p => !p.Contains("/obj/", StringComparison.Ordinal))
            .Distinct()
            .ToList();

        foreach (var file in files)
        {
            // Act
            var single = JsonSerializer.Deserialize<DiagnosticsResult>(await GetDiagnosticsTool.ExecuteAsync(
                fixture.WorkspaceManager, scope: "file", path: file, maxResults: 10_000, ct: ct))!;

            // Assert
            Assert.Equal(
                all.Diagnostics.Where(d => d.File == file).Select(d => $"{d.Id}:{d.Line}:{d.Message}").Order(),
                single.Diagnostics.Select(d => $"{d.Id}:{d.Line}:{d.Message}").Order());
        }
    }

    [Theory]
    [InlineData("class C { private int _unused; }", true)]
    [InlineData("class C { internal event System.Action? Changed; }", true)]
    [InlineData("class C { static void Main() { } }", true)]
    [InlineData("System.Console.WriteLine();", true)]
    [InlineData("class C { public int Value; protected int Other; private const int K = 1; }", false)]
    [InlineData("class C { void M() { var unused = 1; } }", false)]
    public void NeedsCompilationDiagnostics_Declarations_FlagsOnlyCompilationEndShapes(string source, bool expected)
    {
        var root = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)
            .GetRoot(TestContext.Current.CancellationToken);

        Assert.Equal(expected, GetDiagnosticsTool.NeedsCompilationDiagnostics(root));
    }
}
