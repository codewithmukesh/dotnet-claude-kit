using System.ComponentModel;
using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using ModelContextProtocol.Server;

namespace CWM.RoslynNavigator.Tools;

[McpServerToolType]
public static class GetTypeHierarchyTool
{
    [McpServerTool(Name = "get_type_hierarchy"), Description("Get the full inheritance chain, interfaces, and derived types for a type. For interfaces, derived types include both derived interfaces and implementing types.")]
    public static async Task<string> ExecuteAsync(
        WorkspaceManager workspace,
        [Description("Type name. Bare ('OrderService') or namespace-qualified ('MyApp.Orders.OrderService'). Also accepts a SymbolId from find_symbol/get_symbol_at, which names exactly one symbol (overload included).")] string typeName,
        [Description("Maximum derived types to return. TotalDerived in the response reports the full count; re-query with a higher value if it exceeds the list length.")] int maxResults = 500,
        [Description("Skip this many derived types; pass NextOffset from the previous response for the next page.")] int offset = 0,
        CancellationToken ct = default)
    {
        var notReady = await workspace.EnsureReadyOrStatusAsync(ct);
        if (notReady is not null) return notReady;

        var solution = workspace.GetSolution();
        if (solution is null)
            return ToolJson.Serialize(new TypeHierarchyResult([], [], [], 0, false, Math.Max(1, maxResults)));

        var resolved = await SymbolResolver.ResolveOrErrorAsync(workspace, typeName, ct: ct);
        if (resolved.Failed) return resolved.Error;

        var symbol = resolved.Symbol;

        if (symbol is not INamedTypeSymbol typeSymbol)
            return ToolJson.Serialize(new ErrorResponse(
                ErrorCodes.WrongSymbolKind,
                $"'{typeName}' resolved to a {SymbolResolver.GetKindString(symbol)}, not a type."));

        // Get base types chain
        var baseTypes = new List<string>();
        var current = typeSymbol.BaseType;
        while (current is not null && current.SpecialType != SpecialType.System_Object)
        {
            baseTypes.Add(current.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
            current = current.BaseType;
        }

        // Get interfaces
        var interfaces = typeSymbol.AllInterfaces
            .Select(i => i.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))
            .ToList();

        // Get derived types. FindDerivedClassesAsync only walks class inheritance, so for
        // interfaces we combine derived interfaces with implementing types instead.
        var allDerived = new List<INamedTypeSymbol>();
        if (typeSymbol.TypeKind == TypeKind.Interface)
        {
            var derivedInterfaces = await SymbolFinder.FindDerivedInterfacesAsync(typeSymbol, solution, cancellationToken: ct);
            var implementations = await SymbolFinder.FindImplementationsAsync(typeSymbol, solution, cancellationToken: ct);
            allDerived.AddRange(derivedInterfaces.Concat(implementations));
        }
        else
        {
            allDerived.AddRange(await SymbolFinder.FindDerivedClassesAsync(typeSymbol, solution, cancellationToken: ct));
        }

        // Each flavor of a multi-targeted project reports its own copy of a derived type:
        // same declaration ID, same declaring file.
        var distinct = Paging.Distinct(allDerived, d => (SymbolResolver.GetSymbolId(d), SymbolResolver.GetLocation(d)));
        var page = Paging.Apply(DisplayNames(distinct), maxResults, offset);
        var location = SymbolResolver.GetLocation(typeSymbol);
        var file = location is { } loc ? workspace.ToRelativePath(loc.File) : null;

        return Paging.Fit(page, p => ToolJson.Serialize(new TypeHierarchyResult(
            baseTypes, interfaces, p.Items, p.TotalFound, p.Truncated, p.Limit,
            File: file,
            Line: location?.Line ?? 0,
            Offset: p.Offset,
            NextOffset: p.NextOffset)));
    }

    /// <summary>
    /// Display names in a stable order. A name shared by several derived types (OrchardCore has
    /// one "Startup" per module, ~190 of them) is namespace-qualified, or the list cannot tell
    /// them apart; unique names stay short.
    /// </summary>
    private static List<string> DisplayNames(List<INamedTypeSymbol> types)
    {
        var shortNames = types.Select(t => t.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)).ToList();
        var repeated = shortNames
            .GroupBy(n => n, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);

        return [.. types
            .Select((t, i) => repeated.Contains(shortNames[i])
                ? t.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
                : shortNames[i])
            .Order(StringComparer.Ordinal)];
    }
}
