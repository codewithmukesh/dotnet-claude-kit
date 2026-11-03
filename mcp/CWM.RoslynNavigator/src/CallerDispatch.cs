using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace CWM.RoslynNavigator;

/// <summary>
/// Explains how a caller reaches the method it was found for. Roslyn's FindCallersAsync
/// cascades through related members — the interface member a method implements, the base
/// member it overrides, and the implementations or overrides of an abstract member — and
/// marks every call that binds to one of those as indirect. In a DI codebase almost every
/// call to a concrete service binds to its interface, so dropping indirect callers hides
/// most of them. They are kept and labelled instead.
/// </summary>
internal static class CallerDispatch
{
    public const string Direct = "direct";
    public const string Interface = "interface";
    public const string Base = "base";
    public const string Implementation = "implementation";

    /// <summary>
    /// "direct" when the call binds to <paramref name="target"/>; otherwise how the member it
    /// binds to (<see cref="SymbolCallerInfo.CalledSymbol"/>) relates to the target.
    /// </summary>
    public static (string Via, string? ViaSymbol) Classify(ISymbol target, SymbolCallerInfo caller)
    {
        var called = caller.CalledSymbol;
        if (caller.IsDirect || IsSameDeclaration(called, target))
            return (Direct, null);

        var via = called.ContainingType?.TypeKind == TypeKind.Interface
            ? Interface
            : IsOverriddenBy(called, target)
                ? Base
                : Implementation;

        var container = called.ContainingType?.Name;
        return (via, container is null ? called.Name : $"{container}.{called.Name}");
    }

    /// <summary>
    /// The same declaration, including its copy in another target-framework flavor of a
    /// multi-targeted project: a different symbol to Roslyn, the same method to the caller.
    /// (When Roslyn cascades through linked flavors it also reports some calls to the target
    /// itself as indirect.) Compared by declaring span rather than by signature, which differs
    /// between flavors whenever one of them failed to resolve a parameter type.
    /// </summary>
    public static bool IsSameDeclaration(ISymbol a, ISymbol b) =>
        SymbolEqualityComparer.Default.Equals(a, b)
        || a.Kind == b.Kind
            && a.Name == b.Name
            && a.DeclaringSyntaxReferences.FirstOrDefault() is { } left
            && b.DeclaringSyntaxReferences.FirstOrDefault() is { } right
            && left.Span == right.Span
            && string.Equals(left.SyntaxTree.FilePath, right.SyntaxTree.FilePath, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="target"/> overrides <paramref name="candidate"/>, at any depth.</summary>
    private static bool IsOverriddenBy(ISymbol candidate, ISymbol target)
    {
        for (var current = GetOverridden(target.OriginalDefinition); current is not null; current = GetOverridden(current))
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, candidate.OriginalDefinition))
                return true;
        }

        return false;
    }

    private static ISymbol? GetOverridden(ISymbol symbol) => symbol switch
    {
        IMethodSymbol m => m.OverriddenMethod,
        IPropertySymbol p => p.OverriddenProperty,
        IEventSymbol e => e.OverriddenEvent,
        _ => null
    };
}
