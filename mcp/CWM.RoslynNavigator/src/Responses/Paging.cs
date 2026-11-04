namespace CWM.RoslynNavigator.Responses;

/// <summary>
/// Applies a result cap and describes the resulting page. Centralised so every
/// list-returning tool reports truncation identically — a caller that sees
/// Truncated=false knows it has the complete set without comparing counts.
/// <para>
/// Paging is offset based: a page starts at <c>offset</c> and, when more entries follow,
/// carries <c>NextOffset</c> — the exact value to pass back for the next page. Offsets index
/// the tool's full, deterministically ordered result list, so they are stable across calls
/// against the same workspace snapshot.
/// </para>
/// </summary>
internal static class Paging
{
    internal readonly record struct Page<T>(
        List<T> Items, int Count, int TotalFound, bool Truncated, int Limit, int Offset = 0, int? NextOffset = null)
    {
        /// <summary>The first <paramref name="count"/> items of this page, with paging recomputed.</summary>
        public Page<T> Take(int count)
        {
            if (count >= Count) return this;
            var items = Items.Take(Math.Max(0, count)).ToList();
            return this with { Items = items, Count = items.Count, Truncated = true, NextOffset = Offset + items.Count };
        }

        /// <summary>The same page over <paramref name="items"/> — this page's entries, converted one to one.</summary>
        public Page<TOut> WithItems<TOut>(List<TOut> items) =>
            new(items, Count, TotalFound, Truncated, Limit, Offset, NextOffset);
    }

    public static Page<T> Apply<T>(IReadOnlyList<T> all, int maxResults, int offset = 0)
    {
        var limit = Math.Max(1, maxResults);
        var start = Math.Clamp(offset, 0, all.Count);
        var items = all.Skip(start).Take(limit).ToList();
        var end = start + items.Count;
        var truncated = end < all.Count;

        return new Page<T>(items, items.Count, all.Count, truncated, limit, start, truncated ? end : null);
    }

    /// <summary>
    /// Removes entries that share <paramref name="key"/> before capping, keeping the first of
    /// each, so TotalFound counts distinct results and a duplicate never uses up the cap.
    /// Multi-targeted projects are the usual source: Roslyn loads one project per target
    /// framework, and every search returns the same source location once per framework.
    /// </summary>
    public static Page<T> Apply<T, TKey>(IEnumerable<T> all, int maxResults, Func<T, TKey> key, int offset = 0) =>
        Apply(Distinct(all, key), maxResults, offset);

    /// <summary>First entry per key, in input order.</summary>
    public static List<T> Distinct<T, TKey>(IEnumerable<T> all, Func<T, TKey> key)
    {
        var seen = new HashSet<TKey>();
        var distinct = new List<T>();
        foreach (var item in all)
        {
            if (seen.Add(key(item)))
                distinct.Add(item);
        }

        return distinct;
    }

    /// <summary>
    /// Page describing an empty result set — used on the early-return paths where no
    /// query ran at all.
    /// </summary>
    public static Page<T> Empty<T>(int maxResults) =>
        new([], 0, 0, false, Math.Max(1, maxResults));

    /// <summary>
    /// Serializes <paramref name="page"/>, dropping entries from its end until the response fits
    /// <paramref name="budget"/> characters (<see cref="ResponseBudget.MaxChars"/> by default).
    /// A shortened page is Truncated with NextOffset pointing at the first dropped entry, so the
    /// caller pages on instead of losing the tail. At least one entry is always kept.
    /// </summary>
    public static string Fit<T>(Page<T> page, Func<Page<T>, string> serialize, int budget = ResponseBudget.MaxChars)
    {
        var json = serialize(page);
        if (json.Length <= budget || page.Count <= 1)
            return json;

        // Largest prefix that fits: response size grows monotonically with the item count.
        var best = serialize(page.Take(1));
        int low = 2, high = page.Count - 1;
        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            var candidate = serialize(page.Take(mid));
            if (candidate.Length <= budget)
            {
                best = candidate;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return best;
    }
}
