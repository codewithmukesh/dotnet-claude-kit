namespace CWM.RoslynNavigator;

/// <summary>
/// How file-system paths compare on this machine. Windows (NTFS) and macOS (APFS and HFS+ in
/// their default configuration) are case-insensitive, so <c>src/Foo.cs</c> and
/// <c>SRC/foo.cs</c> are the same file there. Linux file systems are case-sensitive: those are
/// two different files, and comparing them case-insensitively merges distinct projects,
/// documents, and directories.
///
/// <para>Decided by OS rather than probed per volume. A case-sensitive APFS volume or a
/// case-insensitive mount on Linux is the rare exception, and the cost of being wrong there
/// is the same as before this helper existed.</para>
/// </summary>
internal static class PathComparison
{
    /// <summary>True on Windows and macOS.</summary>
    public static bool IgnoreCase { get; } = IsCaseInsensitive(
        OperatingSystem.IsWindows(), OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst());

    /// <summary>Use for <c>Equals</c>, <c>StartsWith</c>, and <c>Contains</c> on paths.</summary>
    public static StringComparison Comparison => ComparisonFor(IgnoreCase);

    /// <summary>Use for dictionaries, sets, and <c>Distinct</c> keyed by path.</summary>
    public static StringComparer Comparer => ComparerFor(IgnoreCase);

    internal static bool IsCaseInsensitive(bool isWindows, bool isMacOS) => isWindows || isMacOS;

    internal static StringComparison ComparisonFor(bool ignoreCase) =>
        ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    internal static StringComparer ComparerFor(bool ignoreCase) =>
        ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
