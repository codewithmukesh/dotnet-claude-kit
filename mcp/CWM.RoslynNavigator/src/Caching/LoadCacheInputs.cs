using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace CWM.RoslynNavigator.Caching;

/// <summary>
/// The files a cached load depends on, and their fingerprints. A cache entry is used only when
/// every fingerprint still matches, so this list decides when a cached snapshot is stale.
///
/// <para>What MSBuild reads that can change what a project loads:</para>
/// <list type="bullet">
/// <item>the solution files (which projects exist);</item>
/// <item>every project file;</item>
/// <item>Directory.Build.props/.targets, Directory.Packages.props, global.json and NuGet.config
/// in every folder from each project up to the drive root (the same set the server watches
/// for reloads, <see cref="WorkspaceManager.EnumerateSharedBuildFiles"/>);</item>
/// <item>any .props/.targets those files import by a path that can be resolved without
/// evaluating MSBuild — a literal relative path or one rooted at
/// <c>$(MSBuildThisFileDirectory)</c> — followed transitively;</item>
/// <item>each project's restore output, <c>project.assets.json</c>: a changed package version
/// changes the references even when no project file changes.</item>
/// </list>
/// <para>Imports through other properties, environment variables, and SDK workloads are not
/// followed. That gap is bounded: every cached start is verified by a real MSBuild load in the
/// background, which swaps in the correct snapshot if anything differed.</para>
/// </summary>
internal static partial class LoadCacheInputs
{
    private const string Absent = "absent";
    private const string AssetsFileName = "project.assets.json";

