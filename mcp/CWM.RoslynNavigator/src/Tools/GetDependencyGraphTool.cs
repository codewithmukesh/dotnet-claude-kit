using System.ComponentModel;
using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ModelContextProtocol.Server;

namespace CWM.RoslynNavigator.Tools;

[McpServerToolType]
public static class GetDependencyGraphTool
{
    [McpServerTool(Name = "get_dependency_graph"), Description("Get the call dependency graph for a method, showing all methods it calls recursively up to a specified depth. Useful for impact analysis and understanding code flow.")]
    public static async Task<string> ExecuteAsync(
        WorkspaceManager workspace,
        [Description("Method to analyze. Bare, type-qualified ('OrderService.CreateAsync'), or fully qualified.")] string symbolName,
        [Description("Optional: file path to disambiguate")] string? file = null,
        [Description("Optional: line number to disambiguate")] int? line = null,
        [Description("Maximum recursion depth (1-5)")] int depth = 3,
        [Description("Maximum nodes to return. The response's Truncated flag reports whether the walk was cut short; re-query with a higher value or lower depth for a complete graph.")] int maxResults = 100,
        CancellationToken ct = default)
    {
        var notReady = await workspace.EnsureReadyOrStatusAsync(ct);
        if (notReady is not null) return notReady;

        var solution = workspace.GetSolution();
        if (solution is null)
            return JsonSerializer.Serialize(new DependencyGraphResult("unknown", [], 0, false));

        var resolved = await SymbolResolver.ResolveOrErrorAsync(workspace, symbolName, file, line, ct: ct);
        if (resolved.Failed) return resolved.Error;

        var symbol = resolved.Symbol;

        if (symbol is not IMethodSymbol rootMethod)
            return JsonSerializer.Serialize(new ErrorResponse(
                ErrorCodes.WrongSymbolKind,
                $"'{symbolName}' resolved to a {SymbolResolver.GetKindString(symbol)}, not a method."));

        depth = Math.Clamp(depth, 1, 5);

        // Build file-to-project lookup upfront — O(1) per recursion step instead of O(P*D)
        var fileToProject = new Dictionary<string, Project>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in solution.Projects)
            foreach (var doc in project.Documents)
                if (doc.FilePath is not null)
                    fileToProject.TryAdd(doc.FilePath, project);

        var visited = new HashSet<string>();
        var dependencies = new List<DependencyNode>();
        var state = new WalkState(Math.Max(1, maxResults));

        await WalkDependenciesAsync(workspace, rootMethod, 1, depth, visited, dependencies, fileToProject, state, ct);

        return JsonSerializer.Serialize(new DependencyGraphResult(
            RootSymbol: rootMethod.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            Dependencies: dependencies,
            TotalNodes: dependencies.Count,
            Truncated: state.Truncated));
    }

    private sealed class WalkState(int maxNodes)
    {
        public int MaxNodes { get; } = maxNodes;
        public bool Truncated { get; set; }
    }

    private static async Task WalkDependenciesAsync(
        WorkspaceManager workspace,
        IMethodSymbol method,
        int currentDepth,
        int maxDepth,
        HashSet<string> visited,
        List<DependencyNode> dependencies,
        Dictionary<string, Project> fileToProject,
        WalkState state,
        CancellationToken ct)
    {
        if (currentDepth > maxDepth || state.Truncated) return;

        ct.ThrowIfCancellationRequested();

        var syntaxRef = method.DeclaringSyntaxReferences.FirstOrDefault();
        if (syntaxRef is null) return;

        var syntax = await syntaxRef.GetSyntaxAsync(ct);
        var tree = syntaxRef.SyntaxTree;

        // O(1) project lookup via pre-built dictionary
        if (tree.FilePath is null || !fileToProject.TryGetValue(tree.FilePath, out var project))
            return;

        var compilation = await workspace.GetCompilationAsync(project.Id, ct);
        if (compilation is null) return;

        var semanticModel = compilation.GetSemanticModel(tree);

        // Find all invocation expressions in the method body
        var invocations = syntax.DescendantNodes().OfType<InvocationExpressionSyntax>();

        foreach (var invocation in invocations)
        {
            ct.ThrowIfCancellationRequested();

            var symbolInfo = semanticModel.GetSymbolInfo(invocation, ct);
            if (symbolInfo.Symbol is not IMethodSymbol calledMethod) continue;

            // Skip system/framework methods (exact segment match so user namespaces
            // like "SystemX" or "Microsofty" are not skipped)
            var ns = calledMethod.ContainingNamespace?.ToDisplayString() ?? "";
            if (IsFrameworkNamespace(ns)) continue;

            var displayString = calledMethod.ToDisplayString();
            if (visited.Contains(displayString)) continue;
            visited.Add(displayString);

            if (dependencies.Count >= state.MaxNodes)
            {
                state.Truncated = true;
                return;
            }

            var location = SymbolResolver.GetLocation(calledMethod);
            dependencies.Add(new DependencyNode(
                Symbol: calledMethod.Name,
                ContainingType: calledMethod.ContainingType?.Name ?? "unknown",
                File: location.HasValue ? workspace.ToRelativePath(location.Value.File) : "external",
                Line: location?.Line ?? 0,
                Depth: currentDepth));

            // Recurse if the method has source
            if (calledMethod.DeclaringSyntaxReferences.Length > 0)
            {
                await WalkDependenciesAsync(workspace, calledMethod,
                    currentDepth + 1, maxDepth, visited, dependencies, fileToProject, state, ct);
            }
        }
    }

    private static bool IsFrameworkNamespace(string ns) =>
        ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal) ||
        ns == "Microsoft" || ns.StartsWith("Microsoft.", StringComparison.Ordinal);
}
