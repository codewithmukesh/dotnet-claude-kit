using System.ComponentModel;
using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ModelContextProtocol.Server;

namespace CWM.RoslynNavigator.Tools;

[McpServerToolType]
public static class ResolveStackTraceTool
{
    [McpServerTool(Name = "resolve_stack_trace"), Description("Map a .NET stack trace to source files and lines, undoing async, lambda, and local-function name rewrites. InSolution marks your frames; FirstSolutionFrame is where to start. Paste the trace verbatim.")]
    public static async Task<string> ExecuteAsync(
        WorkspaceManager workspace,
        [Description("Raw trace, exception header included")] string stackTrace,
        [Description("Drop framework frames")] bool solutionOnly = false,
        int maxResults = 50,
        CancellationToken ct = default)
    {
        var notReady = await workspace.EnsureReadyOrStatusAsync(ct);
        if (notReady is not null) return notReady;

        if (string.IsNullOrWhiteSpace(stackTrace))
            return ToolJson.Serialize(new ErrorResponse(
                ErrorCodes.InvalidArgument, "stackTrace is empty."));

        var header = StackTraceParser.ParseHeader(stackTrace);
        var parsed = StackTraceParser.ParseFrames(stackTrace);

        if (parsed.Count == 0)
            return ToolJson.Serialize(new ErrorResponse(
                ErrorCodes.InvalidArgument,
                "No stack frames found. Expected lines of the form 'at Namespace.Type.Method(...)'."));

        var resolved = new List<StackFrameInfo>();

        // Recursive and retried code repeats the same method many times in one trace;
        // resolve each distinct name once.
        var symbolsByName = new Dictionary<string, ISymbol?>(StringComparer.Ordinal);

        for (var i = 0; i < parsed.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var qualifiedName = parsed[i].QualifiedName;
            if (!symbolsByName.TryGetValue(qualifiedName, out var symbol))
            {
                var symbols = await SymbolResolver.FindSymbolsByNameAsync(workspace, qualifiedName, ct: ct);
                symbol = symbols.FirstOrDefault();
                symbolsByName[qualifiedName] = symbol;
            }

            var frame = await ResolveFrameAsync(workspace, parsed[i], i > 0 ? parsed[i - 1] : null, symbol, i, ct);

            if (solutionOnly && !frame.InSolution) continue;

            resolved.Add(frame);
        }

        var page = Paging.Apply(resolved, maxResults);
        var solutionFrames = resolved.Count(f => f.InSolution);
        var firstSolutionFrame = resolved.FirstOrDefault(f => f.InSolution)?.Index;

        return ToolJson.Serialize(new StackTraceResult(
            ExceptionType: header?.ExceptionType,
            Message: header?.Message,
            Frames: page.Items,
            Count: page.Count,
            TotalFound: page.TotalFound,
            Truncated: page.Truncated,
            Limit: page.Limit,
            SolutionFrames: solutionFrames,
            FirstSolutionFrame: firstSolutionFrame));
    }

    private static async Task<StackFrameInfo> ResolveFrameAsync(
        WorkspaceManager workspace,
        StackTraceParser.ParsedFrame frame,
        StackTraceParser.ParsedFrame? callee,
        ISymbol? symbol,
        int index,
        CancellationToken ct)
    {
        // Only source declarations are indexed, so anything that resolves is the user's
        // own code; framework frames fall through with InSolution false.
        if (symbol is null)
        {
            return new StackFrameInfo(
                Index: index,
                Method: frame.QualifiedName,
                InSolution: false,
                File: frame.File,
                Line: frame.Line,
                DeclarationLine: null,
                Snippet: null);
        }

        var location = SymbolResolver.GetLocation(symbol);
        var file = location is { } loc ? workspace.ToRelativePath(loc.File) : frame.File;

        // Prefer the line the trace reported: that is where this frame was executing. Without
        // PDB line info, the line a reader means is the call to the next frame down (the method
        // this one called), not the method's signature; the declaration is the last resort.
        var (callLine, callSites) = frame.Line is null && callee is not null
            ? FindCallSite(symbol, callee.QualifiedName, ct)
            : (null, 0);
        var line = frame.Line ?? callLine ?? location?.Line;
        var source = frame.Line is not null ? "trace" : callLine is not null ? "call" : "declaration";
        var snippet = await ReadSnippetAsync(workspace, symbol, line, ct);

        return new StackFrameInfo(
            Index: index,
            Method: SymbolResolver.BuildQualifiedName(symbol),
            InSolution: true,
            File: file,
            Line: line,
            DeclarationLine: location?.Line,
            Snippet: snippet,
            LineSource: source,
            CallSites: callSites > 1 ? callSites : null);
    }

    private static async Task<string?> ReadSnippetAsync(
        WorkspaceManager workspace,
        ISymbol symbol,
        int? line,
        CancellationToken ct)
    {
        if (line is not > 0) return null;

        var tree = symbol.DeclaringSyntaxReferences.FirstOrDefault()?.SyntaxTree;
        if (tree is null) return null;

        var document = workspace.GetSolution()?.GetDocument(tree);
        if (document is null) return null;

        var text = await document.GetTextAsync(ct);
        var zeroBased = line.Value - 1;
        if (zeroBased >= text.Lines.Count) return null;

        return text.Lines[zeroBased].ToString().Trim();
    }

    /// <summary>
    /// The first line in <paramref name="caller"/>'s body that calls <paramref name="calleeName"/>
    /// (the next frame down), and how many such calls there are. Matched by name: a method call,
    /// a property access for get_X/set_X, or a construction for .ctor. (null, 0) when none match,
    /// e.g. when the next frame is a framework helper reached through a call the trace omits.
    /// </summary>
    internal static (int? Line, int Count) FindCallSite(ISymbol caller, string calleeName, CancellationToken ct)
    {
        var body = caller.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(ct);
        if (body is null) return (null, 0);

        var segments = calleeName.Split('.');
        var member = segments[^1];
        Func<SyntaxNode, bool> matches = member switch
        {
            "ctor" or ".ctor" when segments.Length > 1 => node =>
                node is ObjectCreationExpressionSyntax creation && RightmostName(creation.Type) == segments[^2],
            _ when member.StartsWith("get_", StringComparison.Ordinal) || member.StartsWith("set_", StringComparison.Ordinal) =>
                node => node is IdentifierNameSyntax identifier && identifier.Identifier.Text == member[4..],
            _ => node => node is InvocationExpressionSyntax invocation && RightmostName(invocation.Expression) == member,
        };

        var lines = body.DescendantNodes()
            .Where(matches)
            .Select(node => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1)
            .Distinct()
            .ToList();

        return lines.Count == 0 ? (null, 0) : (lines[0], lines.Count);
    }

    /// <summary>"a.b.Foo&lt;T&gt;" → "Foo"; null for an expression that does not end in a name.</summary>
    private static string? RightmostName(ExpressionSyntax expression) => expression switch
    {
        MemberAccessExpressionSyntax access => access.Name.Identifier.Text,
        MemberBindingExpressionSyntax binding => binding.Name.Identifier.Text,
        QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
        SimpleNameSyntax simple => simple.Identifier.Text,
        _ => null,
    };
}
