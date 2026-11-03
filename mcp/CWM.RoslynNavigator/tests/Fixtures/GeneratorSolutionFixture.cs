using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CWM.RoslynNavigator.Tests.Fixtures;

/// <summary>
/// Builds a throwaway one-project solution in the temp folder that runs a real NuGet source
/// generator (Microsoft.Extensions.Logging's [LoggerMessage] generator), then loads it through
/// <see cref="WorkspaceManager"/>. The generator is copied into the solution folder so the
/// "original" analyzer file belongs to the test and can be probed for locks safely.
/// </summary>
public sealed class GeneratorSolutionFixture : IAsyncLifetime
{
    private const string GeneratorFileName = "Microsoft.Extensions.Logging.Generators.dll";

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "cwm-roslyn-navigator-tests", Guid.NewGuid().ToString("N"));

    public WorkspaceManager WorkspaceManager { get; private set; } = null!;

    /// <summary>The analyzer path the project references — the file that must never be locked.</summary>
    public string GeneratorPath => Path.Combine(_root, "analyzers", GeneratorFileName);

    public async ValueTask InitializeAsync()
    {
        TestSolutionFixture.RegisterMSBuild();

        var analyzerSource = Path.Combine(AppContext.BaseDirectory, "TestAnalyzers");
        var analyzerTarget = Directory.CreateDirectory(Path.Combine(_root, "analyzers")).FullName;
        foreach (var file in Directory.EnumerateFiles(analyzerSource, "*.dll"))
            File.Copy(file, Path.Combine(analyzerTarget, Path.GetFileName(file)));

        // Empty Directory.* files stop MSBuild from walking up into whatever the temp folder's
        // ancestors contain.
        foreach (var name in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props" })
            await File.WriteAllTextAsync(Path.Combine(_root, name), "<Project />");

        var abstractionsPath = typeof(LoggerMessageAttribute).Assembly.Location;
        var projectDirectory = Directory.CreateDirectory(Path.Combine(_root, "GenDemo")).FullName;

        await File.WriteAllTextAsync(Path.Combine(projectDirectory, "GenDemo.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <Reference Include="{abstractionsPath}" />
                <Analyzer Include="{GeneratorPath}" />
              </ItemGroup>
            </Project>
            """);

        await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Log.cs"), """
            using Microsoft.Extensions.Logging;

            namespace GenDemo;

            public static partial class Log
            {
                [LoggerMessage(Level = LogLevel.Information, Message = "Hello {Name}")]
                public static partial void Hello(ILogger logger, string name);
            }
            """);

        var solutionPath = Path.Combine(_root, "GenDemo.slnx");
        await File.WriteAllTextAsync(solutionPath, """
            <Solution>
              <Project Path="GenDemo/GenDemo.csproj" />
            </Solution>
            """);

        WorkspaceManager = new WorkspaceManager(
            NullLoggerFactory.Instance.CreateLogger<WorkspaceManager>(),
            TimeProvider.System);
        await WorkspaceManager.LoadSolutionAsync(solutionPath);
    }

    public ValueTask DisposeAsync()
    {
        WorkspaceManager?.Dispose();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Temp folder; leaving it behind only costs disk space.
        }

        return ValueTask.CompletedTask;
    }
}
