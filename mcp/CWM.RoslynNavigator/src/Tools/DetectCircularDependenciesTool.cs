using System.ComponentModel;
using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;

namespace CWM.RoslynNavigator.Tools;

[McpServerToolType]
public static class DetectCircularDependenciesTool
{
    [McpServerTool(Name = "detect_circular_dependencies"), Description("Detect circular dependencies at the project or type level. Finds cycles in project references or type dependencies within a project.")]
    public static async Task<string> ExecuteAsync(
        WorkspaceManager workspace,
        [Description("Scope: 'projects' for project-level cycles, 'types' for type-level cycles")] string scope = "projects",
        [Description("Optional: project name filter (required for 'types' scope)")] string? projectFilter = null,
        CancellationToken ct = default)
    {
        var notReady = await workspace.EnsureReadyOrStatusAsync(ct);
        if (notReady is not null) return notReady;

        if (ArgumentChecks.Choice("scope", scope, "projects", "types") is { } invalid)
            return invalid;

        var solution = workspace.GetSolution();
        if (solution is null)
            return JsonSerializer.Serialize(new CircularDependenciesResult([], 0));

        if (ArgumentChecks.UnknownProject(solution, projectFilter) is { } unknown)
            return unknown;

        var cycles = scope.ToLowerInvariant() == "types"
            ? await DetectTypeCyclesAsync(workspace, solution, projectFilter, ct)
            : DetectProjectCycles(solution);

        return JsonSerializer.Serialize(new CircularDependenciesResult(cycles, cycles.Count));
    }

    private static List<CircularDependencyChain> DetectProjectCycles(Solution solution)
    {
        // Build adjacency list from project references
        var graph = new Dictionary<string, List<string>>();
        foreach (var project in solution.Projects)
        {
            var refs = project.ProjectReferences
                .Select(r => solution.GetProject(r.ProjectId)?.Name)
                .Where(n => n is not null)
                .Cast<string>()
                .ToList();
            graph[project.Name] = refs;
        }

        return FindCycles(graph, "project");
    }

    private static async Task<List<CircularDependencyChain>> DetectTypeCyclesAsync(
        WorkspaceManager workspace,
        Solution solution,
        string? projectFilter,
        CancellationToken ct)
    {
        var projects = projectFilter is not null
            ? solution.Projects.Where(p => p.Name.Equals(projectFilter, StringComparison.OrdinalIgnoreCase))
            : solution.Projects;

        var graph = new Dictionary<string, List<string>>();

        foreach (var project in projects)
        {
            ct.ThrowIfCancellationRequested();

            var compilation = await workspace.GetCompilationAsync(project.Id, ct);
            if (compilation is null) continue;

            foreach (var tree in compilation.SyntaxTrees)
            {
                ct.ThrowIfCancellationRequested();

                var semanticModel = compilation.GetSemanticModel(tree);
                var root = await tree.GetRootAsync(ct);

                foreach (var node in DeclarationNodes.Types(root))
                {
                    var symbol = semanticModel.GetDeclaredSymbol(node, ct);
                    if (symbol is not INamedTypeSymbol typeSymbol) continue;
                    if (typeSymbol.TypeKind is TypeKind.Enum or TypeKind.Delegate) continue;

                    var typeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                    // Partial classes are safe to process once: GetDeclaredSymbol returns the
                    // merged type symbol, so GetTypeDependencies sees members from every
                    // partial declaration regardless of which syntax tree we hit first.
                    if (graph.ContainsKey(typeName)) continue;

                    var dependencies = GetTypeDependencies(typeSymbol)
                        .Where(d => !string.Equals(d, typeName, StringComparison.Ordinal))
                        .Distinct()
                        .ToList();

                    graph[typeName] = dependencies;
                }
            }
        }

        return FindCycles(graph, "type");
    }

    private static bool IsUserType(INamedTypeSymbol type)
    {
        if (type.SpecialType != SpecialType.None) return false;
        var ns = type.ContainingNamespace;
        if (ns is null || ns.IsGlobalNamespace) return false;
        var nsName = ns.ToDisplayString();
        return !nsName.StartsWith("System", StringComparison.Ordinal)
            && !nsName.StartsWith("Microsoft", StringComparison.Ordinal);
    }

    private static IEnumerable<string> GetTypeDependencies(INamedTypeSymbol type)
    {
        // Base type
        if (type.BaseType is INamedTypeSymbol baseType && IsUserType(baseType))
            yield return baseType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        foreach (var member in type.GetMembers())
        {
            switch (member)
            {
                case IFieldSymbol { Type: INamedTypeSymbol fieldType } when IsUserType(fieldType):
                    yield return fieldType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                    break;

                case IPropertySymbol { Type: INamedTypeSymbol propType } when IsUserType(propType):
                    yield return propType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                    break;

                case IMethodSymbol method:
                    foreach (var param in method.Parameters)
                    {
                        if (param.Type is INamedTypeSymbol paramType && IsUserType(paramType))
                            yield return paramType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                    }
                    break;
            }
        }
    }

    private static List<CircularDependencyChain> FindCycles(Dictionary<string, List<string>> graph, string level)
    {
        var cycles = new List<CircularDependencyChain>();
        var visited = new HashSet<string>();
        var onStack = new HashSet<string>();
        var path = new List<string>();

        foreach (var node in graph.Keys)
        {
            if (!visited.Contains(node))
                Dfs(node, graph, visited, onStack, path, cycles, level);
        }

        // Deduplicate cycles (same cycle can be found starting from different nodes)
        return cycles
            .DistinctBy(c => string.Join("→", c.Chain.Order()))
            .ToList();
    }

    /// <summary>
    /// Iterative DFS with an explicit stack — recursion depth equals the longest dependency
    /// chain, which can overflow the call stack on large type graphs.
    /// </summary>
    private static void Dfs(
        string start,
        Dictionary<string, List<string>> graph,
        HashSet<string> visited,
        HashSet<string> onStack,
        List<string> path,
        List<CircularDependencyChain> cycles,
        string level)
    {
        var stack = new Stack<(string Node, int NextNeighbor)>();

        visited.Add(start);
        onStack.Add(start);
        path.Add(start);
        stack.Push((start, 0));

        while (stack.Count > 0)
        {
            var (node, next) = stack.Pop();

            if (graph.TryGetValue(node, out var neighbors) && next < neighbors.Count)
            {
                // Re-push current frame to continue with the following neighbor later
                stack.Push((node, next + 1));

                var neighbor = neighbors[next];
                if (!visited.Contains(neighbor) && graph.ContainsKey(neighbor))
                {
                    visited.Add(neighbor);
                    onStack.Add(neighbor);
                    path.Add(neighbor);
                    stack.Push((neighbor, 0));
                }
                else if (onStack.Contains(neighbor))
                {
                    // Found a cycle — extract it from path
                    var cycleStart = path.IndexOf(neighbor);
                    if (cycleStart >= 0)
                    {
                        var chain = path.Skip(cycleStart).Append(neighbor).ToList();
                        cycles.Add(new CircularDependencyChain(chain, level));
                    }
                }
            }
            else
            {
                // All neighbors explored — leave the node
                path.RemoveAt(path.Count - 1);
                onStack.Remove(node);
            }
        }
    }
}
