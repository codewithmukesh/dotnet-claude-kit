using System.ComponentModel;
using System.Text.Json;
using CWM.RoslynNavigator.Analyzers;
using CWM.RoslynNavigator.Responses;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using ModelContextProtocol.Server;

namespace CWM.RoslynNavigator.Tools;

[McpServerToolType]
public static class AnalyzeChangeImpactTool
{
    private const int MaxDepth = 5;

    [McpServerTool(Name = "analyze_change_impact"), Description("Answer 'what breaks if I change this?' for one symbol. Reports direct references grouped by project and file, how many are tests, implementations and overrides that must change alongside a signature, transitive callers up to a depth, whether the change crosses an assembly boundary, and an overall risk rating with its rationale. Use before renaming, changing a signature, or deleting — it replaces stitching together find_references, find_implementations, find_overrides, and the project graph.")]
    public static async Task<string> ExecuteAsync(
        WorkspaceManager workspace,
        [Description("Symbol to analyze. Bare, type-qualified ('OrderService.CreateAsync'), or fully qualified.")] string symbolName,
        [Description("Optional: file path to disambiguate")] string? file = null,
        [Description("Optional: line number to disambiguate")] int? line = null,
        [Description("How many call levels to walk for transitive callers (1-5)")] int depth = 2,
        [Description("Maximum entries per list in the response")] int maxResults = 50,
        CancellationToken ct = default)
    {
        var notReady = await workspace.EnsureReadyOrStatusAsync(ct);
        if (notReady is not null) return notReady;

        var solution = workspace.GetSolution();
        if (solution is null)
            return JsonSerializer.Serialize(new ErrorResponse(
                ErrorCodes.InvalidArgument, "No solution is loaded."));

        var resolved = await SymbolResolver.ResolveOrErrorAsync(workspace, symbolName, file, line, ct: ct);
        if (resolved.Failed) return resolved.Error;

        var symbol = resolved.Symbol;
        depth = Math.Clamp(depth, 1, MaxDepth);

        var testProjects = await BuildTestProjectSetAsync(workspace, solution, ct);

        // Direct references, grouped two ways: by project (how far the change reaches) and
        // by file (where the edits land).
        var perProject = new Dictionary<string, (int Count, bool IsTest)>(StringComparer.Ordinal);
        var perFile = new Dictionary<string, (int Count, bool IsTest)>(StringComparer.OrdinalIgnoreCase);
        var directReferences = 0;
        var testReferences = 0;

        var references = await SymbolFinder.FindReferencesAsync(symbol, solution, ct);

        foreach (var reference in references)
        {
            foreach (var location in reference.Locations)
            {
                ct.ThrowIfCancellationRequested();

                var document = location.Document;
                var project = document.Project;
                var isTest = testProjects.Contains(project.Id);

                directReferences++;
                if (isTest) testReferences++;

                var projectEntry = perProject.GetValueOrDefault(project.Name);
                perProject[project.Name] = (projectEntry.Count + 1, isTest);

                var path = document.FilePath is { } p ? workspace.ToRelativePath(p) : "unknown";
                var fileEntry = perFile.GetValueOrDefault(path);
                perFile[path] = (fileEntry.Count + 1, isTest);
            }
        }

        var (implementations, overrides) = await CountDependentDeclarationsAsync(symbol, solution, ct);

        var transitive = symbol is IMethodSymbol method
            ? await CollectTransitiveCallersAsync(method, solution, depth, ct)
            : [];

        var projectPage = Paging.Apply(
            perProject
                .Select(kvp => new ImpactProject(kvp.Key, kvp.Value.Count, kvp.Value.IsTest))
                .OrderByDescending(p => p.References)
                .ThenBy(p => p.Name, StringComparer.Ordinal)
                .ToList(),
            maxResults);

        var filePage = Paging.Apply(
            perFile
                .Select(kvp => new ImpactFile(kvp.Key, kvp.Value.Count, kvp.Value.IsTest))
                .OrderByDescending(f => f.References)
                .ThenBy(f => f.File, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            maxResults);

        var callerPage = Paging.Apply(transitive, maxResults);

        // A change to a symbol visible outside its own assembly can break consumers this
        // solution cannot see, which no reference count will reveal.
        var crossesAssembly = perProject.Count > 1
            || symbol.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected
                or Accessibility.ProtectedOrInternal;

        var (risk, rationale) = RateRisk(
            symbol, directReferences, perProject.Count, implementations, overrides, crossesAssembly);

        return JsonSerializer.Serialize(new ChangeImpactResult(
            Symbol: SymbolResolver.BuildQualifiedName(symbol),
            Kind: SymbolResolver.GetKindString(symbol),
            Accessibility: symbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
            CrossesAssemblyBoundary: crossesAssembly,
            DirectReferences: directReferences,
            AffectedProjects: perProject.Count,
            AffectedFiles: perFile.Count,
            TestReferences: testReferences,
            ImplementationsToUpdate: implementations,
            OverridesToUpdate: overrides,
            Projects: projectPage.Items,
            Files: filePage.Items,
            TransitiveCallers: callerPage.Items,
            TransitiveCallerCount: transitive.Count,
            Truncated: projectPage.Truncated || filePage.Truncated || callerPage.Truncated,
            Limit: Math.Max(1, maxResults),
            Risk: risk,
            Rationale: rationale));
    }

    /// <summary>
    /// Declarations that a signature change forces to change with it: interface
    /// implementations and virtual-member overrides. These are not references, so a
    /// reference count alone understates the work.
    /// </summary>
    private static async Task<(int Implementations, int Overrides)> CountDependentDeclarationsAsync(
        ISymbol symbol,
        Solution solution,
        CancellationToken ct)
    {
        var implementations = 0;
        var overrides = 0;

        switch (symbol)
        {
            case INamedTypeSymbol { TypeKind: TypeKind.Interface } iface:
                implementations = (await SymbolFinder.FindImplementationsAsync(
                    iface, solution, cancellationToken: ct)).Count();
                break;

            case INamedTypeSymbol type:
                overrides = (await SymbolFinder.FindDerivedClassesAsync(
                    type, solution, cancellationToken: ct)).Count();
                break;

            case IMethodSymbol or IPropertySymbol or IEventSymbol:
                if (symbol.ContainingType is { TypeKind: TypeKind.Interface })
                {
                    implementations = (await SymbolFinder.FindImplementationsAsync(
                        symbol, solution, cancellationToken: ct)).Count();
                }

                if (symbol.IsVirtual || symbol.IsAbstract || symbol.IsOverride)
                {
                    overrides = (await SymbolFinder.FindOverridesAsync(
                        symbol, solution, cancellationToken: ct)).Count();
                }
                break;
        }

        return (implementations, overrides);
    }

    /// <summary>
    /// Breadth-first walk up the call graph. Visited symbols are tracked across levels so
    /// a recursive or mutually-recursive call chain terminates.
    /// </summary>
    private static async Task<List<string>> CollectTransitiveCallersAsync(
        IMethodSymbol root,
        Solution solution,
        int depth,
        CancellationToken ct)
    {
        var visited = new HashSet<ISymbol>(SymbolEqualityComparer.Default) { root };
        var ordered = new List<string>();
        var frontier = new List<ISymbol> { root };

        for (var level = 0; level < depth && frontier.Count > 0; level++)
        {
            var next = new List<ISymbol>();

            foreach (var current in frontier)
            {
                ct.ThrowIfCancellationRequested();

                var callers = await SymbolFinder.FindCallersAsync(current, solution, ct);

                foreach (var caller in callers)
                {
                    if (!caller.IsDirect) continue;
                    if (!visited.Add(caller.CallingSymbol)) continue;

                    ordered.Add(SymbolResolver.BuildQualifiedName(caller.CallingSymbol));
                    next.Add(caller.CallingSymbol);
                }
            }

            frontier = next;
        }

        return ordered;
    }

    private static async Task<HashSet<ProjectId>> BuildTestProjectSetAsync(
        WorkspaceManager workspace,
        Solution solution,
        CancellationToken ct)
    {
        var testProjects = new HashSet<ProjectId>();

        foreach (var project in solution.Projects)
        {
            var compilation = await workspace.GetCompilationAsync(project.Id, ct);
            if (compilation is null) continue;

            if (SourceClassifier.IsTestProject(project, compilation))
                testProjects.Add(project.Id);
        }

        return testProjects;
    }

    /// <summary>
    /// Rates the change and says why. The rationale matters more than the label — a caller
    /// acting on "high" needs to know whether that came from reach, from implementations
    /// that must move in lockstep, or from visibility outside the assembly.
    /// </summary>
    private static (string Risk, string Rationale) RateRisk(
        ISymbol symbol,
        int directReferences,
        int affectedProjects,
        int implementations,
        int overrides,
        bool crossesAssembly)
    {
        var reasons = new List<string>();

        if (implementations > 0)
            reasons.Add($"{implementations} implementation(s) must change with the signature");

        if (overrides > 0)
            reasons.Add($"{overrides} override(s)/derived type(s) must change with the signature");

        if (affectedProjects > 1)
            reasons.Add($"referenced from {affectedProjects} projects");

        if (crossesAssembly && symbol.DeclaredAccessibility == Accessibility.Public)
            reasons.Add("public surface — consumers outside this solution may break");

        if (directReferences > 20)
            reasons.Add($"{directReferences} call sites");

        var risk = (implementations + overrides) > 0 || affectedProjects > 2 || directReferences > 20
            ? "high"
            : affectedProjects > 1 || directReferences > 5 || crossesAssembly
                ? "medium"
                : "low";

        if (reasons.Count == 0)
        {
            reasons.Add(directReferences == 0
                ? "no references found — the change is contained, but confirm it is not reached by reflection"
                : $"{directReferences} call site(s) in a single project");
        }

        return (risk, string.Join("; ", reasons));
    }
}
