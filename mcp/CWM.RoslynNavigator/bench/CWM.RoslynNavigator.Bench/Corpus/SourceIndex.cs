using System.Text.RegularExpressions;

namespace CWM.RoslynNavigator.Bench.Corpus;

/// <summary>
/// A cheap textual pre-pass over the corpus. It only nominates candidates; the server makes
/// every final call (implementations, references, callers, overrides), so a textual false
/// positive can be nominated but never selected on text alone.
/// </summary>
internal sealed partial class SourceIndex
{
    private static readonly HashSet<string> ObjectMembers =
        ["ToString", "Equals", "GetHashCode", "Dispose", "DisposeAsync", "Finalize"];

    private SourceIndex(
        Dictionary<string, int> identifierCounts,
        Dictionary<string, int> interfaces,
        Dictionary<string, int> classes,
        Dictionary<string, int> abstractClasses,
        Dictionary<string, int> overrides,
        HashSet<(string Class, string Method)> virtuals,
        List<(string Path, long Bytes)> files)
    {
        IdentifierCounts = identifierCounts;
        Interfaces = interfaces;
        Classes = classes;
        AbstractClasses = abstractClasses;
        Overrides = overrides;
        Virtuals = virtuals;
        Files = files;
    }

    public Dictionary<string, int> IdentifierCounts { get; }

    /// <summary>Production-code declarations: name → number of declarations.</summary>
    public Dictionary<string, int> Interfaces { get; }

    public Dictionary<string, int> Classes { get; }

    public Dictionary<string, int> AbstractClasses { get; }

    /// <summary>Overriding method name → occurrences across production code.</summary>
    public Dictionary<string, int> Overrides { get; }

    /// <summary>Virtual or abstract method declarations in production code, with their declaring class.</summary>
    public HashSet<(string Class, string Method)> Virtuals { get; }

    /// <summary>Hand-written production files, largest first.</summary>
    public List<(string Path, long Bytes)> Files { get; }

    public static SourceIndex Build(SolutionScope scope)
    {
        var identifiers = new Dictionary<string, int>(StringComparer.Ordinal);
        var interfaces = new Dictionary<string, int>(StringComparer.Ordinal);
        var classes = new Dictionary<string, int>(StringComparer.Ordinal);
        var abstracts = new Dictionary<string, int>(StringComparer.Ordinal);
        var overrides = new Dictionary<string, int>(StringComparer.Ordinal);
        var virtuals = new HashSet<(string, string)>();
        var files = new List<(string, long)>();

        foreach (var file in scope.Files(".cs").Order(StringComparer.Ordinal))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in IdentifierPattern().Matches(text)) Increment(identifiers, m.Value);

            var relative = Path.GetRelativePath(scope.SolutionDirectory, file);
            if (IsTestPath(relative)) continue;

            foreach (Match m in InterfacePattern().Matches(text)) Increment(interfaces, m.Groups[1].Value);
            var classStarts = new List<(int Index, string Name)>();
            foreach (Match m in ClassPattern().Matches(text))
            {
                classStarts.Add((m.Index, m.Groups["name"].Value));
                if (m.Groups["static"].Success) continue;
                Increment(classes, m.Groups["name"].Value);
                if (m.Groups["abstract"].Success) Increment(abstracts, m.Groups["name"].Value);
            }

            foreach (Match m in OverridePattern().Matches(text))
            {
                if (!ObjectMembers.Contains(m.Groups[1].Value)) Increment(overrides, m.Groups[1].Value);
            }

            // The declaring class is the nearest class declaration above the member — right
            // for ordinary files, occasionally wrong for nested types; the server verifies.
            foreach (Match m in VirtualPattern().Matches(text))
            {
                var owner = classStarts.LastOrDefault(c => c.Index < m.Index).Name;
                if (owner is not null && !ObjectMembers.Contains(m.Groups[1].Value)) virtuals.Add((owner, m.Groups[1].Value));
            }

            if (!IsGenerated(relative, text)) files.Add((relative, new FileInfo(file).Length));
        }

        files.Sort((a, b) => b.Item2 != a.Item2 ? b.Item2.CompareTo(a.Item2) : string.CompareOrdinal(a.Item1, b.Item1));
        return new SourceIndex(identifiers, interfaces, classes, abstracts, overrides, virtuals, files);
    }

    /// <summary>
    /// Names declared exactly once (so the server can resolve them unambiguously), ranked by
    /// how often the name appears anywhere in the corpus, ties broken by name.
    /// </summary>
    public List<string> TopUnique(Dictionary<string, int> declarations, int take) =>
        [.. declarations
            .Where(d => d.Value == 1)
            .Select(d => d.Key)
            .OrderByDescending(name => IdentifierCounts.GetValueOrDefault(name))
            .ThenBy(name => name, StringComparer.Ordinal)
            .Take(take)];

    /// <summary>Virtual/abstract declarations whose name is overridden most often in the text.</summary>
    public List<(string Class, string Method)> TopVirtuals(int take) =>
        [.. Virtuals
            .Where(v => Overrides.ContainsKey(v.Method))
            .OrderByDescending(v => Overrides[v.Method])
            .ThenBy(v => v.Class, StringComparer.Ordinal)
            .ThenBy(v => v.Method, StringComparer.Ordinal)
            .Take(take)];

    private static bool IsTestPath(string relative) => TestPathPattern().IsMatch(relative);

    private static bool IsGenerated(string relative, string text) =>
        text.AsSpan(0, Math.Min(text.Length, 2048)).Contains("<auto-generated", StringComparison.OrdinalIgnoreCase)
        || relative.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase)
        || relative.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
        || relative.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase)
        || relative.Contains("Migrations", StringComparison.OrdinalIgnoreCase)
        || relative.Contains("wwwroot", StringComparison.OrdinalIgnoreCase);

    private static void Increment(Dictionary<string, int> counts, string key) =>
        counts[key] = counts.GetValueOrDefault(key) + 1;

    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex(@"\binterface\s+(I[A-Z][A-Za-z0-9_]*)")]
    private static partial Regex InterfacePattern();

    [GeneratedRegex(@"\b(?:(?<static>static)\s+|(?<abstract>abstract)\s+|(?:public|internal|sealed|partial|protected|private)\s+)*class\s+(?<name>[A-Z][A-Za-z0-9_]*)")]
    private static partial Regex ClassPattern();

    [GeneratedRegex(@"\boverride\s+[^;{=()]*?\b([A-Z][A-Za-z0-9_]*)\s*(?:<[^()]*>)?\s*\(")]
    private static partial Regex OverridePattern();

    [GeneratedRegex(@"\b(?:virtual|abstract)\s+(?!class\b)[^;{=()]*?\b([A-Z][A-Za-z0-9_]*)\s*(?:<[^()]*>)?\s*\(")]
    private static partial Regex VirtualPattern();

    [GeneratedRegex(@"(?i)(^|[\\/])[^\\/]*tests?[^\\/]*[\\/]")]
    private static partial Regex TestPathPattern();
}
