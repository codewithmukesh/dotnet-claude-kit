using CWM.RoslynNavigator.Tests.Fixtures;

namespace CWM.RoslynNavigator.Tests;

/// <summary>
/// A multi-targeted project loads once per target framework. By default the workspace keeps
/// the primary framework only, unless another instance is still needed: referenced by a kept
/// project, or compiling a file the primary does not.
/// </summary>
public sealed class TargetFrameworkPruningTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cwm-rn-tfm-" + Guid.NewGuid().ToString("N"));
    private readonly WorkspaceManager _workspace;

    public TargetFrameworkPruningTests()
    {
        _workspace = TestWorkspace.Create(TimeProvider.System);
        TestDataCopy.WriteGuardFiles(_root);
    }

    [Fact]
    public async Task Load_MultiTargetedProject_KeepsHighestNetTargetOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        WriteLibrary(extraItems: "");
        WriteSolution("Lib/Lib.csproj");

        await _workspace.LoadSolutionAsync(SolutionPath, ct);

        var project = Assert.Single(_workspace.GetSolution()!.Projects);
        Assert.Equal("Lib(net10.0)", project.Name);
        Assert.Single(await SymbolResolver.FindSymbolsByNameAsync(_workspace, "WidgetZq", ct: ct));
    }

    [Fact]
    public async Task Load_MultiTargetedProject_ReportsDroppedInstancesAsDiagnostic()
    {
        var ct = TestContext.Current.CancellationToken;
        WriteLibrary(extraItems: "");
        WriteSolution("Lib/Lib.csproj");

        await _workspace.LoadSolutionAsync(SolutionPath, ct);

        Assert.Contains(_workspace.Health.GetDiagnostics(_workspace.GetSolution()),
            d => d.Kind == "warning" && d.Message.Contains("--all-target-frameworks", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Load_AllTargetFrameworks_KeepsEveryInstance()
    {
        var ct = TestContext.Current.CancellationToken;
        WriteLibrary(extraItems: "");
        WriteSolution("Lib/Lib.csproj");
        _workspace.AllTargetFrameworks = true;

        await _workspace.LoadSolutionAsync(SolutionPath, ct);

        Assert.Equal(["Lib(net10.0)", "Lib(netstandard2.0)"], _workspace.GetSolution()!.Projects.Select(p => p.Name).Order());
    }

    [Fact]
    public async Task Load_FileCompiledOnlyForSecondaryTarget_KeepsThatInstanceAndSaysWhy()
    {
        var ct = TestContext.Current.CancellationToken;
        WriteLibrary(extraItems: """<Compile Remove="Legacy.cs" Condition="'$(TargetFramework)' == 'net10.0'" />""");
        Write("Lib/Legacy.cs", "namespace Lib; public sealed class LegacyShimZq { }");
        WriteSolution("Lib/Lib.csproj");

        await _workspace.LoadSolutionAsync(SolutionPath, ct);

        Assert.Equal(["Lib(net10.0)", "Lib(netstandard2.0)"], _workspace.GetSolution()!.Projects.Select(p => p.Name).Order());
        Assert.Single(await SymbolResolver.FindSymbolsByNameAsync(_workspace, "LegacyShimZq", ct: ct));
        Assert.Contains(_workspace.Health.GetDiagnostics(_workspace.GetSolution()),
            d => d.Message.Contains("Lib(netstandard2.0)", StringComparison.Ordinal) && d.Message.Contains("Legacy.cs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Load_LowerTargetConsumer_KeepsTheInstanceItReferences()
    {
        var ct = TestContext.Current.CancellationToken;
        WriteLibrary(extraItems: "");
        Write("Consumer/Consumer.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>netstandard2.0</TargetFramework></PropertyGroup>
              <ItemGroup><ProjectReference Include="../Lib/Lib.csproj" /></ItemGroup>
            </Project>
            """);
        Write("Consumer/UsesWidget.cs", "namespace Consumer; public sealed class UsesWidgetZq { public Lib.WidgetZq? W; }");
        WriteSolution("Lib/Lib.csproj", "Consumer/Consumer.csproj");

        await _workspace.LoadSolutionAsync(SolutionPath, ct);

        var solution = _workspace.GetSolution()!;
        var consumer = Assert.Single(solution.Projects, p => p.Name == "Consumer");
        var referenced = Assert.Single(consumer.ProjectReferences);
        Assert.Equal("Lib(netstandard2.0)", solution.GetProject(referenced.ProjectId)!.Name);
        Assert.Contains(solution.Projects, p => p.Name == "Lib(net10.0)");
    }

    [Fact]
    public void Prune_PerFrameworkGeneratedFilesInArtifactsLayout_DropsSecondary()
    {
        using var adhoc = new Microsoft.CodeAnalysis.AdhocWorkspace();
        var root = Path.Combine(Path.GetTempPath(), "repo");
        var projectPath = Path.Combine(root, "src", "Lib", "Lib.csproj");
        var shared = Path.Combine(root, "src", "Lib", "Widget.cs");
        var solution = adhoc.CurrentSolution;
        foreach (var tfm in new[] { "net8.0", "net10.0" })
        {
            var id = Microsoft.CodeAnalysis.ProjectId.CreateNewId();
            var generated = Path.Combine(root, "artifacts", "obj", "Lib", $"debug_{tfm}", "Lib.GlobalUsings.g.cs");
            solution = solution
                .AddProject(Microsoft.CodeAnalysis.ProjectInfo.Create(id, Microsoft.CodeAnalysis.VersionStamp.Default,
                    $"Lib({tfm})", "Lib", Microsoft.CodeAnalysis.LanguageNames.CSharp, filePath: projectPath))
                .AddDocument(Microsoft.CodeAnalysis.DocumentId.CreateNewId(id), "Widget.cs", "class W { }", filePath: shared)
                .AddDocument(Microsoft.CodeAnalysis.DocumentId.CreateNewId(id), "G.cs", "", filePath: generated);
        }

        var result = TargetFrameworkPruner.Prune(solution);

        Assert.Equal(1, result.Dropped);
        Assert.Equal("Lib(net10.0)", Assert.Single(result.Solution.Projects).Name);
        Assert.Empty(result.KeptForCoverage);
    }

    [Theory]
    [InlineData("net10.0", "net8.0")]
    [InlineData("net8.0", "net10.0-windows")]
    [InlineData("net8.0", "netstandard2.1")]
    [InlineData("netstandard2.1", "netstandard2.0")]
    [InlineData("netstandard2.0", "net472")]
    [InlineData("netcoreapp3.1", "netstandard2.1")]
    [InlineData("net10.0-windows", "netcoreapp3.1")]
    public void Rank_PreferredFramework_RanksAboveOther(string preferred, string other)
    {
        Assert.True(TargetFrameworkPruner.Rank(preferred).CompareTo(TargetFrameworkPruner.Rank(other)) > 0);
    }

    [Fact]
    public void Rank_FrameworkNewerThanRuntime_RanksBelowSupportedOnes()
    {
        var future = $"net{Environment.Version.Major + 1}.0";

        Assert.True(TargetFrameworkPruner.Rank("net8.0").CompareTo(TargetFrameworkPruner.Rank(future)) > 0);
    }

    [Theory]
    [InlineData("Lib(net10.0)", "net10.0")]
    [InlineData("Lib", null)]
    [InlineData("My.Lib(netstandard2.0)", "netstandard2.0")]
    public void TargetFrameworkOf_ProjectName_ParsesDiscriminator(string name, string? expected)
    {
        Assert.Equal(expected, TargetFrameworkPruner.TargetFrameworkOf(name));
    }

    private string SolutionPath => Path.Combine(_root, "Multi.slnx");

    private void WriteLibrary(string extraItems)
    {
        Write("Lib/Lib.csproj", $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFrameworks>netstandard2.0;net10.0</TargetFrameworks></PropertyGroup>
              <ItemGroup>{extraItems}</ItemGroup>
            </Project>
            """);
        Write("Lib/Widget.cs", "namespace Lib; public sealed class WidgetZq { }");
    }

    private void WriteSolution(params string[] projects) =>
        Write("Multi.slnx", $"<Solution>{string.Concat(projects.Select(p => $"<Project Path=\"{p}\" />"))}</Solution>");

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
