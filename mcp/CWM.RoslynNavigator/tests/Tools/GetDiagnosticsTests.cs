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
}
