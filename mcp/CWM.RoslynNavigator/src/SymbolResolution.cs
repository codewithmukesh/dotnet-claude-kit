using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using Microsoft.CodeAnalysis;

namespace CWM.RoslynNavigator;

/// <summary>
/// Outcome of a symbol lookup: either a resolved symbol or a serialized
/// <see cref="ErrorResponse"/> ready to return from a tool. The
/// <see cref="MemberNotNullWhenAttribute"/> annotations on <see cref="Failed"/> let callers
/// use <see cref="Symbol"/> without a null-forgiving operator after the guard clause.
/// </summary>
internal readonly struct SymbolResolution
{
    private SymbolResolution(ISymbol? symbol, string? error)
    {
        Symbol = symbol;
        Error = error;
    }

    public ISymbol? Symbol { get; }

    /// <summary>Serialized <see cref="ErrorResponse"/>, or null when resolution succeeded.</summary>
    public string? Error { get; }

    [MemberNotNullWhen(false, nameof(Symbol))]
    [MemberNotNullWhen(true, nameof(Error))]
    public bool Failed => Error is not null;

    public static SymbolResolution Success(ISymbol symbol) => new(symbol, null);

    public static SymbolResolution Failure(ErrorResponse error) =>
        new(null, JsonSerializer.Serialize(error));
}
