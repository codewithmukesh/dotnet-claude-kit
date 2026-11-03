namespace CWM.RoslynNavigator.Tests;

public sealed class DefaultCompileGlobTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cwm-rn-glob-" + Guid.NewGuid().ToString("N"));

    public DefaultCompileGlobTests() => Directory.CreateDirectory(Path.Combine(_root, "App"));

    private string ProjectPath => Path.Combine(_root, "App", "App.csproj");

    [Fact]
    public void AppliesTo_PlainSdkProject_True()
    {
        File.WriteAllText(ProjectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup>
                <Compile Update="Generated.cs" DependentUpon="Source.cs" />
                <ProjectReference Include="..\Lib\Lib.csproj" />
              </ItemGroup>
            </Project>
            """);

        Assert.True(DefaultCompileGlob.AppliesTo(ProjectPath));
    }

    [Theory]
    [InlineData("""<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><Compile Remove="Legacy/**" /></ItemGroup></Project>""")]
    [InlineData("""<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><Compile Include="..\Shared\*.cs" /></ItemGroup></Project>""")]
    [InlineData("""<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup></Project>""")]
    [InlineData("""<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><DefaultItemExcludes>$(DefaultItemExcludes);gen/**</DefaultItemExcludes></PropertyGroup></Project>""")]
    [InlineData("""<Project Sdk="Microsoft.NET.Sdk"><Import Project="..\shared.props" /></Project>""")]
    [InlineData("""<Project ToolsVersion="15.0"><ItemGroup><Compile Include="A.cs" /></ItemGroup></Project>""")]
    public void AppliesTo_CustomizedOrLegacyProject_False(string project)
    {
        File.WriteAllText(ProjectPath, project);

        Assert.False(DefaultCompileGlob.AppliesTo(ProjectPath));
    }

    [Fact]
    public void AppliesTo_DirectoryBuildPropsRemovesCompileItems_False()
    {
        File.WriteAllText(ProjectPath, """<Project Sdk="Microsoft.NET.Sdk" />""");
        File.WriteAllText(Path.Combine(_root, "Directory.Build.props"),
            """<Project><ItemGroup><Compile Remove="**/*.Designer.cs" /></ItemGroup></Project>""");

        Assert.False(DefaultCompileGlob.AppliesTo(ProjectPath));
    }

    [Fact]
    public void AppliesTo_DirectoryBuildPropsChainsUpward_True()
    {
        File.WriteAllText(ProjectPath, """<Project Sdk="Microsoft.NET.Sdk" />""");
        File.WriteAllText(Path.Combine(_root, "App", "Directory.Build.props"), """
            <Project>
              <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />
              <PropertyGroup><Nullable>enable</Nullable></PropertyGroup>
            </Project>
            """);

        Assert.True(DefaultCompileGlob.AppliesTo(ProjectPath));
    }

    [Theory]
    [InlineData("bin/Debug/X.cs", true)]
    [InlineData("obj/X.cs", true)]
    [InlineData(".generated/X.cs", true)]
    [InlineData("Features/.cache/X.cs", true)]
    [InlineData("X.cs", false)]
    [InlineData("Features/Binary/X.cs", false)]
    [InlineData("Features/obj/X.cs", false)]
    public void IsDefaultExcluded_MatchesSdkDefaults(string relative, bool expected)
    {
        var projectDir = Path.Combine(_root, "App");

        Assert.Equal(expected, DefaultCompileGlob.IsDefaultExcluded(projectDir, Path.Combine(projectDir, relative)));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Temp leftovers are harmless.
        }
    }
}
