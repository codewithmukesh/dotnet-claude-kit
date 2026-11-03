namespace CWM.RoslynNavigator;

/// <summary>
/// One solution in the workspace: its file, the loadable projects it declares (absolute,
/// normalized paths), and why it failed to load, if it did. Projects shared with another
/// solution appear in both, but are loaded into the workspace once.
/// </summary>
public sealed record LoadedSolution(string Path, IReadOnlyList<string> ProjectPaths, string? Error)
{
    /// <summary>File name of the solution, e.g. "Orders.sln" — the name tools report.</summary>
    public string Name => System.IO.Path.GetFileName(Path);
}
