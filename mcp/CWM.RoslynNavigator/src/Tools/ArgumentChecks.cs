using System.Text.Json;
using CWM.RoslynNavigator.Responses;

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
}
