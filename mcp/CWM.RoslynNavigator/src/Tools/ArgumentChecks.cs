using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using Microsoft.CodeAnalysis;

namespace CWM.RoslynNavigator.Tools;

/// <summary>
/// Shared validation for enumerated string arguments. An unrecognised value used to fall
/// through to the default branch silently, so a typo ('galaxy', 'warnings') returned a full,
/// plausible answer to a question the caller never asked.
/// </summary>
internal static class ArgumentChecks
{
    /// <summary>
    /// Null when <paramref name="value"/> is one of <paramref name="allowed"/> (ignoring case);
    /// otherwise a serialized InvalidArgument error naming the accepted values.
    /// </summary>
    public static string? Choice(string parameter, string value, params string[] allowed)
    {
        if (allowed.Any(option => string.Equals(option, value, StringComparison.OrdinalIgnoreCase)))
            return null;

        return JsonSerializer.Serialize(new ErrorResponse(ErrorCodes.InvalidArgument,
            $"Unknown {parameter} '{value}'. Use {string.Join(", ", allowed.Select(a => $"'{a}'"))}."));
    }

    /// <summary>Serialized FileNotFound error for a file filter that matches no document.</summary>
    public static string FileFilterMatchesNothing(string file) =>
        JsonSerializer.Serialize(new ErrorResponse(ErrorCodes.FileNotFound,
            $"No document path in the solution contains '{file}'."));

    /// <summary>
    /// Null when <paramref name="name"/> is null or some project satisfies
    /// <paramref name="matches"/> (default: name equality, ignoring case); otherwise a
    /// serialized InvalidArgument error listing the loaded projects. An unknown project filter
    /// used to yield an empty answer indistinguishable from a real project with no findings.
    /// </summary>
    public static string? UnknownProject(Solution solution, string? name, Func<Project, bool>? matches = null)
    {
        if (name is null)
            return null;

        matches ??= p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase);
        if (solution.Projects.Any(matches))
            return null;

        var names = solution.Projects.Select(p => p.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        return JsonSerializer.Serialize(new ErrorResponse(ErrorCodes.InvalidArgument,
            $"No project named '{name}'. Projects: {string.Join(", ", names)}."));
    }
}
