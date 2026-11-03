using System.Xml;
using System.Xml.Linq;

namespace CWM.RoslynNavigator;

/// <summary>
/// Splits a solution's projects into groups that load side by side, each in its own MSBuild
/// build host (see <see cref="ParallelSolutionLoader"/>).
///
/// <para>MSBuild loads a project's <c>ProjectReference</c> closure along with it, so a library
/// every group depends on is loaded once per group. The split therefore keeps closures
/// together: projects are placed largest closure first, each into the group whose combined
/// closure stays smallest after adding it. That balances the groups by the work MSBuild will
/// actually do, not by the number of projects listed.</para>
///
/// <para>The reference graph is read straight from the project files, without evaluation, so
/// references added by imports or conditions are missed. That only makes the split less even:
/// the real loader still resolves every reference itself.</para>
/// </summary>
internal static class ProjectPartitioner
{
    /// <summary>
    /// A split: the groups, how many projects the busiest group will load (its own plus their
    /// references), and how many distinct projects a single load would. Their ratio is the
    /// best speed-up the split can give: one project referencing nearly everything (an
    /// application that bundles every module) caps it near 1, however many groups there are.
    /// </summary>
    internal sealed record Plan(IReadOnlyList<IReadOnlyList<string>> Groups, int LargestGroupLoad, int SingleLoad)
    {
        public double EstimatedSpeedup => LargestGroupLoad == 0 ? 1 : (double)SingleLoad / LargestGroupLoad;
    }

    /// <summary>
    /// Partitions <paramref name="projectPaths"/> (absolute, normalized) into at most
    /// <paramref name="groupCount"/> non-empty groups. Every project lands in exactly one group.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> Partition(IReadOnlyList<string> projectPaths, int groupCount) =>
        CreatePlan(projectPaths, groupCount).Groups;

    public static Plan CreatePlan(IReadOnlyList<string> projectPaths, int groupCount)
    {
        if (groupCount <= 1 || projectPaths.Count <= 1)
            return new Plan([projectPaths], projectPaths.Count, projectPaths.Count);

        var references = new Dictionary<string, IReadOnlyList<string>>(PathComparison.Comparer);
        var closures = projectPaths
            .Distinct(PathComparison.Comparer)
            .Select(path => (Path: path, Closure: Closure(path, references)))
            .OrderByDescending(p => p.Closure.Count)
            .ThenBy(p => p.Path, PathComparison.Comparer)
            .ToList();

        var count = Math.Min(groupCount, closures.Count);
        var members = Enumerable.Range(0, count).Select(_ => new List<string>()).ToArray();
        var unions = Enumerable.Range(0, count).Select(_ => new HashSet<string>(PathComparison.Comparer)).ToArray();

        foreach (var (path, closure) in closures)
        {
            var best = 0;
            var bestSize = int.MaxValue;
            for (var g = 0; g < count; g++)
            {
                var size = unions[g].Count + closure.Count(c => !unions[g].Contains(c));
                if (size < bestSize || (size == bestSize && members[g].Count < members[best].Count))
                {
                    best = g;
                    bestSize = size;
                }
            }

            members[best].Add(path);
            unions[best].UnionWith(closure);
        }

        var single = new HashSet<string>(PathComparison.Comparer);
        foreach (var union in unions)
            single.UnionWith(union);

        return new Plan(
            [.. members.Where(m => m.Count > 0)],
            unions.Max(u => u.Count),
            single.Count);
    }

    /// <summary>The project and every project it references, transitively, as far as the files say.</summary>
    internal static HashSet<string> Closure(string projectPath, Dictionary<string, IReadOnlyList<string>> cache)
    {
        var closure = new HashSet<string>(PathComparison.Comparer) { projectPath };
        var pending = new Stack<string>([projectPath]);
        while (pending.TryPop(out var current))
        {
            foreach (var reference in ReadProjectReferences(current, cache))
            {
                if (closure.Add(reference))
                    pending.Push(reference);
            }
        }

        return closure;
    }

    /// <summary>
    /// The literal <c>ProjectReference</c> includes of a project file, resolved against its
    /// directory. Includes that use MSBuild properties or wildcards are skipped; a missing or
    /// malformed file has none.
    /// </summary>
    internal static IReadOnlyList<string> ReadProjectReferences(string projectPath, Dictionary<string, IReadOnlyList<string>> cache)
    {
        if (cache.TryGetValue(projectPath, out var cached))
            return cached;

        List<string> result = [];
        try
        {
            var directory = Path.GetDirectoryName(projectPath)!;
            var document = XDocument.Load(projectPath);
            foreach (var element in document.Descendants().Where(e => e.Name.LocalName == "ProjectReference"))
            {
                if (element.Attribute("Include")?.Value is not { Length: > 0 } include
                    || include.Contains("$(", StringComparison.Ordinal)
                    || include.Contains('*'))
                {
                    continue;
                }

                foreach (var part in include.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var full = SolutionFileReader.NormalizePath(Path.Combine(directory, part));
                    if (File.Exists(full))
                        result.Add(full);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
        {
            // Unreadable here; the loader reports it properly.
        }

        cache[projectPath] = result;
        return result;
    }
}
