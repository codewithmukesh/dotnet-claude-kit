using System.Diagnostics;
using System.Text.Json;
using CWM.RoslynNavigator.Tests.Fixtures;

namespace CWM.RoslynNavigator.Tests;

/// <summary>
/// TestData lives inside the navigator's own source tree, under its Directory.Packages.props.
/// Without the guard files in tests/TestData the sample projects inherit that file and load
/// with central package management ON — a configuration no real user solution has, which
/// let tests pass against a build context that differs from the one they claim to model.
/// </summary>
[Collection(SampleSolutionCollection.Name)]
public sealed class TestDataIsolationTests(TestSolutionFixture fixture)
{
    public static TheoryData<string, string> SampleProjects => new()
    {
        { "SampleSolution", "SampleApi/SampleApi.csproj" },
        { "SampleSolution", "SampleDomain/SampleDomain.csproj" },
        { "SampleSolution", "SampleInfrastructure/SampleInfrastructure.csproj" },
        { "SecondSolution", "ShippingService/ShippingService.csproj" },
    };

    [Theory]
    [MemberData(nameof(SampleProjects))]
    public async Task Evaluate_SampleProject_ImportsTestDataGuardsAndLeavesCpmOff(string solution, string project)
    {
        // Arrange
        var solutionDirectory = Path.GetDirectoryName(solution == "SampleSolution"
            ? TestSolutionFixture.SampleSolutionPath
            : TestSolutionFixture.SecondSolutionPath)!;
        var projectPath = Path.Combine(solutionDirectory, project);
        var testDataRoot = Path.GetDirectoryName(solutionDirectory)!;

        // Act
        var properties = await GetMSBuildPropertiesAsync(projectPath,
            "ManagePackageVersionsCentrally", "DirectoryPackagesPropsPath", "DirectoryBuildPropsPath");

        // Assert
        Assert.Equal("", properties["ManagePackageVersionsCentrally"]);
        Assert.Equal(Path.Combine(testDataRoot, "Directory.Packages.props"), properties["DirectoryPackagesPropsPath"]);
        Assert.Equal(Path.Combine(testDataRoot, "Directory.Build.props"), properties["DirectoryBuildPropsPath"]);
    }

    [Fact]
    public async Task LoadSolution_SampleSolution_RecordsNoCentralPackageDiagnostics()
    {
        // Arrange
        var workspace = fixture.WorkspaceManager;
        await workspace.WaitForWarmUpAsync();

        // Act
        var loadDiagnostics = workspace.Health.GetDiagnostics(workspace.GetSolution())
            .Select(d => d.Message);
        var compileDiagnostics = (await workspace.GetAllCompilationsAsync(TestContext.Current.CancellationToken))
            .SelectMany(c => c.GetDiagnostics(TestContext.Current.CancellationToken))
            .Select(d => $"{d.Id}: {d.GetMessage()}");

        // Assert
        Assert.DoesNotContain(loadDiagnostics.Concat(compileDiagnostics), IsCentralPackageDiagnostic);
    }

    private static bool IsCentralPackageDiagnostic(string message) =>
        message.Contains("NU1008", StringComparison.Ordinal)
        || message.Contains("NU1009", StringComparison.Ordinal)
        || message.Contains("NU1010", StringComparison.Ordinal)
        || message.Contains("central package", StringComparison.OrdinalIgnoreCase)
        || message.Contains("Directory.Packages.props", StringComparison.OrdinalIgnoreCase);

    private static async Task<Dictionary<string, string>> GetMSBuildPropertiesAsync(
        string projectPath, params string[] names)
    {
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var startInfo = new ProcessStartInfo(dotnet)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("-nologo");
        foreach (var name in names)
            startInfo.ArgumentList.Add($"-getProperty:{name}");

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        Assert.True(process.ExitCode == 0, $"dotnet msbuild failed: {await stderr}{await stdout}");

        using var json = JsonDocument.Parse(await stdout);
        return json.RootElement.GetProperty("Properties").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
    }
}
