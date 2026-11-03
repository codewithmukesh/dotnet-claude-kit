namespace CWM.RoslynNavigator.Bench.Corpus;

/// <summary>
/// The concrete symbols and files every tool call and task is asked about. Produced once by
/// <see cref="TargetSelector"/> and committed, so later runs ask identical questions.
/// </summary>
internal sealed record CorpusTargets
{
    /// <summary>Most-referenced class among the sampled candidates.</summary>
    public required string TypeName { get; init; }

    public required string TypeFile { get; init; }

    /// <summary>Most-implemented interface among the sampled candidates.</summary>
    public required string InterfaceName { get; init; }

    /// <summary>Abstract class with the most derived types among the sampled candidates.</summary>
    public required string BaseClassName { get; init; }

    /// <summary>Containing type of <see cref="MethodName"/>.</summary>
    public required string MethodType { get; init; }

    /// <summary>Method with the most callers among the sampled candidates.</summary>
    public required string MethodName { get; init; }

    public required string MethodFile { get; init; }

    public required int MethodLine { get; init; }

    /// <summary>Virtual or abstract method with the most overrides, or null when none qualified.</summary>
    public string? VirtualMethodName { get; init; }

    public string? VirtualMethodClass { get; init; }

    /// <summary>Largest hand-written source file the server can outline.</summary>
    public required string LargeFile { get; init; }

    public required string DiagnosticsFile { get; init; }

    /// <summary>A synthetic but realistic trace: the target method under its real callers.</summary>
    public required string StackTrace { get; init; }

    /// <summary>Method names in <see cref="StackTrace"/>'s solution frames — what a manual search greps for.</summary>
    public required List<string> StackMethods { get; init; }

    /// <summary>Which server build the selection ran against.</summary>
    public required string SelectedWith { get; init; }

    /// <summary>Every candidate the selector scored, so the choice can be audited.</summary>
    public List<string> SelectionLog { get; init; } = [];
}
