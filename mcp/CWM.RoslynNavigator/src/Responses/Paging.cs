namespace CWM.RoslynNavigator.Responses;

/// <summary>
/// Applies a result cap and describes the resulting page. Centralised so every
/// list-returning tool reports truncation identically — a caller that sees
/// Truncated=false knows it has the complete set without comparing counts.
/// </summary>
internal static class Paging
{
    internal readonly record struct Page<T>(List<T> Items, int Count, int TotalFound, bool Truncated, int Limit);

    public static Page<T> Apply<T>(IReadOnlyList<T> all, int maxResults)
    {
        var limit = Math.Max(1, maxResults);
        var truncated = all.Count > limit;
        var items = truncated ? [.. all.Take(limit)] : new List<T>(all);

        return new Page<T>(items, items.Count, all.Count, truncated, limit);
    }

    /// <summary>
    /// Page describing an empty result set — used on the early-return paths where no
    /// query ran at all.
    /// </summary>
    public static Page<T> Empty<T>(int maxResults) =>
        new([], 0, 0, false, Math.Max(1, maxResults));
}
