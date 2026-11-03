using CWM.RoslynNavigator.Tests.Fixtures;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CWM.RoslynNavigator.Tests;

/// <summary>
/// A background reload builds a second MSBuildWorkspace beside the first. Both must go through
/// the shadow-copying host (#25): generators keep running in the new snapshot and the original
/// analyzer file stays unlocked after the swap.
/// </summary>
public sealed class ShadowCopyReloadTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "cwm-rn-gen-reload-" + Guid.NewGuid().ToString("N"));
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly WorkspaceManager _workspace;

    public ShadowCopyReloadTests()
    {
        TestSolutionFixture.RegisterMSBuild();
        _workspace = TestWorkspace.Create(_clock);
    }

    private string GeneratorPath => Path.Combine(_root, "analyzers", "Microsoft.Extensions.Logging.Generators.dll");
    private string ProjectPath => Path.Combine(_root, "GenDemo", "GenDemo.csproj");

    [Fact]
    public async Task Reload_ProjectWithSourceGenerator_KeepsGeneratingAndLeavesAnalyzerUnlocked()
    {
        var ct = TestContext.Current.CancellationToken;
        var solutionPath = await CreateGeneratorSolutionAsync(ct);
        await _workspace.LoadSolutionAsync(solutionPath, ct);
        Assert.NotNull((await GetHelloAsync(ct)).PartialImplementationPart);

        await BackgroundReloadTests.EditProjectAsync(ProjectPath, "CS0219", ct);
        _clock.Advance(TimeSpan.FromSeconds(10));
        await _workspace.EnsureReadyOrStatusAsync(ct);
        await _workspace.WaitForReloadAsync();

        Assert.Equal(2, _workspace.LoadCount);
        Assert.NotNull((await GetHelloAsync(ct)).PartialImplementationPart);
        var exception = Record.Exception(() =>
        {
            using var stream = new FileStream(GeneratorPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        });
        Assert.Null(exception);
    }

    private async Task<IMethodSymbol> GetHelloAsync(CancellationToken ct)
    {
        var project = _workspace.GetSolution()!.Projects.Single();
        var compilation = await _workspace.GetCompilationAsync(project.Id, ct);
        return compilation!.GetTypeByMetadataName("GenDemo.Log")!.GetMembers("Hello").OfType<IMethodSymbol>().Single();
    }

    private async Task<string> CreateGeneratorSolutionAsync(CancellationToken ct)
    {
        var analyzers = Directory.CreateDirectory(Path.Combine(_root, "analyzers")).FullName;
        foreach (var file in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "TestAnalyzers"), "*.dll"))
            File.Copy(file, Path.Combine(analyzers, Path.GetFileName(file)));

        foreach (var name in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props" })
            await File.WriteAllTextAsync(Path.Combine(_root, name), "<Project />", ct);

        Directory.CreateDirectory(Path.GetDirectoryName(ProjectPath)!);
        await File.WriteAllTextAsync(ProjectPath, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <Reference Include="{typeof(LoggerMessageAttribute).Assembly.Location}" />
                <Analyzer Include="{GeneratorPath}" />
              </ItemGroup>
            </Project>
            """, ct);
        await File.WriteAllTextAsync(Path.Combine(_root, "GenDemo", "Log.cs"), """
            using Microsoft.Extensions.Logging;

            namespace GenDemo;

            public static partial class Log
            {
                [LoggerMessage(Level = LogLevel.Information, Message = "Hello {Name}")]
                public static partial void Hello(ILogger logger, string name);
            }
            """, ct);

        var solutionPath = Path.Combine(_root, "GenDemo.slnx");
        await File.WriteAllTextAsync(solutionPath, """
            <Solution>
              <Project Path="GenDemo/GenDemo.csproj" />
            </Solution>
            """, ct);
        return solutionPath;
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
