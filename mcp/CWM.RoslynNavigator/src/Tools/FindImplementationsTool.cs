using System.ComponentModel;
using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using ModelContextProtocol.Server;

namespace CWM.RoslynNavigator.Tools;

[McpServerToolType]
public static class FindImplementationsTool
{
    [McpServerTool(Name = "find_implementations"), Description("Find all types that implement an interface or derive from a base class. Each result carries IsGenerated so generated implementations can be skipped.")]
    public static async Task<string> ExecuteAsync(
        WorkspaceManager workspace,
        [Description("The interface or base class name to find implementations for")] string interfaceName,
        [Description("Maximum results to return. TotalFound in the response reports the full count; re-query with a higher value if it exceeds Count.")] int maxResults = 50,
        CancellationToken ct = default)
    {
        var notReady = await workspace.EnsureReadyOrStatusAsync(ct);
        if (notReady is not null) return notReady;

        var solution = workspace.GetSolution();
        if (solution is null)
            return Serialize(Paging.Empty<ImplementationInfo>(maxResults));

        var resolved = await SymbolResolver.ResolveOrErrorAsync(workspace, interfaceName, ct: ct);
        if (resolved.Failed) return resolved.Error;

        var symbol = resolved.Symbol;

        if (symbol is not INamedTypeSymbol typeSymbol)
            return JsonSerializer.Serialize(new ErrorResponse(
                ErrorCodes.WrongSymbolKind,
                $"'{interfaceName}' resolved to a {SymbolResolver.GetKindString(symbol)}, not an interface or class."));

        var implementations = typeSymbol.TypeKind == TypeKind.Interface
            ? await SymbolFinder.FindImplementationsAsync(typeSymbol, solution, cancellationToken: ct)
            : await SymbolFinder.FindDerivedClassesAsync(typeSymbol, solution, cancellationToken: ct);

        var generated = new GeneratedCodeIndex(workspace);
        var all = new List<ImplementationInfo>();

        foreach (var impl in implementations)
        {
            var location = SymbolResolver.GetLocation(impl);
            if (location.HasValue)
            {
                all.Add(new ImplementationInfo(
                    impl.Name,
                    workspace.ToRelativePath(location.Value.File),
                    location.Value.Line,
                    generated.IsGenerated(impl, ct)));
            }
        }

        return Serialize(Paging.Apply(all, maxResults));
    }

    private static string Serialize(Paging.Page<ImplementationInfo> page) =>
        JsonSerializer.Serialize(new ImplementationsResult(
            page.Items, page.Count, page.TotalFound, page.Truncated, page.Limit));
}
