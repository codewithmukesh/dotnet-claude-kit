using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CWM.RoslynNavigator.Tests.Fixtures;

/// <summary>
/// A throwaway solution in the temp folder that loads only partially: one project declared
/// in the solution does not exist, one compiles no documents, and one references a missing
/// project and a missing analyzer. MSBuildWorkspace opens it without throwing — exactly the
/// silent-partial-load case workspace health has to surface.
/// </summary>
public sealed class BrokenSolutionFixture : IAsyncLifetime
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "cwm-roslyn-navigator-tests", Guid.NewGuid().ToString("N"));

    public WorkspaceManager WorkspaceManager { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        TestSolutionFixture.RegisterMSBuild();
        Directory.CreateDirectory(_root);

        foreach (var name in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props" })
            await File.WriteAllTextAsync(Path.Combine(_root, name), "<Project />");

        var good = Directory.CreateDirectory(Path.Combine(_root, "Good")).FullName;
        await File.WriteAllTextAsync(Path.Combine(good, "Good.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="../Missing/Missing.csproj" />
                <Analyzer Include="../analyzers/NotThere.Generators.dll" />
              </ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(good, "Widget.cs"), """
            namespace Good;

            public sealed class Widget
            {
                public int Size { get; init; }
            }
            """);

        var empty = Directory.CreateDirectory(Path.Combine(_root, "Empty")).FullName;
        await File.WriteAllTextAsync(Path.Combine(empty, "Empty.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
              </PropertyGroup>
            </Project>
            """);

        var solutionPath = Path.Combine(_root, "Broken.slnx");
        await File.WriteAllTextAsync(solutionPath, """
            <Solution>
              <Project Path="Good/Good.csproj" />
              <Project Path="Empty/Empty.csproj" />
              <Project Path="Ghost/Ghost.csproj" />
            </Solution>
            """);

        WorkspaceManager = new WorkspaceManager(
            NullLoggerFactory.Instance.CreateLogger<WorkspaceManager>(),
            TimeProvider.System);
        await WorkspaceManager.LoadSolutionAsync(solutionPath);
        await WorkspaceManager.WaitForWarmUpAsync();
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
