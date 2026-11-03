namespace CWM.RoslynNavigator.Bench.Measurement;

/// <summary>One tool and the exact arguments it is called with on a corpus.</summary>
internal sealed record ToolCase(string Tool, Dictionary<string, object?> Arguments);
