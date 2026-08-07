using System.Text;
using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using Microsoft.CodeAnalysis;

namespace CWM.RoslynNavigator;

/// <summary>
/// Shared utility for resolving symbol names to ISymbol instances across all projects in the solution.
/// Accepts bare names ("CreateOrderAsync"), type-qualified names ("OrderService.CreateOrderAsync"),
/// and fully-qualified names ("SampleApi.OrderService.CreateOrderAsync"), and supports further
/// disambiguation by file path and line number.
/// </summary>
internal static class SymbolResolver
{
    /// <summary>
    /// Finds all symbols matching the given name across the entire solution.
    /// The name may be qualified; only the final segment is used for the declaration-table
    /// lookup, with the leading segments applied as a suffix filter afterwards.
    /// </summary>
    public static async Task<IReadOnlyList<ISymbol>> FindSymbolsByNameAsync(
        WorkspaceManager workspace,
        string name,
        string? kindFilter = null,
        CancellationToken ct = default)
    {
        var solution = workspace.GetSolution();
        if (solution is null) return [];

        var normalized = NormalizeRequestedName(name);
        if (normalized.Length == 0) return [];

        var simpleName = GetSimpleName(normalized);
        var isQualified = normalized.Length != simpleName.Length;

        var results = new List<ISymbol>();

        foreach (var projectId in solution.ProjectIds)
        {
            var compilation = await workspace.GetCompilationAsync(projectId, ct);
            if (compilation is null) continue;

            var symbols = compilation.GetSymbolsWithName(simpleName, SymbolFilter.All, ct);

            foreach (var symbol in symbols)
            {
                if (kindFilter is not null && !MatchesKind(symbol, kindFilter))
                    continue;

                if (isQualified && !MatchesQualifiedName(symbol, normalized))
                    continue;

                results.Add(symbol);
            }
        }

        return results.Distinct(SymbolEqualityComparer.Default).ToList();
    }

    /// <summary>
    /// Strips a parameter list, generic arguments, and metadata arity markers so that
    /// "IRepository&lt;Order&gt;.GetByIdAsync(Guid, CancellationToken)" reduces to
    /// "IRepository.GetByIdAsync". Segment separators are preserved.
    /// </summary>
    public static string NormalizeRequestedName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        var span = name.AsSpan().Trim();

        // A parameter list carries no information the declaration table can use.
        var paren = span.IndexOf('(');
        if (paren >= 0) span = span[..paren].TrimEnd();

        var builder = new StringBuilder(span.Length);
        var genericDepth = 0;

        for (var i = 0; i < span.Length; i++)
        {
            var ch = span[i];

            switch (ch)
            {
                case '<':
                    genericDepth++;
                    continue;
                case '>' when genericDepth > 0:
                    genericDepth--;
                    continue;
            }

            if (genericDepth > 0) continue;

            // Metadata arity: "IRepository`1" -> "IRepository"
            if (ch == '`')
            {
                while (i + 1 < span.Length && char.IsAsciiDigit(span[i + 1])) i++;
                continue;
            }

            if (char.IsWhiteSpace(ch)) continue;

            builder.Append(ch);
        }

