namespace CWM.RoslynNavigator.Telemetry;

/// <summary>
/// Converts byte counts to token estimates.
///
/// <para><b>The divisor is an estimate, not a measurement.</b> C# source is denser than
/// English prose — more punctuation, more short identifiers, fewer whole words — so it sits
/// below the ~4 bytes/token commonly cited for English. 3.5 is a reasoned starting point,
/// not a calibrated one.</para>
///
/// <para>Calibrating it means sampling real <c>.cs</c> files, submitting each to the
/// Anthropic <c>count_tokens</c> endpoint for an exact count, and regressing byte length
/// against it. Until that runs, <see cref="IsCalibrated"/> is false and every report says
/// so — a savings figure is only as defensible as this constant, and claiming precision it
/// has not earned would undermine the whole exercise.</para>
/// </summary>
internal static class TokenEstimator
{
    /// <summary>Bytes of C# source per token. See the class remarks before changing.</summary>
    public const double BytesPerToken = 3.5;

    /// <summary>
    /// Whether <see cref="BytesPerToken"/> has been derived from measured token counts.
    /// Surfaced in every report so no consumer mistakes an estimate for a measurement.
    /// </summary>
    public const bool IsCalibrated = false;

    /// <summary>Sample size behind the divisor. Zero while uncalibrated.</summary>
    public const int CalibrationSampleSize = 0;

    public static int FromBytes(long bytes)
    {
        if (bytes <= 0) return 0;

        var tokens = Math.Round(bytes / BytesPerToken, MidpointRounding.AwayFromZero);

        // A solution large enough to overflow int tokens is not one we can meaningfully
        // report on, but saturating beats wrapping to a negative.
        return tokens >= int.MaxValue ? int.MaxValue : (int)tokens;
    }

    /// <summary>
    /// Tokens in an already-materialized response string. UTF-8 byte length rather than
    /// character count, so multi-byte identifiers are not undercounted.
    /// </summary>
    public static int FromText(string? text) =>
        string.IsNullOrEmpty(text) ? 0 : FromBytes(System.Text.Encoding.UTF8.GetByteCount(text));
}
