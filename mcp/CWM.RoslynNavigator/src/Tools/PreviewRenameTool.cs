using System.ComponentModel;
using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;
using ModelContextProtocol.Server;

namespace CWM.RoslynNavigator.Tools;

[McpServerToolType]
public static class PreviewRenameTool
{
    private const int MaxLineLength = 200;
    private const int MaxConflicts = 20;

    [McpServerTool(Name = "preview_rename"), Description("Preview a semantic rename without applying it (writes nothing): every changed line with old and new text, including implementations, overrides, and call sites, plus compiler conflicts the rename would cause. Apply the edits yourself.")]
    public static async Task<string> ExecuteAsync(
        WorkspaceManager workspace,
        [Description("Symbol name or SymbolId")] string symbolName,
        [Description("Valid C# identifier (@ prefix allowed)")] string newName,
        string? file = null,
        int? line = null,
        bool renameOverloads = false,
        bool renameInStrings = false,
        bool renameInComments = false,
        [Description("Max changed lines")] int maxResults = 100,
        int? offset = null,
        CancellationToken ct = default)
    {
        var notReady = await workspace.EnsureReadyOrStatusAsync(ct);
        if (notReady is not null) return notReady;

        var solution = workspace.GetSolution();
        if (solution is null)
            return Error(ErrorCodes.InvalidArgument, "No solution is loaded.");

        if (!IsValidIdentifier(newName))
            return Error(ErrorCodes.InvalidArgument, $"'{newName}' is not a valid C# identifier.");

        var resolved = await SymbolResolver.ResolveOrErrorAsync(workspace, symbolName, file, line, ct: ct);
        if (resolved.Failed) return resolved.Error;

        var symbol = resolved.Symbol;
        if (!symbol.Locations.Any(l => l.IsInSource))
            return Error(ErrorCodes.NoSource, $"'{SymbolResolver.BuildQualifiedName(symbol)}' is declared in metadata and cannot be renamed.");

        if (symbol.Name == newName.TrimStart('@'))
            return Error(ErrorCodes.InvalidArgument, $"The symbol is already named '{symbol.Name}'.");

        // Solution is immutable: the renamer returns a new snapshot and the workspace — and
        // the disk — never see it. Nothing here calls TryApplyChanges.
        var options = new SymbolRenameOptions(renameOverloads, renameInStrings, renameInComments, RenameFile: false);
        var renamed = await Renamer.RenameSymbolAsync(solution, symbol, options, newName, ct);

        var changedDocuments = renamed.GetChanges(solution)
            .GetProjectChanges()
            .SelectMany(p => p.GetChangedDocuments(onlyGetDocumentsWithTextChanges: true))
            .ToList();

        var edits = new List<RenameEdit>();
        var seen = new HashSet<(string, int)>();
        foreach (var documentId in changedDocuments)
        {
            var before = solution.GetDocument(documentId);
            var after = renamed.GetDocument(documentId);
            if (before?.FilePath is null || after is null) continue;

            var path = workspace.ToRelativePath(before.FilePath);
            var oldText = await before.GetTextAsync(ct);
            var changes = await after.GetTextChangesAsync(before, ct);

            // A multi-targeted project compiles the same file once per framework.
            foreach (var edit in ToLineEdits(path, oldText, changes))
            {
                if (seen.Add((edit.File, edit.Line)))
                    edits.Add(edit);
            }
        }

        edits.Sort((a, b) => a.File == b.File
            ? a.Line.CompareTo(b.Line)
            : StringComparer.OrdinalIgnoreCase.Compare(a.File, b.File));

        var conflicts = await FindIntroducedErrorsAsync(workspace, solution, renamed, changedDocuments, ct);
        var page = Paging.Apply(edits, maxResults, offset ?? 0);

        var files = edits.Select(e => e.File).Distinct(StringComparer.OrdinalIgnoreCase).Count();

        return Paging.Fit(page, p => ToolJson.Serialize(new RenamePreviewResult(
            Symbol: SymbolResolver.BuildQualifiedName(symbol),
            Kind: SymbolResolver.GetKindString(symbol),
            SymbolId: SymbolResolver.GetSymbolId(symbol),
            OldName: symbol.Name,
            NewName: newName,
            Changes: p.Items,
            Count: p.Count,
            TotalFound: p.TotalFound,
            Files: files,
            Truncated: p.Truncated,
            Limit: p.Limit,
            Conflicts: [.. conflicts.Take(MaxConflicts)],
            ConflictCount: conflicts.Count,
            Note: "Preview only — nothing was written to disk or to the workspace.",
            Offset: p.Offset,
            NextOffset: p.NextOffset)));
    }

