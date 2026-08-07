namespace CWM.RoslynNavigator.Stats;

/// <summary>
/// Joins the two data sources, which identify a repository differently: telemetry knows a
/// solution file path, transcripts know a working directory.
///
/// <para>The design notes called this the most fragile part of the system, on the
/// assumption that the repository would have to be recovered by decoding a transcript
/// directory name like <c>C--Users-mukesh-repos-codewithmukesh-dotnet-claude-kit</c>. That
/// decode is genuinely ambiguous — a hyphen in the name is indistinguishable from a path
/// separator, and <c>dotnet-claude-kit</c> contains both.</para>
///
/// <para>It turns out not to be necessary: every transcript line carries a <c>cwd</c>
/// field with the real, unmangled path. Reading it from the content removes the ambiguity
/// entirely, and the directory-name decode survives only as a last resort for a transcript
/// whose lines somehow carry no cwd.</para>
/// </summary>
internal static class RepoPath
{
    /// <summary>Lower-cased, forward-slashed, trailing-separator-free form for comparison.</summary>
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        var normalized = path.Replace('\\', '/').TrimEnd('/');
        return normalized.ToLowerInvariant();
    }

    /// <summary>
    /// Whether two paths refer to the same repository. One being an ancestor of the other
    /// counts: a session may run from a subdirectory of the solution root, or from a parent
    /// of it in a multi-solution layout.
    /// </summary>
    public static bool SameRepository(string left, string right)
    {
        var a = Normalize(left);
        var b = Normalize(right);

        if (a.Length == 0 || b.Length == 0) return false;
        if (a == b) return true;

        return IsAncestorOf(a, b) || IsAncestorOf(b, a);
    }

    /// <summary>Segment-aware containment, so /repo/app does not match /repo/app-legacy.</summary>
    private static bool IsAncestorOf(string ancestor, string descendant) =>
        descendant.Length > ancestor.Length
        && descendant.StartsWith(ancestor, StringComparison.Ordinal)
        && descendant[ancestor.Length] == '/';

    /// <summary>
    /// Best-effort recovery of a path from a transcript directory name. Only used when no
    /// line in the transcript carried a cwd, because the encoding is lossy: every path
    /// separator and every literal hyphen both became '-'.
    /// </summary>
    public static string? FromTranscriptDirectoryName(string directoryName)
    {
        if (string.IsNullOrWhiteSpace(directoryName)) return null;

        // Windows form: "C--Users-mukesh-repos-x" -> drive letter, then segments.
        if (directoryName.Length > 3 && directoryName[1] == '-' && directoryName[2] == '-'
            && char.IsAsciiLetter(directoryName[0]))
        {
            return $"{directoryName[0]}:/{directoryName[3..].Replace('-', '/')}";
        }

        // POSIX form: leading '-' stands in for the root slash.
        if (directoryName.StartsWith('-'))
            return "/" + directoryName.TrimStart('-').Replace('-', '/');

        return null;
    }

    /// <summary>Display name for a repository — the last segment of its path.</summary>
    public static string DisplayName(string path)
    {
        var normalized = Normalize(path);
        if (normalized.Length == 0) return "(unknown)";

        var slash = normalized.LastIndexOf('/');
        return slash >= 0 && slash < normalized.Length - 1 ? normalized[(slash + 1)..] : normalized;
    }
}
