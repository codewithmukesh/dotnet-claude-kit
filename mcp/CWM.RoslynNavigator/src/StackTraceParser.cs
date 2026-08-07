using System.Text.RegularExpressions;

namespace CWM.RoslynNavigator;

/// <summary>
/// Parses .NET exception output into frames that can be resolved against the solution.
///
/// The awkward part is that the compiler rewrites async methods, lambdas, and local
/// functions into generated types whose names do not exist in source. A frame reading
/// <c>OrderService+&lt;CreateAsync&gt;d__5.MoveNext()</c> has to be mapped back to
/// <c>OrderService.CreateAsync</c> before any symbol lookup can succeed.
/// </summary>
internal static partial class StackTraceParser
{
    /// <summary>A single "at ..." line, normalized back to a source-level name.</summary>
    internal sealed record ParsedFrame(string RawMethod, string QualifiedName, string? File, int? Line);

    /// <summary>The exception header, when the input includes one.</summary>
    internal sealed record ParsedHeader(string ExceptionType, string Message);

    [GeneratedRegex(@"^\s*at\s+(?<method>.+?)(?:\s+in\s+(?<file>.+?):line\s+(?<line>\d+))?\s*$",
        RegexOptions.ExplicitCapture)]
    private static partial Regex FrameLine { get; }

    [GeneratedRegex(@"^\s*(?<type>[\w.`+]*(?:Exception|Error))\s*:\s*(?<message>.+?)\s*$",
        RegexOptions.ExplicitCapture)]
    private static partial Regex HeaderLine { get; }

    /// <summary>Async state machine: <c>Type+&lt;Method&gt;d__12.MoveNext</c>.</summary>
    [GeneratedRegex(@"^(?<owner>.+)\+<(?<name>[^>]+)>d__\d+\.MoveNext$", RegexOptions.ExplicitCapture)]
    private static partial Regex AsyncStateMachine { get; }

    /// <summary>Lambda, optionally hoisted into a display class: <c>Type.&lt;&gt;c.&lt;Method&gt;b__3_0</c>.</summary>
    [GeneratedRegex(@"^(?<owner>.+?)(?:\.<>c(?:__DisplayClass[\w]*)?)?\.<(?<name>[^>]+)>b__[\w|]+$",
        RegexOptions.ExplicitCapture)]
    private static partial Regex Lambda { get; }

    /// <summary>Local function: <c>Type.&lt;Outer&gt;g__Inner|3_0</c>. Resolves to the enclosing method.</summary>
    [GeneratedRegex(@"^(?<owner>.+)\.<(?<name>[^>]+)>g__[\w]+\|[\w]+$", RegexOptions.ExplicitCapture)]
    private static partial Regex LocalFunction { get; }

    /// <summary>Generic method arity in a frame: <c>Select[TSource,TResult]</c>.</summary>
    [GeneratedRegex(@"\[[^\]]*\]", RegexOptions.ExplicitCapture)]
    private static partial Regex GenericArity { get; }

    public static ParsedHeader? ParseHeader(string stackTrace)
    {
        foreach (var line in EnumerateLines(stackTrace))
        {
            // The header precedes the frames; anything after the first "at " is not one.
            if (FrameLine.IsMatch(line)) return null;

            var match = HeaderLine.Match(line);
            if (match.Success)
                return new ParsedHeader(match.Groups["type"].Value, match.Groups["message"].Value);
        }

        return null;
    }

    public static List<ParsedFrame> ParseFrames(string stackTrace)
    {
        var frames = new List<ParsedFrame>();

        foreach (var line in EnumerateLines(stackTrace))
        {
            var match = FrameLine.Match(line);
            if (!match.Success) continue;

            var rawMethod = match.Groups["method"].Value.Trim();
            if (rawMethod.Length == 0) continue;

            var file = match.Groups["file"].Success ? match.Groups["file"].Value.Trim() : null;
            int? lineNumber = match.Groups["line"].Success && int.TryParse(match.Groups["line"].Value, out var n)
                ? n
                : null;

            frames.Add(new ParsedFrame(rawMethod, NormalizeMethod(rawMethod), file, lineNumber));
        }

        return frames;
    }

    /// <summary>
    /// Reduces a frame's method text to the qualified source name it came from, undoing
    /// compiler rewrites for async methods, lambdas, and local functions.
    /// </summary>
    public static string NormalizeMethod(string rawMethod)
    {
        var text = rawMethod.Trim();

        // Drop the parameter list — it carries types we cannot match on reliably.
        var paren = text.IndexOf('(');
        if (paren >= 0) text = text[..paren];

        text = GenericArity.Replace(text, string.Empty).Trim();

        if (AsyncStateMachine.Match(text) is { Success: true } async)
            text = $"{async.Groups["owner"].Value}.{async.Groups["name"].Value}";
        else if (LocalFunction.Match(text) is { Success: true } local)
            text = $"{local.Groups["owner"].Value}.{local.Groups["name"].Value}";
        else if (Lambda.Match(text) is { Success: true } lambda)
            text = $"{lambda.Groups["owner"].Value}.{lambda.Groups["name"].Value}";

        // Nested types use '+' in metadata but '.' in source.
        text = text.Replace('+', '.');

        // Constructors appear as .ctor/.cctor; the type name is the useful anchor.
        if (text.EndsWith("..ctor", StringComparison.Ordinal))
            text = text[..^6];
        else if (text.EndsWith("..cctor", StringComparison.Ordinal))
            text = text[..^7];

        return text.Trim('.');
    }

    private static IEnumerable<string> EnumerateLines(string text)
    {
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
            yield return line;
    }
}
