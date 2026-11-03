namespace CWM.RoslynNavigator;

/// <summary>How loosely find_symbol matches a name. Each mode includes every tighter one.</summary>
internal enum MatchMode
{
    Exact,
    Prefix,
    CamelHump,
    Substring
}

/// <summary>How well one declared name matched — the primary ranking key. Lower is better.</summary>
internal enum MatchTier
{
    Exact = 0,
    Prefix = 1,
    CamelHump = 2,
    Substring = 3,
    None = 99
}

/// <summary>
/// Name matching for fuzzy symbol search and "did you mean" suggestions. Works on declared
/// simple names only, so it can run as a predicate over each compilation's declaration table
/// (<c>Compilation.GetSymbolsWithName(predicate)</c>) without binding anything.
/// </summary>
internal static class SymbolMatcher
{
    public static bool TryParseMode(string? value, out MatchMode mode)
    {
        mode = (value ?? "exact").Trim().ToLowerInvariant() switch
        {
            "" or "exact" => MatchMode.Exact,
            "prefix" => MatchMode.Prefix,
            "camelhump" or "camel" or "camelcase" => MatchMode.CamelHump,
            "substring" or "contains" => MatchMode.Substring,
            _ => (MatchMode)(-1)
        };
        return Enum.IsDefined(mode);
    }

    /// <summary>
    /// The best tier at which <paramref name="name"/> matches <paramref name="query"/>, or
    /// <see cref="MatchTier.None"/> when it does not match within <paramref name="mode"/>.
    /// Exact is case-sensitive (as the declaration table is); the looser tiers ignore case.
    /// </summary>
    public static MatchTier Classify(string name, string query, MatchMode mode)
    {
        if (query.Length == 0) return MatchTier.None;

        if (name.Equals(query, StringComparison.Ordinal)) return MatchTier.Exact;
        if (mode == MatchMode.Exact) return MatchTier.None;

        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return MatchTier.Prefix;
        if (mode == MatchMode.Prefix) return MatchTier.None;

        if (IsCamelHumpMatch(name, query)) return MatchTier.CamelHump;
        if (mode == MatchMode.CamelHump) return MatchTier.None;

        return name.Contains(query, StringComparison.OrdinalIgnoreCase) ? MatchTier.Substring : MatchTier.None;
    }

    /// <summary>
    /// Camel-hump match: the query's humps are prefixes of the name's humps, in order. The
    /// first query hump anchors on the first name hump; later humps may skip some. "GBIA",
    /// "gbia" and "GetByAsync" all match GetByIdAsync; "IdAsync" does not (that is a
    /// substring). A query with no upper case letters is read one hump per character. Needs
    /// at least two humps — a one-hump query is a prefix or substring search.
    /// </summary>
    public static bool IsCamelHumpMatch(string name, string query)
    {
        var queryHumps = SplitQuery(query);
        if (queryHumps.Count < 2) return false;

        var nameHumps = SplitHumps(name);
        return nameHumps.Count >= queryHumps.Count
            && nameHumps[0].StartsWith(queryHumps[0], StringComparison.OrdinalIgnoreCase)
            && Match(nameHumps, 1, queryHumps, 1);
    }

    private static bool Match(List<string> nameHumps, int n, List<string> queryHumps, int q)
    {
        if (q == queryHumps.Count) return true;

        for (var i = n; i <= nameHumps.Count - (queryHumps.Count - q); i++)
        {
            if (nameHumps[i].StartsWith(queryHumps[q], StringComparison.OrdinalIgnoreCase)
                && Match(nameHumps, i + 1, queryHumps, q + 1))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Splits "GetHTTPResponse2Async" into Get, H, T, T, P, Response, 2, Async.</summary>
    internal static List<string> SplitHumps(string name)
    {
        var humps = new List<string>();
        var start = -1;

        for (var i = 0; i < name.Length; i++)
        {
            var ch = name[i];
            if (ch is '_' or '@')
            {
                if (start >= 0) humps.Add(name[start..i]);
                start = -1;
                continue;
            }

            var boundary = start < 0
                || char.IsUpper(ch)
                || (char.IsDigit(ch) && !char.IsDigit(name[i - 1]))
                || (!char.IsDigit(ch) && char.IsDigit(name[i - 1]));

            if (boundary)
            {
                if (start >= 0) humps.Add(name[start..i]);
                start = i;
            }
        }

        if (start >= 0) humps.Add(name[start..]);
        return humps;
    }

    private static List<string> SplitQuery(string query)
    {
        var trimmed = query.Trim();
        if (trimmed.Any(char.IsUpper))
            return SplitHumps(trimmed);

        return [.. trimmed.Where(char.IsLetterOrDigit).Select(c => c.ToString())];
    }

    /// <summary>
    /// Levenshtein distance, abandoned once it must exceed <paramref name="max"/>. Used for
    /// typo suggestions, where only small distances matter.
    /// </summary>
    public static int BoundedEditDistance(string a, string b, int max)
    {
        if (Math.Abs(a.Length - b.Length) > max) return max + 1;

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            var rowMin = current[0];
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + cost);
                rowMin = Math.Min(rowMin, current[j]);
            }

            if (rowMin > max) return max + 1;
            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
