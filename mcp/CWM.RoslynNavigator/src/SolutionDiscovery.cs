namespace CWM.RoslynNavigator;

/// <summary>
/// Discovers .sln/.slnx files from command-line arguments or the working directory.
/// Uses breadth-first search up to <see cref="MaxSearchDepth"/> levels deep.
/// </summary>
internal static class SolutionDiscovery
{
    private static readonly string[] SolutionExtensions = [".sln", ".slnx"];

    /// <summary>
    /// Maximum directory depth to search for solution files (0 = root only).
    /// </summary>
    public const int MaxSearchDepth = 3;

    /// <summary>
    /// Opt-in flag: load every solution found under each scanned directory instead of only
    /// the first. Meant for microservice layouts where one feature spans several solutions.
    /// </summary>
    public const string AllSolutionsFlag = "--all-solutions";

    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".idea", "node_modules", "bin", "obj",
        "packages", "artifacts", "TestResults", ".claude"
    };

    /// <summary>
    /// Resolves the solution file path from the provided arguments or by scanning the working directory.
    /// Returns the first solution when several are configured.
    /// </summary>
    /// <param name="args">Command-line arguments. Supports --solution / -s path.</param>
    /// <param name="workingDirectory">The directory to scan if no explicit path is provided.</param>
    /// <returns>The full path to the solution file, or null if not found.</returns>
    public static string? FindSolutionPath(string[] args, string? workingDirectory = null) =>
        FindSolutionPaths(args, workingDirectory).FirstOrDefault();

    /// <summary>
    /// Resolves every solution to load. Each <c>--solution</c> / <c>-s</c> value (repeatable) is a
    /// solution file or a directory to scan; without one, the working directory is scanned. A
    /// scanned directory yields its first solution, or all of them with <see cref="AllSolutionsFlag"/>.
    /// Explicit paths that do not exist are skipped, and the working directory is not scanned as a
    /// fallback — an explicit argument always wins. Order is preserved and duplicates removed.
    /// </summary>
    public static IReadOnlyList<string> FindSolutionPaths(string[] args, string? workingDirectory = null)
    {
        var all = IsAllSolutionsMode(args);
        var explicitPaths = GetExplicitSolutionPaths(args);

        IEnumerable<string> found = explicitPaths.Count > 0
            ? explicitPaths.SelectMany(path => ResolveExplicit(path, all))
            : ScanDirectory(workingDirectory ?? Directory.GetCurrentDirectory(), all);

        return found.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Solutions found under <paramref name="directory"/>: the first one, or every one within the
    /// depth limit when <paramref name="all"/> is set. Used for MCP roots as well as the CLI.
    /// </summary>
    public static IReadOnlyList<string> ScanDirectory(string directory, bool all)
    {
        if (!Directory.Exists(directory))
            return [];

        var results = new List<string>();
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((Path.GetFullPath(directory), 0));

        while (queue.Count > 0)
        {
            var (currentDir, depth) = queue.Dequeue();

            var solutions = SafeGetSolutionFiles(currentDir);
            if (solutions.Count > 0)
            {
                // Shallowest match wins; within the same depth, alphabetical.
                if (!all) return [solutions[0]];
                results.AddRange(solutions);
            }

            if (depth >= MaxSearchDepth)
                continue;

            foreach (var subDir in SafeGetDirectories(currentDir))
            {
                var dirName = Path.GetFileName(subDir);
                if (!SkippedDirectories.Contains(dirName))
                    queue.Enqueue((subDir, depth + 1));
            }
        }

        return results;
    }

    public static bool IsAllSolutionsMode(string[] args) =>
        args.Any(a => string.Equals(a, AllSolutionsFlag, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> ResolveExplicit(string path, bool all)
    {
        if (File.Exists(path))
            return [Path.GetFullPath(path)];

        if (Directory.Exists(path))
            return ScanDirectory(path, all);

        return [];
    }

    private static List<string> GetExplicitSolutionPaths(string[] args)
    {
        var paths = new List<string>();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] is "--solution" or "-s")
            {
                paths.Add(args[i + 1]);
                i++;
            }
        }
        return paths;
    }

    private static List<string> SafeGetSolutionFiles(string directory)
    {
        try
        {
            return SolutionExtensions
                .SelectMany(ext => Directory.GetFiles(directory, $"*{ext}"))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    private static string[] SafeGetDirectories(string directory)
    {
        try
        {
            var dirs = Directory.GetDirectories(directory);
            Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
            return dirs;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }
}
