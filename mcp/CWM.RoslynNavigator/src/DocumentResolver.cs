using Microsoft.CodeAnalysis;

namespace CWM.RoslynNavigator;

/// <summary>
/// Resolves a caller-supplied file path to documents in the loaded solution. Shared by every
/// file-addressed tool so they agree on what "this file" means and on when it does not exist.
/// </summary>
internal static class DocumentResolver
{
    /// <summary>
    /// Returns every document whose path equals <paramref name="path"/> or ends with it on a
    /// directory boundary. Backslashes and leading "./" are normalized, so "./App/Program.cs",
    /// ".\App\Program.cs" and "App/Program.cs" are equivalent. Ordered by path for stable output.
    /// </summary>
    public static IReadOnlyList<Document> FindDocuments(Solution solution, string path)
    {
        var needle = Normalize(path);
        if (needle.Length == 0)
            return [];

        return solution.Projects
            .SelectMany(p => p.Documents)
            .Where(d => d.FilePath is { } fp && Matches(Normalize(fp), needle))
            .OrderBy(d => d.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// True when <paramref name="filePath"/> is the file <paramref name="requestedPath"/> names:
    /// equal to it or ending with it on a directory boundary, separators normalized. So
    /// "SampleDomain/IOrderRepository.cs" matches a Windows path, and "Service.cs" does not
    /// match "OrderService.cs".
    /// </summary>
    public static bool PathMatches(string filePath, string requestedPath)
    {
        var needle = Normalize(requestedPath);
        return needle.Length > 0 && Matches(Normalize(filePath), needle);
    }

    internal static string Normalize(string path)
    {
        var normalized = path.Trim().Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..].TrimStart('/');
        return normalized;
    }

    private static bool Matches(string documentPath, string needle)
    {
        if (!documentPath.EndsWith(needle, StringComparison.OrdinalIgnoreCase))
            return false;

        var start = documentPath.Length - needle.Length;
        return start == 0 || needle.StartsWith('/') || documentPath[start - 1] == '/';
    }
}
