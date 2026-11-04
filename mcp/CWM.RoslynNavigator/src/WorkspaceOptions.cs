using System.Globalization;
using CWM.RoslynNavigator.Caching;

namespace CWM.RoslynNavigator;

/// <summary>Command-line switches that tune how the workspace loads. Unknown arguments are ignored.</summary>
internal static class WorkspaceOptions
{
    public const string LoadParallelismFlag = "--load-parallelism";
    public const string AllTargetFrameworksFlag = "--all-target-frameworks";
    public const string NoLoadCacheFlag = "--no-load-cache";

    /// <summary>
    /// Applies the switches in <paramref name="args"/> to <paramref name="workspace"/>.
    /// <c>--load-parallelism N</c>: build hosts for the solution load (0 = automatic, 1 = off).
    /// <c>--all-target-frameworks</c>: keep every target framework of multi-targeted projects.
    /// <c>--no-load-cache</c>: always load with MSBuild; never read or write the load cache.
    /// </summary>
    public static void Apply(string[] args, WorkspaceManager workspace, Func<string, string?>? readVariable = null)
    {
        if (ReadInt(args, LoadParallelismFlag) is { } parallelism)
            workspace.LoadParallelism = Math.Max(0, parallelism);
        if (HasFlag(args, AllTargetFrameworksFlag))
            workspace.AllTargetFrameworks = true;
        workspace.LoadCache = HasFlag(args, NoLoadCacheFlag) ? null : LoadCacheStore.FromEnvironment(readVariable);
    }

    private static bool HasFlag(string[] args, string flag) =>
        args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

    internal static int? ReadInt(string[] args, string flag)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }
        }

        return null;
    }
}
