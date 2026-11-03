namespace CWM.RoslynNavigator.Tests.Fixtures;

/// <summary>
/// A throwaway copy of tests/TestData in the temp folder, for tests that mutate files or
/// need a solution no other test is loading. Copies SampleSolution and SecondSolution side
/// by side (SecondSolution references ../SampleSolution) plus the Directory.* guard files,
/// so the copy evaluates exactly like the original whatever the temp folder's ancestors hold.
/// Build output (bin/obj) is skipped.
/// </summary>
public sealed class TestDataCopy : IDisposable
{
    private TestDataCopy(string root) => Root = root;

    public string Root { get; }

    public string SampleRoot => Path.Combine(Root, "SampleSolution");

    public string SampleSolutionPath => Path.Combine(SampleRoot, "SampleSolution.sln");

    public string SecondSolutionPath => Path.Combine(Root, "SecondSolution", "SecondSolution.slnx");

    public static TestDataCopy Create(string prefix = "cwm-rn")
    {
        var root = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        var testData = Path.GetDirectoryName(Path.GetDirectoryName(TestSolutionFixture.SampleSolutionPath)!)!;

        CopyDirectory(Path.Combine(testData, "SampleSolution"), Path.Combine(root, "SampleSolution"));
        CopyDirectory(Path.Combine(testData, "SecondSolution"), Path.Combine(root, "SecondSolution"));
        WriteGuardFiles(root);

        return new TestDataCopy(root);
    }

    /// <summary>
    /// Empty Directory.Build.props/.targets and Directory.Packages.props stop MSBuild walking
    /// up into whatever the folder's ancestors contain.
    /// </summary>
    public static void WriteGuardFiles(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (var name in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props" })
            File.WriteAllText(Path.Combine(directory, name), "<Project />");
    }

    public static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        // Filter on the path relative to the source root. The source itself sits under the
        // test output directory, so an absolute-path check for "bin" matches everything.
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (IsBuildOutput(relative)) continue;

            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Temp folder; leaving it behind only costs disk space.
        }
    }

    private static bool IsBuildOutput(string relativePath) =>
        relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                            || segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
}
