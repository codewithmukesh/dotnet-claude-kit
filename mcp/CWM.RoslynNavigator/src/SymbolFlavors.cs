using Microsoft.CodeAnalysis;

namespace CWM.RoslynNavigator;

/// <summary>
/// One source symbol as every project that compiles it sees it.
/// <para>
/// A multi-targeted project loads once per framework kept for the solution ("Volo.Abp.Core(net9.0)",
/// "Volo.Abp.Core(net10.0)"), and each flavor's compilation has its own symbol for every type.
/// A consumer compiles against one flavor, so its <c>class X : AbpModule</c> derives from that
/// flavor's symbol. <c>SymbolFinder.FindReferencesAsync</c> cascades across such linked symbols,
/// but <c>FindDerivedClassesAsync</c>, <c>FindImplementationsAsync</c>, <c>FindOverridesAsync</c>
/// and <c>FindCallersAsync</c> do not: seeded with the net9.0 <c>AbpModule</c>, the hierarchy
/// listed the three AbpModule subclasses compiled against net9.0 and none of the hundreds built
/// against net10.0. Searching from every flavor and merging closes that gap.
/// </para>
/// </summary>
internal static class SymbolFlavors
{
    /// <summary>
    /// <paramref name="symbol"/> first, then its counterpart in each other project whose
    /// compilation contains the same declaring file. Just the symbol when it has no source or
    /// no other project compiles it.
    /// </summary>
    public static async Task<IReadOnlyList<T>> ExpandAsync<T>(T symbol, Solution solution, CancellationToken ct)
        where T : class, ISymbol
    {
        var tree = symbol.Locations.FirstOrDefault(l => l.IsInSource)?.SourceTree;
        if (tree?.FilePath is not { Length: > 0 } path
            || DocumentationCommentId.CreateDeclarationId(symbol) is not { } id)
        {
            return [symbol];
        }

        var projects = solution.GetDocumentIdsWithFilePath(path).Select(d => d.ProjectId).Distinct().ToList();
        if (projects.Count <= 1)
            return [symbol];

        var flavors = new List<T> { symbol };
        foreach (var projectId in projects)
        {
            if (solution.GetProject(projectId) is not { } project
                || await project.GetCompilationAsync(ct) is not { } compilation
                || compilation.Assembly.Equals(symbol.ContainingAssembly, SymbolEqualityComparer.Default))
            {
                continue;
            }

            if (DocumentationCommentId.GetFirstSymbolForDeclarationId(id, compilation) is T counterpart)
                flavors.Add(counterpart);
        }

        return flavors;
    }

    /// <summary>Runs <paramref name="search"/> from every flavor of <paramref name="symbol"/> and concatenates the results.</summary>
    public static async Task<List<TResult>> SearchAllAsync<T, TResult>(
        T symbol, Solution solution, Func<T, Task<IEnumerable<TResult>>> search, CancellationToken ct)
        where T : class, ISymbol
    {
        var results = new List<TResult>();
        foreach (var flavor in await ExpandAsync(symbol, solution, ct))
            results.AddRange(await search(flavor));

        return results;
    }
}
