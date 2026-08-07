namespace CWM.RoslynNavigator.Telemetry;

/// <summary>
/// Telemetry configuration, resolved from the environment.
///
/// Collection is <b>off unless explicitly enabled</b>. This is a published NuGet package;
/// a measurement someone opted into is defensible in a way that one they never knew about
/// is not, and the numbers this feeds are meant to back a public claim.
///
/// Nothing is ever transmitted. Records are written to the local machine only, carry no
/// identity, and no component in this assembly opens a network connection.
/// </summary>
internal sealed record TelemetryOptions(bool Enabled, string RootDirectory)
{
    public const string EnvironmentVariable = "CWM_ROSLYN_TELEMETRY";

    private const string DirectoryName = "cwm-roslyn-navigator";

    public static TelemetryOptions FromEnvironment(
        Func<string, string?>? readVariable = null,
        string? localAppDataOverride = null)
    {
        readVariable ??= Environment.GetEnvironmentVariable;

        var enabled = IsEnabled(readVariable(EnvironmentVariable));

        var root = localAppDataOverride
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                DirectoryName);

        return new TelemetryOptions(enabled, root);
    }

    /// <summary>
    /// Only an explicit affirmative turns collection on. An unset, empty, or unrecognized
    /// value leaves it off — the safe direction for an unrecognized value.
    /// </summary>
    private static bool IsEnabled(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "on" or "1" or "true" or "yes" or "enabled" => true,
            _ => false
        };
}
