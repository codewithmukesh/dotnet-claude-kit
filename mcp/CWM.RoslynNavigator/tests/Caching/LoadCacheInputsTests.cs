using CWM.RoslynNavigator.Caching;
using Microsoft.CodeAnalysis;

namespace CWM.RoslynNavigator.Tests.Caching;

/// <summary>Which files key a cache entry, and how they are fingerprinted, without loading MSBuild.</summary>
public sealed class LoadCacheInputsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("cwm-rn-inputs-").FullName;

    [Fact]
    public void ReadImports_StaticPaths_ResolvedAgainstTheImportingFile()
    {
        // Arrange
        var props = Write("eng/Directory.Build.props", """
            <Project>
              <Import Project="Versions.props" />
              <Import Project="$(MSBuildThisFileDirectory)..\common\Common.targets" Condition="Exists('x')" />
              <Import Project="$(RepoRoot)Skipped.props" />
              <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />
              <Import Project="Wild*.props" />
            </Project>
            """);

        // Act
        var imports = LoadCacheInputs.ReadImports(props).ToList();

        // Assert: only what can be resolved without evaluating MSBuild.
        Assert.Equal(
            [Path.Combine(_root, "eng", "Versions.props"), Path.Combine(_root, "common", "Common.targets")],
            imports);
    }

    [Fact]
    public void Compute_ArtifactsLayout_FindsRestoreOutputOutsideTheProjectFolder()
    {
        // Arrange: UseArtifactsOutput puts obj/ under artifacts/, not beside the project.
        var project = Write("src/Lib/Lib.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var generated = Write("artifacts/obj/Lib/debug/Lib.AssemblyInfo.cs", "");
        var assets = Write("artifacts/obj/Lib/project.assets.json", "{}");
        var snapshot = Snapshot(project, generated);

        // Act
        var inputs = LoadCacheInputs.Compute(snapshot, []);

        // Assert
        Assert.Contains(inputs, i => i.Path == assets && i.Fingerprint.StartsWith("stamp:", StringComparison.Ordinal));
        Assert.Contains(inputs, i => i.Path == Path.Combine(_root, "src", "Lib", "obj", "project.assets.json") && i.Fingerprint == "absent");
        Assert.Contains(inputs, i => i.Path == project && i.Fingerprint.StartsWith("sha256:", StringComparison.Ordinal));
    }

    [Fact]
    public void FirstDifference_RestoreOutputRewritten_NamesIt()
    {
        // Arrange
        var project = Write("Lib/Lib.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var assets = Write("Lib/obj/project.assets.json", """{ "version": 3, "libraries": { "Pkg/1.0.0": {} } }""");
        var snapshot = Snapshot(project);
        var before = LoadCacheInputs.Compute(snapshot, []);

        // Act: a restore that resolved Pkg 1.1.0
        File.WriteAllText(assets, """{ "version": 3, "libraries": { "Pkg/1.1.0": {} } }""");
        File.SetLastWriteTimeUtc(assets, DateTime.UtcNow.AddSeconds(5));
        var difference = LoadCacheInputs.FirstDifference(before, LoadCacheInputs.Compute(snapshot, []));

        // Assert
        Assert.Equal($"{assets} changed", difference);
    }

    [Fact]
    public void FirstDifference_SameFiles_IsNull()
    {
        // Arrange
        var snapshot = Snapshot(Write("Lib/Lib.csproj", "<Project />"));

        // Act
        var difference = LoadCacheInputs.FirstDifference(LoadCacheInputs.Compute(snapshot, []), LoadCacheInputs.Compute(snapshot, []));

        // Assert
        Assert.Null(difference);
    }

    private string Write(string relativePath, string content)
    {
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static CachedSnapshot Snapshot(string projectPath, params string[] documents) =>
        new([], [],
        [
            new CachedProject(
                "Lib", "Lib", LanguageNames.CSharp, projectPath, null, null, null, null, null, null, null,
                [.. documents.Select(d => new CachedDocument(d, null, [], SourceCodeKind.Regular))],
                [], [], [], [], []),
        ]);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
