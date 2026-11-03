namespace CWM.RoslynNavigator.Bench.Measurement;

/// <summary>Where the "without navigator" file-read floor comes from.</summary>
internal enum BaselineSource
{
    /// <summary>Files the navigator's answer names (File/TestFile) — BaselineCalculator's rule.</summary>
    ResponseFiles,

    /// <summary>Project files the project graph names: every .csproj an agent would open.</summary>
    ResponseProjectPaths,

    /// <summary>A file known up front (outline, diagnostics): the agent reads it regardless.</summary>
    FixedFiles,
}

/// <summary>
/// A realistic agent task, answered two ways: one navigator call, or the manual route an
/// agent takes with Grep and Read. The navigator side reuses the measured tool call with
/// the same id; the manual side is a search plus the file-read floor.
/// </summary>
/// <param name="GrepPattern">Null when the manual route needs no search (the file is already known).</param>
internal sealed record TaskCase(
    string Id,
    string Title,
    string Tool,
    string? GrepPattern,
    string GrepGlob,
    BaselineSource Baseline,
    List<string> FixedFiles);
