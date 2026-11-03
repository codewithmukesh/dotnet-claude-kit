using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;

namespace CWM.RoslynNavigator.Tests.Tools;

/// <summary>
/// The blast radius of an interface implementation: calls to it and calls through the
/// interface member count, calls to sibling implementations do not. FindReferencesAsync
/// cascades up to the interface member and back down to every other implementation, which
/// on IDisposable.Dispose meant every Dispose in the solution.
/// </summary>
public sealed class AnalyzeChangeImpactCascadeTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"cwm-rn-impact-{Guid.NewGuid():N}");
    private WorkspaceManager _workspace = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        TestDataCopy.WriteGuardFiles(_root);
        Directory.CreateDirectory(Path.Combine(_root, "Jobs"));
        await File.WriteAllTextAsync(Path.Combine(_root, "Jobs", "Jobs.csproj"),
            """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>""");
        await File.WriteAllTextAsync(Path.Combine(_root, "Jobs", "Jobs.cs"), """
            namespace Jobs;

            public interface IJob { void Run(); }

            public sealed class Alpha : IJob { public void Run() { } }

            public sealed class Beta : IJob { public void Run() { } }

            public sealed class Scheduler
            {
                public void UseAlpha(Alpha alpha) => alpha.Run();
                public void UseBeta(Beta beta) => beta.Run();
                public void UseBetaAgain(Beta beta) => beta.Run();
                public void UseJob(IJob job) => job.Run();
                public void Tick(IJob job) => UseJob(job);
            }
            """);
        var solution = Path.Combine(_root, "Jobs.slnx");
        await File.WriteAllTextAsync(solution, """<Solution><Project Path="Jobs/Jobs.csproj" /></Solution>""");

        _workspace = TestWorkspace.Create(TimeProvider.System);
        await _workspace.LoadSolutionAsync(solution);
    }

    public ValueTask DisposeAsync()
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

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task AnalyzeChangeImpact_InterfaceImplementation_ExcludesSiblingImplementationCalls()
    {
        // Act
        var json = await AnalyzeChangeImpactTool.ExecuteAsync(_workspace, "Alpha.Run", ct: Ct);

        // Assert — alpha.Run() and job.Run() can reach Alpha.Run; beta.Run() cannot.
        var result = JsonSerializer.Deserialize<ChangeImpactResult>(json)!;
        Assert.Equal(2, result.DirectReferences);
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task AnalyzeChangeImpact_InterfaceImplementation_WalksCallersThroughInterface()
    {
        // Act
        var json = await AnalyzeChangeImpactTool.ExecuteAsync(_workspace, "Alpha.Run", depth: 2, ct: Ct);

        // Assert
        var result = JsonSerializer.Deserialize<ChangeImpactResult>(json)!;
        Assert.Equal(["Jobs.Scheduler.Tick", "Jobs.Scheduler.UseAlpha", "Jobs.Scheduler.UseJob"], result.TransitiveCallers.Order());
        Assert.Equal(1, result.IndirectCallers);
    }
}
