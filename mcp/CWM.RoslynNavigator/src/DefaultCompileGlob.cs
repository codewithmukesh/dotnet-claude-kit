using System.Text.RegularExpressions;

namespace CWM.RoslynNavigator;

/// <summary>
/// Decides, without an MSBuild evaluation, whether a new .cs file is compiled by a project
/// purely through the SDK's default <c>**/*.cs</c> glob. When that is certain the file can be
/// added to the workspace directly; whenever it is not, the caller falls back to a full
/// reload, which asks MSBuild. Every check therefore errs towards "not certain".
/// </summary>
internal static partial class DefaultCompileGlob
{
    /// <summary>
    /// True when <paramref name="projectFilePath"/> is an SDK-style project and neither it nor
    /// any Directory.Build.props/.targets above it customizes compile items: no explicit
    /// <c>&lt;Compile Include/Remove&gt;</c>, no disabled default items, no changed default
    /// excludes, and no imports this check cannot follow.
    /// </summary>
    public static bool AppliesTo(string projectFilePath)
    {
        try
        {
            var project = File.ReadAllText(projectFilePath);
            if (!SdkStyle().IsMatch(project) || Customizes(project) || ImportsProject().IsMatch(project))
                return false;

            for (var dir = Path.GetDirectoryName(projectFilePath); dir is not null; dir = Path.GetDirectoryName(dir))
            {
                foreach (var name in (ReadOnlySpan<string>)["Directory.Build.props", "Directory.Build.targets"])
                {
                    var path = Path.Combine(dir, name);
                    if (File.Exists(path) && !IsPlainDirectoryBuildFile(File.ReadAllText(path)))
                        return false;
                }
            }

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// True when a file under <paramref name="projectDirectory"/> falls in the SDK's default
    /// item excludes: the bin/ and obj/ output folders, or any folder whose name starts with
    /// a dot (<c>**/.*/**</c>).
    /// </summary>
    public static bool IsDefaultExcluded(string projectDirectory, string filePath)
    {
        var relative = Path.GetRelativePath(projectDirectory, filePath);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (segments.Length > 1
            && (segments[0].Equals("bin", StringComparison.OrdinalIgnoreCase)
                || segments[0].Equals("obj", StringComparison.OrdinalIgnoreCase)))
            return true;

        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (segments[i].StartsWith('.'))
                return true;
        }

        return false;
    }

    private static bool Customizes(string msbuild) =>
        CompileItem().IsMatch(msbuild) || DefaultItemProperty().IsMatch(msbuild);

    /// <summary>
    /// A Directory.Build file that customizes nothing and imports nothing but the next one up
    /// (<c>GetPathOfFileAbove</c>) — which this walk visits anyway.
    /// </summary>
    private static bool IsPlainDirectoryBuildFile(string msbuild) =>
        !Customizes(msbuild)
        && ImportsProject().Matches(msbuild).All(m => FollowsChainUpward(msbuild, m.Index));

    private static bool FollowsChainUpward(string msbuild, int importStart)
    {
        var end = msbuild.IndexOf('>', importStart);
        var element = end < 0 ? msbuild[importStart..] : msbuild[importStart..end];
        return element.Contains("GetPathOfFileAbove", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex("""<Project\b[^>]*\bSdk\s*=|<Sdk\b|<Import\b[^>]*\bSdk\s*=""", RegexOptions.IgnoreCase)]
    private static partial Regex SdkStyle();

    /// <summary>A Compile item that adds or removes files. <c>Update</c> only sets metadata.</summary>
    [GeneratedRegex("""<Compile\b[^>]*\b(Include|Remove)\s*=""", RegexOptions.IgnoreCase)]
    private static partial Regex CompileItem();

    [GeneratedRegex("""<(EnableDefaultCompileItems|EnableDefaultItems|DefaultItemExcludes|DefaultItemExcludesInProjectFolder|BaseOutputPath|BaseIntermediateOutputPath|UseArtifactsOutput)\b""", RegexOptions.IgnoreCase)]
    private static partial Regex DefaultItemProperty();

    /// <summary>An Import of a file (not an SDK) — its contents are not followed here.</summary>
    [GeneratedRegex("""<Import\b(?![^>]*\bSdk\s*=)[^>]*\bProject\s*=""", RegexOptions.IgnoreCase)]
    private static partial Regex ImportsProject();
}
