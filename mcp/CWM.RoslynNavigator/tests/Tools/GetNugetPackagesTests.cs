using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;

namespace CWM.RoslynNavigator.Tests.Tools;

[Collection(SampleSolutionCollection.Name)]
public class GetNugetPackagesTests(TestSolutionFixture fixture)
{
    [Fact]
    public async Task GetNugetPackages_SampleSolution_ReturnsAllProjectsWithTfm()
    {
        var json = await GetNugetPackagesTool.ExecuteAsync(
            fixture.WorkspaceManager, ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<NugetPackagesResult>(json)!;

        Assert.Equal(4, result.Projects.Count); // Domain, Infrastructure, Api, Tests
        Assert.All(result.Projects, p => Assert.Equal("net10.0", p.TargetFramework));
    }

    [Fact]
    public async Task GetNugetPackages_SampleApi_HasLoggingAbstractionsWithVersion()
    {
        var json = await GetNugetPackagesTool.ExecuteAsync(
            fixture.WorkspaceManager, projectFilter: "SampleApi", ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<NugetPackagesResult>(json)!;

        var project = Assert.Single(result.Projects);
        // TestData's guard Directory.Packages.props stops the walk-up before this repo's own,
        // so the sample is a plain non-CPM project and the version comes from the csproj.
        Assert.False(project.Cpm);

        var package = Assert.Single(project.Packages);
        Assert.Equal("Microsoft.Extensions.Logging.Abstractions", package.Id);
        Assert.Equal("10.0.0", package.Version);
        Assert.Equal(1, result.TotalFound);
    }

    [Fact]
    public void ParseProjectPackages_Cpm_ResolvesVersionFromDirectoryPackagesProps()
    {
        var directory = Directory.CreateTempSubdirectory("cwm-nuget-test").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "Directory.Packages.props"),
                """
                <Project>
                  <PropertyGroup>
                    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageVersion Include="Serilog" Version="4.2.0" />
                  </ItemGroup>
                </Project>
                """);

            var csprojPath = Path.Combine(directory, "App.csproj");
            File.WriteAllText(csprojPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <PackageReference Include="Serilog" />
                    <PackageReference Include="Polly" VersionOverride="8.5.0" />
                  </ItemGroup>
                </Project>
                """);

            var (packages, cpm) = GetNugetPackagesTool.ParseProjectPackages(csprojPath);

            Assert.True(cpm);
            Assert.Equal(2, packages.Count);
            Assert.Equal("4.2.0", packages.Single(p => p.Id == "Serilog").Version);
            Assert.Equal("8.5.0", packages.Single(p => p.Id == "Polly").Version);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ParseProjectPackages_NoCpm_ReadsVersionAttribute()
    {
        var directory = Directory.CreateTempSubdirectory("cwm-nuget-test").FullName;
        try
        {
            var csprojPath = Path.Combine(directory, "App.csproj");
            File.WriteAllText(csprojPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <PackageReference Include="FluentValidation" Version="12.0.0" />
                    <PackageReference Include="NoVersionPackage" />
                  </ItemGroup>
                </Project>
                """);

            var (packages, cpm) = GetNugetPackagesTool.ParseProjectPackages(csprojPath);

            Assert.False(cpm);
            Assert.Equal("12.0.0", packages.Single(p => p.Id == "FluentValidation").Version);
            Assert.Null(packages.Single(p => p.Id == "NoVersionPackage").Version);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