        return builder.ToString().Trim('.');
    }

    /// <summary>
    /// Returns the final segment of a (possibly qualified) name — the symbol's own declared name.
    /// </summary>
    public static string GetSimpleName(string normalizedName)
    {
        var lastDot = normalizedName.LastIndexOf('.');
        return lastDot < 0 ? normalizedName : normalizedName[(lastDot + 1)..];
    }

    /// <summary>
    /// True when the requested qualified name matches the symbol exactly or is a
    /// segment-aligned suffix of it. "OrderService.GetOrderAsync" matches
    /// SampleApi.OrderService.GetOrderAsync but not SampleApi.ProductService.GetOrderAsync.
    /// </summary>
    public static bool MatchesQualifiedName(ISymbol symbol, string normalizedRequest)
    {
        var qualified = BuildQualifiedName(symbol);

        return qualified.Equals(normalizedRequest, StringComparison.Ordinal)
            || qualified.EndsWith($".{normalizedRequest}", StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds the dotted namespace/containing-type/name chain for a symbol, using declared
    /// names so generic arity and type arguments never appear (Roslyn's ContainingNamespace
    /// already skips past containing types, so nested types resolve correctly).
    /// </summary>
    public static string BuildQualifiedName(ISymbol symbol)
    {
        var parts = new Stack<string>();
        parts.Push(symbol.Name);

        for (var type = symbol.ContainingType; type is not null; type = type.ContainingType)
            parts.Push(type.Name);

        for (var ns = symbol.ContainingNamespace; ns is { IsGlobalNamespace: false }; ns = ns.ContainingNamespace)
            parts.Push(ns.Name);

        return string.Join('.', parts);
    }

    /// <summary>
    /// Finds a single symbol by name, optionally disambiguated by file and line.
    /// Picks the first candidate when the name remains ambiguous — prefer
    /// <see cref="ResolveOrErrorAsync"/> in tools, which reports the ambiguity instead.
    /// </summary>
    public static async Task<ISymbol?> ResolveSymbolAsync(
        WorkspaceManager workspace,
        string name,
        string? file = null,
        int? line = null,
        CancellationToken ct = default)
    {
        var symbols = await FindSymbolsByNameAsync(workspace, name, ct: ct);
        if (symbols.Count == 0) return null;

        return NarrowByLocation(symbols, file, line)[0];
    }

    /// <summary>
    /// Resolves a symbol, producing a serialized <see cref="ErrorResponse"/> instead of null
    /// when the name matches nothing or matches several distinct symbols.
    /// </summary>
    public static async Task<SymbolResolution> ResolveOrErrorAsync(
        WorkspaceManager workspace,
        string name,
        string? file = null,
        int? line = null,
        string? kindFilter = null,
        CancellationToken ct = default)
    {
        var symbols = await FindSymbolsByNameAsync(workspace, name, kindFilter, ct);

        if (symbols.Count == 0)
        {
            return SymbolResolution.Failure(new ErrorResponse(
                ErrorCodes.SymbolNotFound,
                $"No symbol named '{name}' exists in the solution. Check the spelling, or search with find_symbol."));
        }

        var narrowed = NarrowByLocation(symbols, file, line);

        // Overloads, partial declarations, and per-target-framework duplicates all share a
        // qualified name and kind — they are one logical symbol, not an ambiguity.
        var distinct = narrowed
            .GroupBy(s => (BuildQualifiedName(s), GetKindString(s)))
            .ToList();

        if (distinct.Count == 1)
            return SymbolResolution.Success(narrowed[0]);

        var candidates = distinct
            .Take(MaxAmbiguityCandidates)
            .Select(group =>
            {
                var location = GetLocation(group.First());
                return new SymbolCandidate(
                    Qualified: group.Key.Item1,
                    Kind: group.Key.Item2,
                    File: location is { } loc ? workspace.ToRelativePath(loc.File) : "unknown",
                    Line: location?.Line ?? 0);
            })
            .ToList();

        return SymbolResolution.Failure(new ErrorResponse(
            ErrorCodes.AmbiguousMatch,
            $"'{name}' matches {distinct.Count} distinct symbols. Re-query with a fully-qualified name, or pass file/line.",
            candidates));
    }

    private const int MaxAmbiguityCandidates = 10;

    /// <summary>
    /// Narrows candidates using an optional file path then line number. Falls back to the
    /// wider set whenever a hint eliminates everything, so a stale hint never turns a
    /// resolvable name into a miss.
    /// </summary>
    private static IReadOnlyList<ISymbol> NarrowByLocation(
        IReadOnlyList<ISymbol> symbols,
        string? file,
        int? line)
    {
        if (symbols.Count <= 1 || file is null) return symbols;

        var byFile = symbols
            .Where(s => s.DeclaringSyntaxReferences.Any(r =>
                r.SyntaxTree.FilePath.EndsWith(file, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (byFile.Count == 0) return symbols;
        if (byFile.Count == 1 || !line.HasValue) return byFile;

        var byLine = byFile
            .Where(s => s.DeclaringSyntaxReferences.Any(r =>
                r.SyntaxTree.GetLineSpan(r.Span).StartLinePosition.Line + 1 == line.Value))
            .ToList();

        return byLine.Count > 0 ? byLine : byFile;
    }

    /// <summary>
    /// Gets the file path and line number for a symbol's declaration.
    /// </summary>
    public static (string File, int Line)? GetLocation(ISymbol symbol)
    {
        var syntaxRef = symbol.DeclaringSyntaxReferences.FirstOrDefault();
        if (syntaxRef is null) return null;

        var lineSpan = syntaxRef.SyntaxTree.GetLineSpan(syntaxRef.Span);
        var filePath = syntaxRef.SyntaxTree.FilePath;
        var lineNumber = lineSpan.StartLinePosition.Line + 1; // 1-based

        return (filePath, lineNumber);
    }

    /// <summary>
    /// Gets a short snippet of the source text around a location.
    /// </summary>
    public static async Task<string> GetSnippetAsync(
        Document document,
        int position,
        CancellationToken ct = default)
    {
        var text = await document.GetTextAsync(ct);
        var line = text.Lines.GetLineFromPosition(position);
        return line.ToString().Trim();
    }

    /// <summary>
    /// Maps a symbol kind string to a Roslyn SymbolKind check.
    /// </summary>
    public static bool MatchesKind(ISymbol symbol, string kind) => kind.ToLowerInvariant() switch
    {
        "type" or "class" => symbol is INamedTypeSymbol { TypeKind: TypeKind.Class },
        "interface" => symbol is INamedTypeSymbol { TypeKind: TypeKind.Interface },
        "struct" => symbol is INamedTypeSymbol { TypeKind: TypeKind.Struct },
        "enum" => symbol is INamedTypeSymbol { TypeKind: TypeKind.Enum },
        "record" => symbol is INamedTypeSymbol { IsRecord: true },
        "method" => symbol is IMethodSymbol,
        "property" => symbol is IPropertySymbol,
        "field" => symbol is IFieldSymbol,
        "event" => symbol is IEventSymbol,
        "namespace" => symbol is INamespaceSymbol,
        "any" or "" or null => true,
        _ => symbol.Kind.ToString().Equals(kind, StringComparison.OrdinalIgnoreCase)
    };

    /// <summary>
    /// Gets a human-readable kind string for a symbol.
    /// </summary>
    public static string GetKindString(ISymbol symbol) => symbol switch
    {
        INamedTypeSymbol { IsRecord: true } nts => nts.TypeKind == TypeKind.Struct ? "record struct" : "record",
        INamedTypeSymbol nts => nts.TypeKind.ToString().ToLowerInvariant(),
        IMethodSymbol => "method",
        IPropertySymbol => "property",
        IFieldSymbol => "field",
        IEventSymbol => "event",
        INamespaceSymbol => "namespace",
        _ => symbol.Kind.ToString().ToLowerInvariant()
    };
}
