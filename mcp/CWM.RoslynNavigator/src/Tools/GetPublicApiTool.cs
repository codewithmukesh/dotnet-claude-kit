using System.ComponentModel;
using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;

namespace CWM.RoslynNavigator.Tools;

[McpServerToolType]
public static class GetPublicApiTool
{
    [McpServerTool(Name = "get_public_api"), Description("Get the public members of a type without reading the full source file. Returns method signatures, properties, events.")]
    public static async Task<string> ExecuteAsync(
        WorkspaceManager workspace,
        [Description("Type name. Bare ('OrderService') or namespace-qualified ('MyApp.Orders.OrderService').")] string typeName,
        [Description("Maximum members to return. TotalFound in the response reports the full count; re-query with a higher value if it exceeds Count.")] int maxResults = 50,
        CancellationToken ct = default)
    {
        var notReady = await workspace.EnsureReadyOrStatusAsync(ct);
        if (notReady is not null) return notReady;

        var resolved = await SymbolResolver.ResolveOrErrorAsync(workspace, typeName, ct: ct);
        if (resolved.Failed) return resolved.Error;

        var symbol = resolved.Symbol;

        if (symbol is not INamedTypeSymbol typeSymbol)
            return JsonSerializer.Serialize(new ErrorResponse(
                ErrorCodes.WrongSymbolKind,
                $"'{typeName}' resolved to a {SymbolResolver.GetKindString(symbol)}, not a type."));

        var allMembers = typeSymbol.GetMembers()
            .Where(m => m.DeclaredAccessibility == Accessibility.Public)
            .Where(m => !m.IsImplicitlyDeclared) // Exclude compiler-generated members
            .Where(m => m is not IMethodSymbol { MethodKind: MethodKind.PropertyGet or MethodKind.PropertySet })
            .Select(m => new MemberInfo(
                Kind: GetMemberKind(m),
                Signature: m.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                Accessibility: m.DeclaredAccessibility.ToString().ToLowerInvariant()))
            .ToList();

        var page = Paging.Apply(allMembers, maxResults);
        var typeKind = SymbolResolver.GetKindString(typeSymbol);

        return JsonSerializer.Serialize(new PublicApiResult(
            typeKind, page.Items, page.Count, page.TotalFound, page.Truncated, page.Limit));
    }

    private static string GetMemberKind(ISymbol symbol) => symbol switch
    {
        IMethodSymbol { MethodKind: MethodKind.Constructor } => "constructor",
        IMethodSymbol => "method",
        IPropertySymbol => "property",
        IFieldSymbol => "field",
        IEventSymbol => "event",
        INamedTypeSymbol => "nested type",
        _ => symbol.Kind.ToString().ToLowerInvariant()
    };
}