    /// <summary>
    /// Fingerprints of every input of a load of <paramref name="solutionPaths"/> that produced
    /// <paramref name="snapshot"/>, sorted by path.
    /// </summary>
    public static List<InputFingerprint> Compute(CachedSnapshot snapshot, IReadOnlyList<string> solutionPaths)
    {
        var projectPaths = snapshot.Projects.Select(p => p.FilePath).Distinct(PathComparison.Comparer).ToList();

        var buildFiles = new HashSet<string>(PathComparison.Comparer);
        buildFiles.UnionWith(solutionPaths.Select(Path.GetFullPath));
        buildFiles.UnionWith(projectPaths);
        buildFiles.UnionWith(WorkspaceManager.EnumerateSharedBuildFiles(projectPaths, solutionPaths));
        AddImports(buildFiles);

        var assetsFiles = new HashSet<string>(PathComparison.Comparer);
        foreach (var project in snapshot.Projects)
            assetsFiles.UnionWith(FindAssetsFiles(project));
        assetsFiles.ExceptWith(buildFiles);

        var inputs = buildFiles.Select(path => (Path: path, Content: true))
            .Concat(assetsFiles.Select(path => (Path: path, Content: false)))
            .ToArray();

        var fingerprints = new InputFingerprint[inputs.Length];
        Parallel.For(0, inputs.Length, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8) },
            i => fingerprints[i] = new InputFingerprint(inputs[i].Path, Fingerprint(inputs[i].Path, inputs[i].Content)));

        return [.. fingerprints.OrderBy(f => f.Path, StringComparer.Ordinal)];
    }

    /// <summary>
    /// The first input whose fingerprint differs between <paramref name="cached"/> and
    /// <paramref name="current"/> (or that only one of them has), or null when they match.
    /// </summary>
    public static string? FirstDifference(IReadOnlyList<InputFingerprint> cached, IReadOnlyList<InputFingerprint> current)
    {
        var now = current.ToDictionary(f => f.Path, f => f.Fingerprint, StringComparer.Ordinal);
        foreach (var input in cached)
        {
            if (!now.Remove(input.Path, out var fingerprint))
                return $"{input.Path} is no longer an input";
            if (fingerprint != input.Fingerprint)
                return fingerprint == Absent ? $"{input.Path} was deleted" : $"{input.Path} changed";
        }

        return now.Count > 0 ? $"{now.Keys.Order(StringComparer.Ordinal).First()} is a new input" : null;
    }

    /// <summary>
    /// The latest last-write time among <paramref name="inputs"/> that exist. A load that
    /// started before this may have read an older version of that file.
    /// </summary>
    public static DateTime LatestWriteUtc(IEnumerable<InputFingerprint> inputs) =>
        inputs.Where(i => i.Fingerprint != Absent)
            .Select(i => File.GetLastWriteTimeUtc(i.Path))
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();

    /// <summary>
    /// Build files are small and often rewritten without changing (a branch switch, a
    /// formatter), so they are compared by content. Restore output can be megabytes per project
    /// and NuGet rewrites it only when it changes, so size and timestamp are enough there.
    /// </summary>
    internal static string Fingerprint(string path, bool content)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return Absent;
            if (!content) return $"stamp:{info.Length}:{info.LastWriteTimeUtc.Ticks}";

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return "sha256:" + Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable now: a value no write can have produced, so the key cannot match.
            return "unreadable:" + Guid.NewGuid().ToString("N");
        }
    }

    /// <summary>
    /// A project's restore output. Usually <c>obj/project.assets.json</c> beside the project,
    /// but a repo can move it (<c>BaseIntermediateOutputPath</c>, the artifacts layout). The SDK
    /// writes generated sources (AssemblyInfo, global usings) under the same intermediate
    /// folder, so the folders from each such document up to its <c>obj</c> folder are searched.
    /// The conventional path is always an input, as "absent" when missing, so a project that
    /// is restored later invalidates the cache.
    /// </summary>
    private static IEnumerable<string> FindAssetsFiles(CachedProject project)
    {
        var projectDirectory = Path.GetDirectoryName(project.FilePath)!;
        yield return Path.Combine(projectDirectory, "obj", AssetsFileName);

        var searched = new HashSet<string>(PathComparison.Comparer);
        foreach (var document in project.Documents)
        {
            var directory = Path.GetDirectoryName(document.FilePath);
            if (directory is null || !ContainsObjSegment(directory)) continue;

            for (; directory is not null && searched.Add(directory); directory = Path.GetDirectoryName(directory))
            {
                var candidate = Path.Combine(directory, AssetsFileName);
                if (File.Exists(candidate))
                {
                    yield return candidate;
                    break;
                }

                if (Path.GetFileName(directory).Equals("obj", StringComparison.OrdinalIgnoreCase)) break;
            }
        }
    }

    private static bool ContainsObjSegment(string directory) =>
        directory.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment.Equals("obj", StringComparison.OrdinalIgnoreCase));

    /// <summary>Adds the statically resolvable imports of every file in <paramref name="files"/>, transitively.</summary>
    private static void AddImports(HashSet<string> files)
    {
        var pending = new Stack<string>(files.Where(IsMSBuildFile));
        var visited = new HashSet<string>(PathComparison.Comparer);
        while (pending.TryPop(out var file))
        {
            if (!visited.Add(file)) continue;

            foreach (var import in ReadImports(file))
            {
                if (files.Add(import) && IsMSBuildFile(import))
                    pending.Push(import);
            }
        }
    }

    private static bool IsMSBuildFile(string path) =>
        Path.GetExtension(path) is var extension
        && (extension.EndsWith("proj", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".props", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".targets", StringComparison.OrdinalIgnoreCase));

    internal static IEnumerable<string> ReadImports(string file)
    {
        string text;
        try
        {
            if (!File.Exists(file)) return [];
            text = File.ReadAllText(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var directory = Path.GetDirectoryName(file)!;
        var imports = new List<string>();
        foreach (Match match in ImportElement().Matches(text))
        {
            var project = match.Groups["path"].Value.Trim()
                .Replace("$(MSBuildThisFileDirectory)", directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

            // Anything still needing evaluation (properties, item transforms, wildcards) is not
            // resolvable here; the background verification covers it.
            if (project.Length == 0 || project.Contains("$(") || project.Contains("@(") || project.Contains("%(")
                || project.Contains('*') || project.Contains('?') || project.Contains(';'))
            {
                continue;
            }

            try
            {
                imports.Add(Path.GetFullPath(project.Replace('\\', Path.DirectorySeparatorChar), directory));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // Not a path after all.
            }
        }

        return imports;
    }

    [GeneratedRegex("""<Import\b[^>]*?\bProject\s*=\s*["'](?<path>[^"']*)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex ImportElement();
}
