namespace CWM.RoslynNavigator.Bench.Server;

/// <summary>
/// A server build to benchmark, given on the command line as <c>label=path</c>. A path to a
/// <c>.dll</c> runs under the <c>dotnet</c> host (a source build); anything else runs
/// directly (a <c>dotnet tool install --tool-path</c> shim).
/// </summary>
internal sealed record ServerBuild(string Label, string Path)
{
    /// <summary>Extra server switches (e.g. <c>--load-parallelism 1</c>) appended after the solution.</summary>
    public IReadOnlyList<string> ExtraArguments { get; init; } = [];

    public static ServerBuild Parse(string spec)
    {
        var separator = spec.IndexOf('=');
        if (separator <= 0 || separator == spec.Length - 1)
            throw new ArgumentException($"Expected label=path, got '{spec}'.");

        var path = System.IO.Path.GetFullPath(spec[(separator + 1)..]);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Server build not found: {path}", path);

        return new ServerBuild(spec[..separator], path);
    }

    public bool RunsUnderHost => Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

    /// <summary>The executable to start. An absolute path, so no shell wrapper is involved.</summary>
    public string Command => RunsUnderHost ? DotnetHost.Resolve() : Path;

    public IList<string> Arguments(string solutionPath) =>
        RunsUnderHost
            ? [Path, "--solution", solutionPath, .. ExtraArguments]
            : ["--solution", solutionPath, .. ExtraArguments];
}