    /// <summary>
    /// A C# identifier, or a keyword escaped with '@'. Contextual keywords ("var", "async")
    /// are valid identifiers and allowed.
    /// </summary>
    internal static bool IsValidIdentifier(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        var bare = name.StartsWith('@') ? name[1..] : name;
        if (!SyntaxFacts.IsValidIdentifier(bare)) return false;

        return name.StartsWith('@') || SyntaxFacts.GetKeywordKind(bare) == SyntaxKind.None;
    }

    /// <summary>
    /// Collapses text changes into one entry per changed line: the line before, and the same
    /// line with every change on it applied. Renames never add or remove lines, so the old
    /// line number is also the new one.
    /// </summary>
    private static IEnumerable<RenameEdit> ToLineEdits(string path, SourceText oldText, IEnumerable<TextChange> changes)
    {
        foreach (var group in changes
            .OrderBy(c => c.Span.Start)
            .GroupBy(c => oldText.Lines.GetLineFromPosition(c.Span.Start).LineNumber))
        {
            var textLine = oldText.Lines[group.Key];
            var lineSpan = TextSpan.FromBounds(textLine.Start, Math.Max(textLine.End, group.Max(c => c.Span.End)));
            var original = oldText.ToString(lineSpan);

            var updated = original;
            foreach (var change in group.OrderByDescending(c => c.Span.Start))
            {
                var start = change.Span.Start - lineSpan.Start;
                updated = string.Concat(updated.AsSpan(0, start), change.NewText, updated.AsSpan(start + change.Span.Length));
            }

            yield return new RenameEdit(path, group.Key + 1, Clip(original), Clip(updated));
        }
    }

    private static string Clip(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length <= MaxLineLength ? trimmed : trimmed[..MaxLineLength] + "…";
    }

    /// <summary>
    /// Errors present after the rename that were not there before. Bounded to what a rename
    /// can plausibly break: semantic errors in the documents it changed, plus declaration
    /// errors (duplicate members, clashing type names) across the projects that contain them.
    /// A full GetDiagnostics on every affected project would bind every method body twice.
    /// Errors are matched on ID, file, and line, since a rename changes messages but not lines.
    /// </summary>
    private static async Task<List<DiagnosticInfo>> FindIntroducedErrorsAsync(
        WorkspaceManager workspace,
        Solution before,
        Solution after,
        IReadOnlyList<DocumentId> changedDocuments,
        CancellationToken ct)
    {
        var beforeErrors = await CollectErrorsAsync(before, changedDocuments, ct);
        var afterErrors = await CollectErrorsAsync(after, changedDocuments, ct);

        var baseline = beforeErrors
            .GroupBy(Key)
            .ToDictionary(g => g.Key, g => g.Count());

        var introduced = new List<DiagnosticInfo>();
        var reported = new HashSet<(string, string, int)>();
        foreach (var diagnostic in afterErrors)
        {
            var key = Key(diagnostic);
            if (baseline.TryGetValue(key, out var remaining) && remaining > 0)
            {
                baseline[key] = remaining - 1;
                continue;
            }

            var span = diagnostic.Location.GetLineSpan();
            var info = new DiagnosticInfo(
                diagnostic.Id,
                "error",
                diagnostic.GetMessage(),
                span.Path is { Length: > 0 } p ? workspace.ToRelativePath(p) : "unknown",
                span.StartLinePosition.Line + 1);

            // The same file compiled per target framework reports the same error per framework.
            if (reported.Add((info.Id, info.File, info.Line)))
                introduced.Add(info);
        }

        return introduced;
    }

    private static (string Id, string File, int Line) Key(Diagnostic diagnostic)
    {
        var span = diagnostic.Location.GetLineSpan();
        return (diagnostic.Id, span.Path ?? "", span.StartLinePosition.Line);
    }

    private static async Task<List<Diagnostic>> CollectErrorsAsync(
        Solution solution, IReadOnlyList<DocumentId> changedDocuments, CancellationToken ct)
    {
        var errors = new List<Diagnostic>();

        foreach (var projectId in changedDocuments.Select(d => d.ProjectId).Distinct())
        {
            if (solution.GetProject(projectId) is not { } project) continue;
            if (await project.GetCompilationAsync(ct) is not { } compilation) continue;

            errors.AddRange(compilation.GetDeclarationDiagnostics(ct)
                .Where(d => d.Severity == DiagnosticSeverity.Error));
        }

        foreach (var documentId in changedDocuments)
        {
            if (solution.GetDocument(documentId) is not { } document) continue;
            if (await document.GetSemanticModelAsync(ct) is not { } model) continue;

            // Declaration errors are already counted from the compilation.
            errors.AddRange(model.GetMethodBodyDiagnostics(cancellationToken: ct)
                .Where(d => d.Severity == DiagnosticSeverity.Error));
        }

        return errors;
    }

    private static string Error(string code, string message) =>
        ToolJson.Serialize(new ErrorResponse(code, message));
}
