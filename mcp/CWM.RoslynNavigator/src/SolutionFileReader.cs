using Microsoft.VisualStudio.SolutionPersistence.Serializer;

namespace CWM.RoslynNavigator;

/// <summary>
/// Lists the project files a .sln/.slnx declares, without evaluating them. Uses the same
/// parser MSBuildWorkspace uses internally (Microsoft.VisualStudio.SolutionPersistence, a
/// dependency of Microsoft.CodeAnalysis.Workspaces.MSBuild), so both formats behave identically.
/// </summary>
internal static class SolutionFileReader
{
    /// <summary>Project types Roslyn can load. Others (.esproj, .sqlproj, ...) are skipped,
    /// exactly as OpenSolutionAsync skips them.</summary>
    private static readonly HashSet<string> SupportedProjectExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".csproj", ".vbproj" };

    /// <summary>
    /// Absolute paths of the loadable projects in <paramref name="solutionPath"/>, in declaration
    /// order. Throws when the solution file itself cannot be parsed.
    /// </summary>
    public static async Task<IReadOnlyList<string>> GetProjectPathsAsync(string solutionPath, CancellationToken ct)
    {
        var serializer = SolutionSerializers.GetSerializerByMoniker(solutionPath)
            ?? throw new InvalidOperationException($"Unrecognized solution format: {solutionPath}");

        var model = await serializer.OpenAsync(solutionPath, ct);
        var solutionDir = Path.GetDirectoryName(Path.GetFullPath(solutionPath))!;

        return model.SolutionProjects
            .Select(p => NormalizePath(Path.Combine(solutionDir, p.FilePath)))
            .Where(p => SupportedProjectExtensions.Contains(Path.GetExtension(p)))
            .ToList();
    }

    /// <summary>
    /// Canonical form used to dedupe project files across solutions: absolute, OS separators.
    /// Compare with <see cref="StringComparer.OrdinalIgnoreCase"/>, as the rest of the workspace does.
    /// </summary>
    public static string NormalizePath(string path) =>
        Path.GetFullPath(path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar));
}
