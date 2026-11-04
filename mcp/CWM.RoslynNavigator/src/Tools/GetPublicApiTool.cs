using System.ComponentModel;
using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;

namespace CWM.RoslynNavigator.Tools;

[McpServerToolType]
public static class GetPublicApiTool
{
    [McpServerTool(Name = "get_public_api"), Description("Public members of a type (method, property, and event signatures) without reading the file.")]
    public static async Task<string> ExecuteAsync(
        WorkspaceManager workspace,
        [Description("Type name or SymbolId")] string typeName,
        int maxResults = 50,
        int offset = 0,
        CancellationToken ct = default)
    {
        var notReady = await workspace.EnsureReadyOrStatusAsync(ct);
        if (notReady is not null) return notReady;

        var resolved = await SymbolResolver.ResolveOrErrorAsync(workspace, typeName, ct: ct);
        if (resolved.Failed) return resolved.Error;

        var symbol = resolved.Symbol;

        if (symbol is not INamedTypeSymbol typeSymbol)
            return ToolJson.Serialize(new ErrorResponse(
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

        var page = Paging.Apply(allMembers, maxResults, offset);
        var typeKind = SymbolResolver.GetKindString(typeSymbol);
        var location = SymbolResolver.GetLocation(typeSymbol);
        var file = location is { } loc ? workspace.ToRelativePath(loc.File) : null;

        return Paging.Fit(page, p => ToolJson.Serialize(new PublicApiResult(
            typeKind, p.Items, p.Count, p.TotalFound, p.Truncated, p.Limit,
            File: file,
            Line: location?.Line ?? 0,
            Offset: p.Offset,
            NextOffset: p.NextOffset)));
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
