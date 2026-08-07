using System.ComponentModel;
using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;

namespace CWM.RoslynNavigator.Tools;

[McpServerToolType]
public static class GetSymbolDetailTool
{
    [McpServerTool(Name = "get_symbol_detail"), Description("Get detailed information about a symbol including full signature, parameters, return type, modifiers, and XML documentation. Perfect for understanding APIs without reading the whole file.")]
    public static async Task<string> ExecuteAsync(
        WorkspaceManager workspace,
        [Description("Symbol name (type, method, property). Bare, type-qualified ('OrderService.CreateAsync'), or fully qualified.")] string symbolName,
        [Description("Optional: containing type name for methods/properties")] string? containingType = null,
        CancellationToken ct = default)
    {
        var notReady = await workspace.EnsureReadyOrStatusAsync(ct);
        if (notReady is not null) return notReady;

        var lookupName = containingType is not null && !symbolName.Contains('.')
            ? $"{containingType}.{symbolName}"
            : symbolName;

        var resolved = await SymbolResolver.ResolveOrErrorAsync(workspace, lookupName, ct: ct);
        if (resolved.Failed) return resolved.Error;

        var symbol = resolved.Symbol;

        var location = SymbolResolver.GetLocation(symbol);
        var summary = ExtractXmlSummary(symbol.GetDocumentationCommentXml());

        List<ParameterDetail>? parameters = null;
        string? returnType = null;

        if (symbol is IMethodSymbol method)
        {
            returnType = method.ReturnType.ToDisplayString();
            parameters = method.Parameters.Select(p => new ParameterDetail(
                Name: p.Name,
                Type: p.Type.ToDisplayString(),
                DefaultValue: p.HasExplicitDefaultValue ? p.ExplicitDefaultValue?.ToString() : null
            )).ToList();
        }
        else if (symbol is IPropertySymbol prop)
        {
            returnType = prop.Type.ToDisplayString();
        }

        var detail = new SymbolDetail(
            Name: symbol.Name,
            Kind: SymbolResolver.GetKindString(symbol),
            Signature: symbol.ToDisplayString(),
            ReturnType: returnType,
            Accessibility: symbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
            Namespace: symbol.ContainingNamespace?.ToDisplayString(),
            Parameters: parameters,
            XmlDoc: summary,
            File: location is { } loc ? workspace.ToRelativePath(loc.File) : "unknown",
            Line: location?.Line ?? 0);

        return JsonSerializer.Serialize(detail);
    }

    private static string? ExtractXmlSummary(string? xmlDoc)
    {
        if (string.IsNullOrEmpty(xmlDoc))
            return null;

        var summaryStart = xmlDoc.IndexOf("<summary>", StringComparison.Ordinal);
        var summaryEnd = xmlDoc.IndexOf("</summary>", StringComparison.Ordinal);

        if (summaryStart < 0 || summaryEnd <= summaryStart)
            return null;

        return xmlDoc[(summaryStart + 9)..summaryEnd]
            .Replace("\r\n", " ")
            .Replace("\n", " ")
            .Trim();
    }
}
