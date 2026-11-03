using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace CWM.RoslynNavigator;

/// <summary>
/// Keeps one target framework per multi-targeted project.
///
/// <para>MSBuildWorkspace loads a project once per target framework, so a library targeting
/// netstandard2.0;net8.0;net10.0 is three Roslyn projects with the same files. Every file is
/// parsed and compiled three times, held in memory three times, and every search finds it
/// three times. ABP's 270 projects load as 690.</para>
///
/// <para>After a load, each multi-targeted project keeps its primary instance — the highest
/// plain <c>netX.Y</c> target, else the highest of the others (see <see cref="Rank"/>) — and
/// drops the rest, except an instance that is still needed:</para>
/// <list type="bullet">
/// <item>a kept project references it (a netstandard2.0-only project referencing the library
/// needs the library's netstandard2.0 build, not its net10.0 one), or</item>
/// <item>it compiles a source file no kept instance of that project compiles, such as a file
/// included only under one framework by a conditional <c>Compile</c> item. Such an instance
/// is kept, and the reason is reported as a load diagnostic.</item>
/// </list>
/// <para>What is lost is code inside <c>#if</c> blocks for the dropped frameworks: the files
/// are indexed, but under the primary framework's symbols. <c>--all-target-frameworks</c>
/// keeps every instance.</para>
/// </summary>
internal static partial class TargetFrameworkPruner
{
    /// <summary>The result: the pruned solution and what was dropped or kept and why.</summary>
    internal sealed record PruneResult(
        Solution Solution,
        int Dropped,
        IReadOnlyList<string> KeptForCoverage);

    public static PruneResult Prune(Solution solution)
    {
        var byFile = solution.Projects
            .Where(p => p.FilePath is not null)
            .GroupBy(p => SolutionFileReader.NormalizePath(p.FilePath!), PathComparison.Comparer)
            .ToList();
        if (byFile.All(g => g.Count() == 1))
            return new PruneResult(solution, 0, []);

        var keep = new HashSet<ProjectId>();
        var secondaries = new List<(Project Project, Project Primary, List<Project> Instances)>();
        foreach (var group in byFile)
        {
            var instances = group.ToList();
            var primary = instances
                .OrderByDescending(p => p.DocumentIds.Count > 0)
                .ThenByDescending(p => Rank(TargetFrameworkOf(p)))
                .ThenBy(p => p.Name, StringComparer.Ordinal)
                .First();
            keep.Add(primary.Id);
            secondaries.AddRange(instances.Where(p => p.Id != primary.Id).Select(p => (p, primary, instances)));
        }

        // Projects without a file path are never pruned.
        foreach (var project in solution.Projects.Where(p => p.FilePath is null))
            keep.Add(project.Id);

        List<string> keptForCoverage = [];
        var changed = true;
        while (changed)
        {
            changed = AddReferenceClosure(solution, keep);

            foreach (var (secondary, primary, instances) in secondaries)
            {
                if (keep.Contains(secondary.Id)) continue;

                var covered = KeptSourcePaths(instances, keep);
                var uncovered = SourcePaths(secondary).Where(path => !covered.Contains(path)).ToList();
                if (uncovered.Count == 0) continue;

                keep.Add(secondary.Id);
                changed = true;
                keptForCoverage.Add(
                    $"{secondary.Name}: kept because it compiles {uncovered.Count} file(s) {primary.Name} does not " +
                    $"(e.g. {Path.GetRelativePath(Path.GetDirectoryName(secondary.FilePath)!, uncovered[0]).Replace('\\', '/')})");
            }
        }

        var dropped = solution.ProjectIds.Where(id => !keep.Contains(id)).ToList();
        foreach (var id in dropped)
            solution = solution.RemoveProject(id);

        return new PruneResult(solution, dropped.Count, keptForCoverage);
    }

    /// <summary>Adds every project a kept project references, transitively. True when any was added.</summary>
    private static bool AddReferenceClosure(Solution solution, HashSet<ProjectId> keep)
    {
        var added = false;
        var pending = new Stack<ProjectId>(keep);
        while (pending.TryPop(out var id))
        {
            if (solution.GetProject(id) is not { } project) continue;
            foreach (var reference in project.ProjectReferences)
            {
                if (solution.ContainsProject(reference.ProjectId) && keep.Add(reference.ProjectId))
                {
                    added = true;
                    pending.Push(reference.ProjectId);
                }
            }
        }

        return added;
    }

