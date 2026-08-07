using CWM.RoslynNavigator.Analyzers;
using Microsoft.CodeAnalysis;

namespace CWM.RoslynNavigator;

/// <summary>
/// Per-call memo for "is this tree generated?". A single navigation result set usually
/// spans few files but many symbols, so classifying once per tree avoids re-reading
/// leading trivia for every hit in the same file.
/// </summary>
internal sealed class GeneratedCodeIndex(WorkspaceManager workspace)
{
    private readonly Dictionary<SyntaxTree, bool> _cache = [];

    public bool IsGenerated(SyntaxTree? tree, CancellationToken ct)
    {
        if (tree is null) return false;

        if (_cache.TryGetValue(tree, out var cached))
            return cached;

        var result = SourceClassifier.IsGenerated(tree, workspace.ToRelativePath(tree.FilePath), ct);
        _cache[tree] = result;
        return result;
    }

    /// <summary>
    /// Whether a symbol is declared in generated source, or synthesized by the compiler
    /// with no declaration at all (implicit constructors, record members).
    /// </summary>
    public bool IsGenerated(ISymbol symbol, CancellationToken ct)
    {
        if (symbol.IsImplicitlyDeclared) return true;

        var syntaxRef = symbol.DeclaringSyntaxReferences.FirstOrDefault();
        return syntaxRef is not null && IsGenerated(syntaxRef.SyntaxTree, ct);
    }
}
