using System.Text.RegularExpressions;
using CWM.RoslynNavigator.Bench.Measurement;

namespace CWM.RoslynNavigator.Bench.Corpus;

/// <summary>
/// The part of a clone the solution actually covers: the directories of its declared
/// projects. Repositories often hold samples or a second solution beside the one benchmarked;
/// searching those would inflate the manual route with code the navigator never sees, so
/// every search, size, and candidate is scoped to these directories. Derived from the
/// solution file alone, so it is identical for every server build.
/// </summary>
internal sealed partial class SolutionScope
{
    private SolutionScope(string solutionDirectory, List<string> projectFiles, List<string> roots)
    {
        SolutionDirectory = solutionDirectory;
        ProjectFiles = projectFiles;
        Roots = roots;
    }

    public string SolutionDirectory { get; }

    public List<string> ProjectFiles { get; }

    /// <summary>Project directories with nested duplicates removed, ordinal order.</summary>
    public List<string> Roots { get; }

    public static SolutionScope For(string solutionPath)
    {
        var directory = Path.GetDirectoryName(solutionPath)!;
        var projects = ProjectPattern().Matches(File.ReadAllText(solutionPath))
            .Select(m => Path.GetFullPath(Path.Combine(directory, m.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar))))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToList();

        var roots = new List<string>();
        foreach (var candidate in projects.Select(p => Path.GetDirectoryName(p)!).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(d => d.Length))
        {
            var nested = roots.Any(r => candidate.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            if (!nested && Directory.Exists(candidate)) roots.Add(candidate);
        }

        roots.Sort(StringComparer.Ordinal);
        return new SolutionScope(directory, projects, roots);
    }

    /// <summary>Every file with the extension under any project directory, each once.</summary>
    public IEnumerable<string> Files(string extension) =>
        extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
            ? ProjectFiles.Where(File.Exists)
            : Roots.SelectMany(root => GrepScanner.EnumerateFiles(root, extension));

    [GeneratedRegex(@"[""']([^""']+\.csproj)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex ProjectPattern();
}
