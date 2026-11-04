using Microsoft.Extensions.Logging.Abstractions;

namespace CWM.RoslynNavigator.Tests.Fixtures;

/// <summary>
/// ABP's shape in miniature: a library multi-targeting net10.0 and net8.0, consumed by one
/// net8.0 project and one net10.0 project. Each consumer compiles against a different flavor of
/// the library, so its types derive from a different Roslyn symbol for the same base class. A
/// search seeded with one flavor's symbol misses the other consumer (ABP: 3 of hundreds of
/// AbpModule subclasses). Loaded the default way, which keeps the flavors consumers need.
/// </summary>
public sealed class CrossFlavorSolutionFixture : IAsyncLifetime
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "cwm-rn-crossflavor-" + Guid.NewGuid().ToString("N"));

    public WorkspaceManager WorkspaceManager { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        TestSolutionFixture.RegisterMSBuild();
        TestDataCopy.WriteGuardFiles(_root);

        await WriteProjectAsync("Lib", "net10.0;net8.0", references: [], "Modules.cs", """
            namespace Lib;

            public interface IModule
            {
                void Configure();
            }

            public abstract class ModuleBase : IModule
            {
                public virtual void Configure() { }
            }

            public sealed class CoreModule : ModuleBase;
            """);

        await WriteProjectAsync("Legacy", "net8.0", references: ["Lib"], "LegacyModule.cs", """
            namespace Legacy;

            public sealed class LegacyModule : Lib.ModuleBase
            {
                public override void Configure() { }

                public static void Run(Lib.ModuleBase module) => module.Configure();
            }
            """);

        await WriteProjectAsync("Modern", "net10.0", references: ["Lib"], "ModernModule.cs", """
            namespace Modern;

            public sealed class ModernModule : Lib.ModuleBase
            {
                public override void Configure() { }

                public static void Run(Lib.ModuleBase module) => module.Configure();
            }
            """);

        var solutionPath = Path.Combine(_root, "CrossFlavor.slnx");
        await File.WriteAllTextAsync(solutionPath, """
            <Solution>
              <Project Path="Lib/Lib.csproj" />
              <Project Path="Legacy/Legacy.csproj" />
              <Project Path="Modern/Modern.csproj" />
            </Solution>
            """);

        WorkspaceManager = new WorkspaceManager(NullLogger<WorkspaceManager>.Instance, TimeProvider.System);
        await WorkspaceManager.LoadSolutionAsync(solutionPath);
        await WorkspaceManager.WaitForWarmUpAsync();
    }

    private async Task WriteProjectAsync(string name, string frameworks, string[] references, string file, string source)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        var property = frameworks.Contains(';') ? "TargetFrameworks" : "TargetFramework";
        var items = string.Concat(references.Select(r => $"    <ProjectReference Include=\"../{r}/{r}.csproj\" />\n"));

        await File.WriteAllTextAsync(Path.Combine(directory, $"{name}.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <{property}>{frameworks}</{property}>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
              <ItemGroup>
            {items}  </ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(directory, file), source);
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
