using CWM.RoslynNavigator.Tools;

namespace CWM.RoslynNavigator.Responses;

/// <summary>
/// The two forms an inventory-style list can take, and the choice between them.
/// <list type="bullet">
/// <item><b>full</b>: one JSON object per entry — the detailed shape, unchanged for small results.</item>
/// <item><b>compact</b>: entries grouped by file as short "line ..." strings, a third to a half the
/// size. A large answer then fits the response budget whole, or in a few pages, instead of being
/// cut to its first entries.</item>
/// </list>
/// <c>auto</c> (every tool's default) keeps the full form when the page fits
/// <see cref="ResponseBudget.MaxChars"/> and switches to compact when it does not.
/// </summary>
internal static class ListFormat
{
    public const string Auto = "auto";
    public const string Full = "full";
    public const string Compact = "compact";

    /// <summary>Most entries a per-project count list carries before folding the rest into one line.</summary>
    public const int TopProjects = 25;

    /// <summary>Null when <paramref name="format"/> is valid; otherwise a serialized InvalidArgument error.</summary>
    public static string? Validate(string? format) =>
        format is null ? null : ArgumentChecks.Choice("format", format, Auto, Full, Compact);

    /// <summary>
    /// Serializes <paramref name="page"/> in the requested form, fitted to the response budget.
    /// Auto tries the full form for the whole page first and falls back to compact.
    /// </summary>
    public static string Render<T>(
        Paging.Page<T> page,
        string? format,
        Func<Paging.Page<T>, string> full,
        Func<Paging.Page<T>, string> compact)
    {
        if (string.Equals(format, Full, StringComparison.OrdinalIgnoreCase))
            return Paging.Fit(page, full);

        if (string.Equals(format, Compact, StringComparison.OrdinalIgnoreCase))
            return Paging.Fit(page, compact);

        var json = full(page);
        return json.Length <= ResponseBudget.MaxChars ? json : Paging.Fit(page, compact);
    }

    /// <summary>Groups entries by file, files in order of first appearance, entries in input order.</summary>
    public static List<FileEntries> GroupByFile<T>(IEnumerable<T> items, Func<T, string> file, Func<T, string> entry)
    {
        var groups = new List<FileEntries>();
        var byFile = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var path = file(item);
            if (!byFile.TryGetValue(path, out var entries))
            {
                entries = [];
                byFile[path] = entries;
                groups.Add(new FileEntries(path, entries));
            }

            entries.Add(entry(item));
        }

        return groups;
    }

    /// <summary>
    /// Counts per key, largest first. Past <paramref name="top"/> keys the rest fold into one
    /// "(N more)" entry, so the counts always add up to the number of items.
    /// </summary>
    public static List<NamedCount> CountBy<T>(IEnumerable<T> items, Func<T, string> key, int top = TopProjects)
    {
        var counts = items
            .GroupBy(key, StringComparer.Ordinal)
            .Select(g => new NamedCount(g.Key, g.Count()))
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .ToList();

        if (counts.Count <= top)
            return counts;

        var rest = counts.Skip(top).ToList();
        return [.. counts.Take(top), new NamedCount($"({rest.Count} more)", rest.Sum(c => c.Count))];
    }

    /// <summary>"Lib(net8.0)" → "Lib": the csproj a multi-targeted flavor belongs to.</summary>
    public static string ProjectName(Microsoft.CodeAnalysis.Project project) =>
        GetProjectGraphTool.BaseName(project.Name);
}
