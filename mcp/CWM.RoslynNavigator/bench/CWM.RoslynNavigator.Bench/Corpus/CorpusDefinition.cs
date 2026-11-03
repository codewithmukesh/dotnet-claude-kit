namespace CWM.RoslynNavigator.Bench.Corpus;

/// <summary>
/// One checked-in corpus: where to clone it, which commit, which solution to open, and the
/// symbols the benchmark asks about. Committed as <c>bench/corpus/&lt;name&gt;.json</c> so a
/// run is reproducible without re-running target selection.
/// </summary>
internal sealed record CorpusDefinition
{
    public required string Name { get; init; }

    /// <summary>small, medium, large, or xl.</summary>
    public required string Tier { get; init; }

    public required string Repository { get; init; }

    /// <summary>Exact commit the targets were selected against. Clone this, not a branch.</summary>
    public required string Commit { get; init; }

    /// <summary>Folder name under the corpus root that holds the clone.</summary>
    public required string CloneDirectory { get; init; }

    /// <summary>Solution path relative to the clone root.</summary>
    public required string Solution { get; init; }

    /// <summary>The restore command that must succeed before a run, relative to the clone root.</summary>
    public required string Restore { get; init; }

    /// <summary>Anything a reproducer must know: projects that cannot load, local tweaks.</summary>
    public List<string> Notes { get; init; } = [];

    /// <summary>Null until <c>select</c> has run against this corpus.</summary>
    public CorpusTargets? Targets { get; init; }
}
