using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CWM.RoslynNavigator.Analyzers;

/// <summary>
/// AP006: Detects string interpolation or concatenation in the message template of a logging call.
/// Interpolated strings bypass structured logging, allocate even when the log level is disabled,
/// and prevent log aggregation tools from grouping related messages.
///
/// Only the message-template argument is inspected — concatenation inside a <em>value</em>
/// argument is ordinary code. Templates that the compiler folds to a constant (adjacent string
/// literals wrapping a long template across lines, or a const prefix) are not violations:
/// they produce exactly one template string, which is the goal.
/// </summary>
internal sealed class LoggingInterpolationDetector : IAntiPatternDetector
{
    private static readonly HashSet<string> LogMethods = new(StringComparer.Ordinal)
    {
        "LogTrace", "LogDebug", "LogInformation",
        "LogWarning", "LogError", "LogCritical"
    };

    // Semantic, so that constant folding can be evaluated exactly rather than guessed.
    public bool RequiresSemanticModel => true;

    public SourceKind AppliesTo => SourceKind.Production;

    public IEnumerable<AntiPatternViolation> Detect(DetectionContext context)
    {
        var model = context.Model;
        if (model is null)
            yield break;

        foreach (var invocation in context.Root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            context.Ct.ThrowIfCancellationRequested();

            var methodName = AnalyzerHelpers.InvokedMethodName(invocation);
            if (methodName is null || !LogMethods.Contains(methodName))
                continue;

            var template = FindTemplateArgument(invocation);
            if (template is null)
                continue;

            var (message, snippetSource) = template switch
            {
                InterpolatedStringExpressionSyntax => (
                    $"String interpolation in {methodName}() bypasses structured logging",
                    template),
                BinaryExpressionSyntax => (
                    $"String concatenation in {methodName}() bypasses structured logging",
                    template),
                _ => (null, null)
            };

            if (message is null || snippetSource is null)
                continue;

            // A template the compiler folds to a single constant is correct code, whether it
            // was written as adjacent literals or as a const reference. Asked only now: a plain
            // literal template (most log calls) never needs the method body bound.
            if (model.GetConstantValue(template, context.Ct).HasValue)
                continue;

            var line = snippetSource.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            var snippet = snippetSource.ToString();
            if (snippet.Length > 60) snippet = snippet[..57] + "...";

            yield return new AntiPatternViolation(
                Id: "AP006",
                Severity: AntiPatternSeverity.Warning,
                Message: message,
                File: context.FilePath,
                Line: line,
                Snippet: snippet,
                Suggestion: $"Use message template: {methodName}(\"Message {{Param}}\", value)",
                Confidence: AntiPatternConfidence.High,
                Member: AnalyzerHelpers.EnclosingMember(invocation));
        }
    }

    /// <summary>
    /// The message-template argument. Normally argument 0, but the
    /// <c>Log*(Exception, string, params object[])</c> and <c>Log*(EventId, string, ...)</c>
    /// overloads push it to argument 1. Returns null when no argument is string-shaped —
    /// for example a plain variable holding a pre-built template.
    /// </summary>
    private static ExpressionSyntax? FindTemplateArgument(InvocationExpressionSyntax invocation)
    {
        var arguments = invocation.ArgumentList.Arguments;

        for (var i = 0; i < arguments.Count && i < 2; i++)
        {
            var expression = arguments[i].Expression;
            if (IsStringShaped(expression))
                return expression;
        }

        return null;
    }

    private static bool IsStringShaped(ExpressionSyntax expression) => expression switch
    {
        InterpolatedStringExpressionSyntax => true,
        LiteralExpressionSyntax literal => literal.IsKind(SyntaxKind.StringLiteralExpression),
        BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression) =>
            IsStringShaped(binary.Left) || IsStringShaped(binary.Right),
        _ => false
    };
}
