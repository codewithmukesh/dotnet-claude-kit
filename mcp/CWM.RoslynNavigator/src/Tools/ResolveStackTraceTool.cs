using System.ComponentModel;
using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;

namespace CWM.RoslynNavigator.Tools;

[McpServerToolType]
public static class ResolveStackTraceTool
{
    [McpServerTool(Name = "resolve_stack_trace"), Description("Map a .NET exception stack trace onto the solution. Undoes compiler rewrites for async methods, lambdas, and local functions, then resolves each frame to a file and line. Frames are marked InSolution so framework noise can be skipped, and FirstSolutionFrame points at the topmost frame in your own code — usually where the investigation starts. Paste the trace verbatim; the exception header is parsed too.")]
    public static async Task<string> ExecuteAsync(
        WorkspaceManager workspace,
        [Description("The raw stack trace, including the exception header line if available")] string stackTrace,
        [Description("Only return frames that resolve to solution source, dropping framework frames")] bool solutionOnly = false,
        [Description("Maximum frames to return. TotalFound reports the full count.")] int maxResults = 50,
        CancellationToken ct = default)
    {
        var notReady = await workspace.EnsureReadyOrStatusAsync(ct);
        if (notReady is not null) return notReady;

        if (string.IsNullOrWhiteSpace(stackTrace))
            return JsonSerializer.Serialize(new ErrorResponse(
                ErrorCodes.InvalidArgument, "stackTrace is empty."));

        var header = StackTraceParser.ParseHeader(stackTrace);
        var parsed = StackTraceParser.ParseFrames(stackTrace);

        if (parsed.Count == 0)
            return JsonSerializer.Serialize(new ErrorResponse(
                ErrorCodes.InvalidArgument,
                "No stack frames found. Expected lines of the form 'at Namespace.Type.Method(...)'."));

        var resolved = new List<StackFrameInfo>();

        for (var i = 0; i < parsed.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var frame = await ResolveFrameAsync(workspace, parsed[i], i, ct);

            if (solutionOnly && !frame.InSolution) continue;

            resolved.Add(frame);
        }

        var page = Paging.Apply(resolved, maxResults);
        var solutionFrames = resolved.Count(f => f.InSolution);
        var firstSolutionFrame = resolved.FirstOrDefault(f => f.InSolution)?.Index;

        return JsonSerializer.Serialize(new StackTraceResult(
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
        int index,
        CancellationToken ct)
    {
        var symbols = await SymbolResolver.FindSymbolsByNameAsync(
            workspace, frame.QualifiedName, ct: ct);

        // Only source declarations are indexed, so anything that resolves is the user's
        // own code; framework frames fall through with InSolution false.
        var symbol = symbols.FirstOrDefault();
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

        // Prefer the line the trace reported — that is where it actually threw — and fall
        // back to the declaration when the trace carried no PDB line info.
        var line = frame.Line ?? location?.Line;
        var snippet = await ReadSnippetAsync(workspace, symbol, line, ct);

        return new StackFrameInfo(
            Index: index,
            Method: SymbolResolver.BuildQualifiedName(symbol),
            InSolution: true,
            File: file,
            Line: line,
            DeclarationLine: location?.Line,
            Snippet: snippet);
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
}
