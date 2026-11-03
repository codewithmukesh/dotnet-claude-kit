using System.Globalization;

namespace CWM.RoslynNavigator;

/// <summary>Command-line switches that tune how the workspace loads. Unknown arguments are ignored.</summary>
internal static class WorkspaceOptions
{
    public const string LoadParallelismFlag = "--load-parallelism";
    public const string AllTargetFrameworksFlag = "--all-target-frameworks";

    /// <summary>
    /// Applies the switches in <paramref name="args"/> to <paramref name="workspace"/>.
    /// <c>--load-parallelism N</c>: build hosts for the solution load (0 = automatic, 1 = off).
    /// <c>--all-target-frameworks</c>: keep every target framework of multi-targeted projects.
    /// </summary>
    public static void Apply(string[] args, WorkspaceManager workspace)
    {
        if (ReadInt(args, LoadParallelismFlag) is { } parallelism)
            workspace.LoadParallelism = Math.Max(0, parallelism);
        if (args.Any(a => string.Equals(a, AllTargetFrameworksFlag, StringComparison.OrdinalIgnoreCase)))
            workspace.AllTargetFrameworks = true;
    }

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
