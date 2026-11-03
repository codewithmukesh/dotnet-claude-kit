using CWM.RoslynNavigator.Tools;
using Microsoft.CodeAnalysis;

namespace CWM.RoslynNavigator;

/// <summary>
/// Which target-framework flavor each loaded project is. MSBuildWorkspace loads a
/// multi-targeted csproj once per framework ("Lib(net8.0)", "Lib(net10.0)"), so the same
/// source location is found once per flavor. Tools merge those duplicates; this says, for a
/// merged entry, whether it exists in only some of its project's frameworks (code under
/// <c>#if NET8_0</c>) — the one case where the frameworks are worth reporting.
/// </summary>
internal sealed class TargetFrameworkFlavors
{
    private readonly Dictionary<ProjectId, (string ProjectFile, string Tfm)> _flavors = [];
    private readonly Dictionary<string, int> _flavorCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Solution _solution;

    public TargetFrameworkFlavors(Solution solution)
    {
        _solution = solution;

        foreach (var project in solution.Projects)
        {
            if (project.FilePath is not { } file
                || GetProjectGraphTool.ExtractTfmFromProjectName(project.Name) is not { } tfm)
            {
                continue;
            }

            _flavors[project.Id] = (file, tfm);
            _flavorCounts[file] = _flavorCounts.GetValueOrDefault(file) + 1;
        }
    }

    /// <summary>True when the solution loads any project more than once.</summary>
    public bool Any => _flavors.Count > 0;

    /// <summary>The project whose compilation <paramref name="tree"/> belongs to, if any.</summary>
    public ProjectId? ProjectOf(SyntaxTree? tree) =>
        tree is null ? null : _solution.GetDocumentId(tree)?.ProjectId;

    /// <summary>The project declaring <paramref name="symbol"/> (its first declaration).</summary>
    public ProjectId? ProjectOf(ISymbol symbol) =>
        ProjectOf(symbol.DeclaringSyntaxReferences.FirstOrDefault()?.SyntaxTree);

    /// <summary>
    /// The frameworks an entry found in <paramref name="projects"/> exists in, when that is a
    /// strict subset of its csproj's frameworks; null when it exists in all of them, when its
    /// project is not multi-targeted, or when the entries span different csproj files.
    /// </summary>
    public List<string>? Subset(IEnumerable<ProjectId?> projects)
    {
        if (!Any) return null;

        string? projectFile = null;
        var tfms = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var id in projects)
        {
            if (id is null || !_flavors.TryGetValue(id, out var flavor))
                return null;

            if (projectFile is null)
                projectFile = flavor.ProjectFile;
            else if (!projectFile.Equals(flavor.ProjectFile, StringComparison.OrdinalIgnoreCase))
                return null;

            tfms.Add(flavor.Tfm);
        }

        return projectFile is not null && tfms.Count < _flavorCounts[projectFile] ? [.. tfms] : null;
    }

    /// <summary>
    /// One entry per <paramref name="key"/>, in first-seen order. An entry found in only some
    /// of its project's frameworks gets them via <paramref name="withFrameworks"/>.
    /// </summary>
    public List<T> Merge<T, TKey>(
        IEnumerable<(T Item, ProjectId? Project)> entries,
        Func<T, TKey> key,
        Func<T, List<string>, T> withFrameworks)
        where TKey : notnull
    {
        var index = new Dictionary<TKey, int>();
        var merged = new List<(T Item, List<ProjectId?> Projects)>();

        foreach (var (item, project) in entries)
        {
            var k = key(item);
            if (index.TryGetValue(k, out var at))
            {
                merged[at].Projects.Add(project);
                continue;
            }

            index[k] = merged.Count;
            merged.Add((item, [project]));
        }

        return [.. merged.Select(m => Subset(m.Projects) is { } tfms ? withFrameworks(m.Item, tfms) : m.Item)];
    }
}
