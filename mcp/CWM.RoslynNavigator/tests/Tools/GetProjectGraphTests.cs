using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;

namespace CWM.RoslynNavigator.Tests.Tools;

[Collection(SampleSolutionCollection.Name)]
public class GetProjectGraphTests(TestSolutionFixture fixture)
{
    [Fact]
    public async Task GetProjectGraph_SampleSolution_ReturnsAllProjects()
    {
        var json = await GetProjectGraphTool.ExecuteAsync(fixture.WorkspaceManager, ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<ProjectGraphResult>(json)!;

        Assert.Equal(4, result.Projects!.Count); // Domain, Infrastructure, Api, Tests
        Assert.Contains(result.Projects, p => p.Name == "SampleDomain");
        Assert.Contains(result.Projects, p => p.Name == "SampleInfrastructure");
        Assert.Contains(result.Projects, p => p.Name == "SampleApi");
    }

    [Fact]
    public async Task GetProjectGraph_CorrectReferences_InfrastructureDependsOnDomain()
    {
        var json = await GetProjectGraphTool.ExecuteAsync(fixture.WorkspaceManager, ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<ProjectGraphResult>(json)!;

        var infra = result.Projects!.First(p => p.Name == "SampleInfrastructure");
        Assert.Contains("SampleDomain", infra.References!);
    }

    [Fact]
    public async Task GetProjectGraph_CorrectReferences_ApiDependsOnBoth()
    {
        var json = await GetProjectGraphTool.ExecuteAsync(fixture.WorkspaceManager, ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<ProjectGraphResult>(json)!;

        var api = result.Projects!.First(p => p.Name == "SampleApi");
        Assert.Contains("SampleDomain", api.References!);
        Assert.Contains("SampleInfrastructure", api.References!);
    }

    [Fact]
    public async Task GetProjectGraph_SolutionName_IsCorrect()
    {
        var json = await GetProjectGraphTool.ExecuteAsync(fixture.WorkspaceManager, ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<ProjectGraphResult>(json)!;

        Assert.Equal("SampleSolution.sln", result.Solution);
    }

    [Fact]
    public async Task GetProjectGraph_SingleSolution_OmitsMultiSolutionFields()
    {
        // Multi-solution support (#16) must not change single-solution output.
        var json = await GetProjectGraphTool.ExecuteAsync(fixture.WorkspaceManager, ct: TestContext.Current.CancellationToken);

        Assert.DoesNotContain("\"Solutions\"", json);
        Assert.Equal(
            Path.GetDirectoryName(TestSolutionFixture.SampleSolutionPath),
            fixture.WorkspaceManager.SolutionDirectory);
    }

    [Fact]
    public async Task GetProjectGraph_Paths_AreSolutionRelative()
    {
        // Act
        var json = await GetProjectGraphTool.ExecuteAsync(fixture.WorkspaceManager, ct: TestContext.Current.CancellationToken);

        // Assert
        var result = JsonSerializer.Deserialize<ProjectGraphResult>(json)!;
        Assert.Equal("SampleDomain/SampleDomain.csproj", result.Projects!.Single(p => p.Name == "SampleDomain").Path);
        Assert.All(result.Projects!, p => Assert.Equal(["net10.0"], p.TargetFrameworks));
    }

    [Fact]
    public async Task GetProjectGraph_NoProjectReferences_OmitsReferences()
    {
        // Act
        var json = await GetProjectGraphTool.ExecuteAsync(fixture.WorkspaceManager, ct: TestContext.Current.CancellationToken);

        // Assert
        var result = JsonSerializer.Deserialize<ProjectGraphResult>(json)!;
        Assert.Null(result.Projects!.Single(p => p.Name == "SampleDomain").References);
    }

    [Fact]
    public async Task GetProjectGraph_MaxResultsBelowTotal_TruncatesAndReportsTotal()
    {
        // Act
        var json = await GetProjectGraphTool.ExecuteAsync(fixture.WorkspaceManager, maxResults: 1, ct: TestContext.Current.CancellationToken);

        // Assert
        var result = JsonSerializer.Deserialize<ProjectGraphResult>(json)!;
        ToolGapTests.AssertTruncated(result.Projects!.Count, result.Count, result.TotalFound, result.Truncated, result.Limit, total: 4);
    }

    [Fact]
    public async Task GetProjectGraph_ProjectFilter_ReturnsMatchWithDependents()
    {
        // Act
        var json = await GetProjectGraphTool.ExecuteAsync(fixture.WorkspaceManager, project: "SampleDomain", ct: TestContext.Current.CancellationToken);

        // Assert
        var result = JsonSerializer.Deserialize<ProjectGraphResult>(json)!;
        var domain = Assert.Single(result.Projects!);
        Assert.Equal(["SampleApi", "SampleInfrastructure", "SampleTests"], domain.ReferencedBy!.Order());
    }

    [Fact]
    public async Task GetProjectGraph_UnknownProjectFilter_ReturnsInvalidArgument()
    {
        // Act
        var json = await GetProjectGraphTool.ExecuteAsync(fixture.WorkspaceManager, project: "NoSuchProject", ct: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ErrorCodes.InvalidArgument, JsonSerializer.Deserialize<ErrorResponse>(json)!.Error);
    }

    [Fact]
    public async Task GetProjectGraph_Summary_ReportsLayersAndMostReferenced()
    {
        // Act
        var json = await GetProjectGraphTool.ExecuteAsync(fixture.WorkspaceManager, summary: true, ct: TestContext.Current.CancellationToken);

        // Assert — Domain (0) ← Infrastructure (1) ← Api (2) ← Tests (3).
        var result = JsonSerializer.Deserialize<ProjectGraphSummaryResult>(json)!;
        Assert.Equal(4, result.Projects);
        Assert.Equal(1, result.TestProjects);
        Assert.Equal([0, 1, 2, 3], result.Layers.Select(l => l.Depth));
        Assert.Equal("SampleDomain", result.Layers[0].Examples.Single());
        Assert.Equal(new NamedCount("SampleDomain", 3), result.MostReferenced[0]);
        Assert.Equal(new NamedCount("net10.0", 4), Assert.Single(result.TargetFrameworks));
    }

    [Fact]
    public async Task GetProjectGraph_RestoredChain_ListsDirectReferencesOnly()
    {
        // Arrange — App → Lib → Core. Once restored, Roslyn compiles App against both Lib and
        // Core (transitive project references); the graph must show what App's csproj declares.
        var root = Path.Combine(Path.GetTempPath(), $"cwm-rn-graphchain-{Guid.NewGuid():N}");
        TestDataCopy.WriteGuardFiles(root);
        foreach (var (name, reference) in new[] { ("Core", (string?)null), ("Lib", "Core"), ("App", "Lib") })
        {
            Directory.CreateDirectory(Path.Combine(root, name));
            var item = reference is null ? "" : $"""<ItemGroup><ProjectReference Include="..\{reference}\{reference}.csproj" /></ItemGroup>""";
            await File.WriteAllTextAsync(Path.Combine(root, name, $"{name}.csproj"),
                $"""<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>{item}</Project>""",
                TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(root, name, $"{name}Type.cs"), $"namespace {name}; public class {name}Type;", TestContext.Current.CancellationToken);
        }

        var solutionPath = Path.Combine(root, "Chain.slnx");
        await File.WriteAllTextAsync(solutionPath,
            """<Solution><Project Path="Core/Core.csproj" /><Project Path="Lib/Lib.csproj" /><Project Path="App/App.csproj" /></Solution>""",
            TestContext.Current.CancellationToken);
        using (var restore = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("dotnet", $"restore \"{solutionPath}\"") { RedirectStandardOutput = true, RedirectStandardError = true })!)
        {
            await restore.WaitForExitAsync(TestContext.Current.CancellationToken);
        }

        using var workspace = TestWorkspace.Create(TimeProvider.System);
        try
        {
            await workspace.LoadSolutionAsync(solutionPath, TestContext.Current.CancellationToken);
            Assert.Equal(2, workspace.GetSolution()!.Projects.Single(p => p.Name == "App").ProjectReferences.Count());

            // Act
            var json = await GetProjectGraphTool.ExecuteAsync(workspace, ct: TestContext.Current.CancellationToken);

            // Assert
            var app = JsonSerializer.Deserialize<ProjectGraphResult>(json)!.Projects!.Single(p => p.Name == "App");
            Assert.Equal(["Lib"], app.References);
        }
        finally
        {
            workspace.Dispose();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
