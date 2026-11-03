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
        [Description("Maximum derived types to return. TotalDerived in the response reports the full count; re-query with a higher value if it exceeds the list length.")] int maxResults = 50,
        CancellationToken ct = default)
    {
        var notReady = await workspace.EnsureReadyOrStatusAsync(ct);
        if (notReady is not null) return notReady;

        var solution = workspace.GetSolution();
        if (solution is null)
            return JsonSerializer.Serialize(new TypeHierarchyResult([], [], [], 0, false, Math.Max(1, maxResults)));

        var resolved = await SymbolResolver.ResolveOrErrorAsync(workspace, typeName, ct: ct);
        if (resolved.Failed) return resolved.Error;

        var symbol = resolved.Symbol;

        if (symbol is not INamedTypeSymbol typeSymbol)
            return JsonSerializer.Serialize(new ErrorResponse(
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
        var page = Paging.Apply(
            Paging.Distinct(allDerived, d => (SymbolResolver.GetSymbolId(d), SymbolResolver.GetLocation(d)))
                .Select(d => d.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))
                .ToList(),
            maxResults);
        var location = SymbolResolver.GetLocation(typeSymbol);

        return JsonSerializer.Serialize(new TypeHierarchyResult(
            baseTypes, interfaces, page.Items, page.TotalFound, page.Truncated, page.Limit,
            File: location is { } loc ? workspace.ToRelativePath(loc.File) : null,
            Line: location?.Line ?? 0));
    }
}
