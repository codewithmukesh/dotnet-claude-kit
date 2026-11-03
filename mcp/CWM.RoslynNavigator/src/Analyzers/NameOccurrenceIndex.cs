using System.Buffers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace CWM.RoslynNavigator.Analyzers;

/// <summary>
/// Records which syntax trees mention each of a fixed set of names as a whole identifier —
/// a maximal run of letters, digits and underscores — anywhere in the source text, including
/// comments and string literals. One pass over each tree's text replaces scanning every file
/// once per name, which was O(names × files × text) on large solutions.
/// </summary>
internal sealed class NameOccurrenceIndex
{
    private readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> _names;
    private readonly Dictionary<string, (SyntaxTree First, bool InMultipleTrees)> _seen = new(StringComparer.Ordinal);

    // Names that are not a single identifier run (e.g. an indexer's "this[]") can never be
    // matched by the token scan, so they keep the original text search against kept texts.
    private readonly HashSet<string> _irregularNames = new(StringComparer.Ordinal);
    private readonly List<(SyntaxTree Tree, string Text)> _irregularTexts = [];

    public NameOccurrenceIndex(IEnumerable<string> names)
    {
        var regular = new HashSet<string>(StringComparer.Ordinal);

        foreach (var name in names)
        {
            if (name.Length > 0 && name.All(IsIdentifierChar))
                regular.Add(name);
            else
                _irregularNames.Add(name);
        }

        _names = regular.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    /// <summary>Indexes one tree. Callers add each tree once.</summary>
    public void Add(SyntaxTree tree, SourceText text) => Record(tree, Find(text), text);

    /// <summary>
    /// The indexed names <paramref name="text"/> contains as whole identifiers. Reads only, so
    /// several texts can be scanned in parallel and recorded afterwards with <see cref="Record"/>.
    /// </summary>
    public List<string> Find(SourceText text)
    {
        var found = new List<string>();
        var length = text.Length;
        var buffer = ArrayPool<char>.Shared.Rent(Math.Max(1, length));

        try
        {
            text.CopyTo(0, buffer, 0, length);
            var span = buffer.AsSpan(0, length);

            var i = 0;
            while (i < span.Length)
            {
                if (!IsIdentifierChar(span[i]))
                {
                    i++;
                    continue;
                }

                var start = i;
                while (i < span.Length && IsIdentifierChar(span[i]))
                    i++;

                if (_names.TryGetValue(span[start..i], out var name))
                    found.Add(name);
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }

        return found;
    }

    /// <summary>Records what <see cref="Find"/> returned for <paramref name="tree"/>. Not thread-safe.</summary>
    public void Record(SyntaxTree tree, List<string> names, SourceText text)
    {
        foreach (var name in names)
            Record(name, tree);

        if (_irregularNames.Count > 0)
            _irregularTexts.Add((tree, text.ToString()));
    }

    /// <summary>
    /// Whether <paramref name="name"/> appears as a whole identifier in any indexed tree other
    /// than <paramref name="declaringTree"/>.
    /// </summary>
    public bool AppearsOutside(string name, SyntaxTree? declaringTree)
    {
        if (_irregularNames.Contains(name))
        {
            return _irregularTexts.Any(entry =>
                entry.Tree != declaringTree && ContainsIdentifier(entry.Text, name));
        }

        return _seen.TryGetValue(name, out var seen)
            && (seen.InMultipleTrees || seen.First != declaringTree);
    }

    private void Record(string name, SyntaxTree tree)
    {
        if (!_seen.TryGetValue(name, out var seen))
            _seen[name] = (tree, false);
        else if (!seen.InMultipleTrees && seen.First != tree)
            _seen[name] = (seen.First, true);
    }

    /// <summary>Start positions of <paramref name="name"/> in <paramref name="text"/> as a whole identifier token.</summary>
    internal static IEnumerable<int> IdentifierPositions(string text, string name)
    {
        if (name.Length == 0) yield break;

        var index = 0;
        while ((index = text.IndexOf(name, index, StringComparison.Ordinal)) >= 0)
        {
            var afterIndex = index + name.Length;
            if ((index == 0 || !IsIdentifierChar(text[index - 1]))
                && (afterIndex >= text.Length || !IsIdentifierChar(text[afterIndex])))
            {
                yield return index;
            }

            index += 1;
        }
    }

    /// <summary>
    /// Checks whether <paramref name="name"/> occurs in <paramref name="text"/> as a whole
    /// identifier token (not as a substring of a longer identifier).
    /// </summary>
    internal static bool ContainsIdentifier(string text, string name)
    {
        var index = 0;
        while ((index = text.IndexOf(name, index, StringComparison.Ordinal)) >= 0)
        {
            var before = index == 0 ? '\0' : text[index - 1];
            var afterIndex = index + name.Length;
            var after = afterIndex >= text.Length ? '\0' : text[afterIndex];

            if (!IsIdentifierChar(before) && !IsIdentifierChar(after))
                return true;

            index += 1;
        }

        return false;
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

}