    /// <summary>Source files compiled by the kept ones among one project file's <paramref name="instances"/>.</summary>
    private static HashSet<string> KeptSourcePaths(List<Project> instances, HashSet<ProjectId> keep)
    {
        var paths = new HashSet<string>(PathComparison.Comparer);
        foreach (var project in instances)
        {
            if (keep.Contains(project.Id))
                paths.UnionWith(SourcePaths(project));
        }

        return paths;
    }

    /// <summary>
    /// The project's documents, except what the SDK generates per framework (AssemblyInfo,
    /// global usings, ...), which differ between instances by path only: anything under the
    /// project's obj/ folder, or in a folder named after the framework — obj/Debug/net8.0/, or
    /// the artifacts layout's obj/Lib/debug_net8.0/.
    /// </summary>
    private static IEnumerable<string> SourcePaths(Project project)
    {
        var objPrefix = Path.GetDirectoryName(project.FilePath) is { } directory
            ? Path.Combine(directory, "obj") + Path.DirectorySeparatorChar
            : null;
        var tfm = TargetFrameworkOf(project);
        return project.Documents
            .Select(d => d.FilePath)
            .OfType<string>()
            .Where(path => (objPrefix is null || !path.StartsWith(objPrefix, PathComparison.Comparison))
                           && (tfm is null || !InFrameworkFolder(path, tfm)));
    }

    private static bool InFrameworkFolder(string path, string tfm)
    {
        var directory = Path.GetDirectoryName(path);
        while (directory is not null)
        {
            var name = Path.GetFileName(directory);
            if (name.Equals(tfm, StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("_" + tfm, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            directory = Path.GetDirectoryName(directory);
        }

        return false;
    }

    /// <summary>The target framework MSBuildWorkspace appends to a multi-targeted project's name: "Lib(net10.0)".</summary>
    internal static string? TargetFrameworkOf(Project project) => TargetFrameworkOf(project.Name);

    internal static string? TargetFrameworkOf(string projectName)
    {
        if (!projectName.EndsWith(')')) return null;
        var open = projectName.LastIndexOf('(');
        return open < 0 ? null : projectName[(open + 1)..^1];
    }

    /// <summary>
    /// Preference order: plain .NET (net5.0 and later, no OS suffix) by version, then .NET with
    /// an OS suffix (net10.0-windows), then .NET Core, then .NET Standard, then .NET Framework,
    /// then anything unrecognized. A framework newer than the running .NET is ranked last among
    /// its kind, since this SDK cannot have evaluated it properly.
    /// </summary>
    internal static (int Family, Version Version) Rank(string? targetFramework)
    {
        if (targetFramework is null) return (0, new Version(0, 0));
        var tfm = targetFramework.ToLowerInvariant();

        if (ModernDotNet().Match(tfm) is { Success: true } modern)
        {
            var version = new Version(int.Parse(modern.Groups[1].Value), int.Parse(modern.Groups[2].Value));
            var supported = version.Major <= Environment.Version.Major;
            var family = modern.Groups[3].Success ? 5 : 6;
            return (supported ? family : 1, version);
        }

        if (NetCoreApp().Match(tfm) is { Success: true } core)
            return (4, new Version(int.Parse(core.Groups[1].Value), int.Parse(core.Groups[2].Value)));

        if (NetStandard().Match(tfm) is { Success: true } standard)
            return (3, new Version(int.Parse(standard.Groups[1].Value), int.Parse(standard.Groups[2].Value)));

        if (NetFramework().Match(tfm) is { Success: true } framework)
            return (2, new Version(int.Parse(framework.Groups[1].Value), 0));

        return (0, new Version(0, 0));
    }

    [GeneratedRegex(@"^net(\d+)\.(\d+)(-.+)?$")]
    private static partial Regex ModernDotNet();

    [GeneratedRegex(@"^netcoreapp(\d+)\.(\d+)$")]
    private static partial Regex NetCoreApp();

    [GeneratedRegex(@"^netstandard(\d+)\.(\d+)$")]
    private static partial Regex NetStandard();

    [GeneratedRegex(@"^net(\d{2,3})$")]
    private static partial Regex NetFramework();
}
