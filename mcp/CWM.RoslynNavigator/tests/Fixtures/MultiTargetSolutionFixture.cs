using Microsoft.Extensions.Logging.Abstractions;

namespace CWM.RoslynNavigator.Tests.Fixtures;

/// <summary>
/// A throwaway solution with one project multi-targeting net10.0 and net8.0, so the workspace
/// loads it twice ("Lib(net10.0)", "Lib(net8.0)") and every search sees each source location
/// once per framework — the duplication the tools must merge. One type is compiled only for
/// net8.0 (<c>#if NET8_0</c>) to pin the TargetFrameworks subset reporting.
/// </summary>
public sealed class MultiTargetSolutionFixture : IAsyncLifetime
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "cwm-rn-multitarget-" + Guid.NewGuid().ToString("N"));

    public WorkspaceManager WorkspaceManager { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        TestSolutionFixture.RegisterMSBuild();
        TestDataCopy.WriteGuardFiles(_root);

        var project = Directory.CreateDirectory(Path.Combine(_root, "Lib")).FullName;
        await File.WriteAllTextAsync(Path.Combine(project, "Lib.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net10.0;net8.0</TargetFrameworks>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
            </Project>
            """);

        await File.WriteAllTextAsync(Path.Combine(project, "Greeting.cs"), """
            namespace Lib;

            public interface IGreeter
            {
                string Greet(string name);
            }

            public class Greeter : IGreeter
            {
                public virtual string Greet(string name) => $"Hello {name}";
            }

            public sealed class LoudGreeter : Greeter
            {
                public override string Greet(string name) => base.Greet(name).ToUpperInvariant();
            }

            internal sealed class Reception(IGreeter greeter)
            {
                public string Welcome() => Decorate(greeter.Greet("guest"));

                private static string Decorate(string text) => $"*{text}*";

                private static int NeverCalled() => 42;

                public string Farewell() => greeter.Greet("friend") + " " + DateTime.Now;
            }

            #if NET8_0
            internal sealed class LegacyShim
            {
                public string Run(IGreeter greeter) => greeter.Greet("legacy");
            }
            #endif
            """);

        var solutionPath = Path.Combine(_root, "MultiTarget.slnx");
        await File.WriteAllTextAsync(solutionPath, """
            <Solution>
              <Project Path="Lib/Lib.csproj" />
            </Solution>
            """);

        WorkspaceManager = new WorkspaceManager(NullLogger<WorkspaceManager>.Instance, TimeProvider.System);
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
