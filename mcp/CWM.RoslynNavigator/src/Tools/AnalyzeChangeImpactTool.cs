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

    [McpServerTool(Name = "analyze_change_impact"), Description("Blast radius of changing one symbol: references by project and file, tests, implementations and overrides that must change too, transitive callers (through interface or base members too), and a risk rating. Use before renaming, changing a signature, or deleting.")]
    public static async Task<string> ExecuteAsync(
        WorkspaceManager workspace,
        [Description("Symbol name or SymbolId")] string symbolName,
        string? file = null,
        int? line = null,
        [Description("Caller levels to walk, 1-5")] int depth = 2,
        [Description("Cap per list")] int maxResults = 50,
        CancellationToken ct = default)
    {
        var notReady = await workspace.EnsureReadyOrStatusAsync(ct);
        if (notReady is not null) return notReady;

        var solution = workspace.GetSolution();
        if (solution is null)
            return ToolJson.Serialize(new ErrorResponse(
                ErrorCodes.InvalidArgument, "No solution is loaded."));

        var resolved = await SymbolResolver.ResolveOrErrorAsync(workspace, symbolName, file, line, ct: ct);
        if (resolved.Failed) return resolved.Error;

        var symbol = resolved.Symbol;
        depth = Math.Clamp(depth, 1, MaxDepth);

        var testProjects = BuildTestProjectSet(solution, ct);

        // Direct references, grouped two ways: by project (how far the change reaches) and
        // by file (where the edits land).
        var perProject = new Dictionary<string, (int Count, bool IsTest)>(StringComparer.Ordinal);
        var perFile = new Dictionary<string, (int Count, bool IsTest)>(StringComparer.OrdinalIgnoreCase);
        var directReferences = 0;
        var testReferences = 0;

        // For a member, the call search finds the references that can reach it: calls to it,
        // to the interface or base member it implements or overrides (which may dispatch to
        // it), and to its own overrides and implementations. It does not cascade sideways the
        // way FindReferencesAsync does — up to IDisposable.Dispose and back down to every other
        // Dispose in the solution (OrchardCore: 855 definitions, 2,361 locations, 71 s for
        // WorkflowExecutionContext.Dispose) — none of which a change here affects. The same
        // search seeds the caller walk, so it runs once.
        IReadOnlyList<SymbolCallerInfo>? firstLevel = null;
        IEnumerable<Location> referenceLocations;
        if (symbol is IMethodSymbol or IPropertySymbol or IEventSymbol)
        {
            firstLevel = [.. await SymbolFinder.FindCallersAsync(symbol, solution, ct)];
            referenceLocations = firstLevel.SelectMany(c => c.Locations);
        }
        else
        {
            referenceLocations = (await SymbolFinder.FindReferencesAsync(symbol, solution, ct))
                .SelectMany(r => r.Locations)
                .Select(l => l.Location);
        }

        // Each source position counts once: Roslyn reports it once per project that compiles
        // the file (every target framework of a multi-targeted project), and those flavors
        // are one project to the person making the change.
        var seenPositions = new HashSet<(string?, int)>();

        foreach (var location in referenceLocations)
        {
            ct.ThrowIfCancellationRequested();

            if (!location.IsInSource
                || !seenPositions.Add((location.SourceTree?.FilePath, location.SourceSpan.Start))
                || solution.GetDocument(location.SourceTree) is not { } document)
            {
                continue;
            }

            var project = document.Project;
            var isTest = testProjects.Contains(project.Id);
            var projectName = ProjectDisplayName(project);

            directReferences++;
            if (isTest) testReferences++;

            var projectEntry = perProject.GetValueOrDefault(projectName);
            perProject[projectName] = (projectEntry.Count + 1, isTest);

            var path = document.FilePath is { } p ? workspace.ToRelativePath(p) : "unknown";
            var fileEntry = perFile.GetValueOrDefault(path);
            perFile[path] = (fileEntry.Count + 1, isTest);
        }

        var (implementations, overrides) = await CountDependentDeclarationsAsync(symbol, solution, ct);

        var walk = symbol is IMethodSymbol method && firstLevel is not null
            ? await CollectTransitiveCallersAsync(method, firstLevel, solution, depth, ct)
            : new CallerWalk([], 0, false);
        var transitive = walk.Callers;

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
        if (walk.Stopped)
            rationale += $"; caller walk stopped at its {MaxCallerSearches}-search bound, so TransitiveCallerCount is a lower bound";

        return ToolJson.Serialize(new ChangeImpactResult(
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
            Truncated: projectPage.Truncated || filePage.Truncated || callerPage.Truncated || walk.Stopped,
            Limit: Math.Max(1, maxResults),
            Risk: risk,
            Rationale: rationale,
            IndirectCallers: walk.Indirect));
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
                    iface, solution, cancellationToken: ct)).DistinctBy(DeclarationKey).Count();
                break;

            case INamedTypeSymbol type:
                overrides = (await SymbolFinder.FindDerivedClassesAsync(
                    type, solution, cancellationToken: ct)).DistinctBy(DeclarationKey).Count();
                break;

            case IMethodSymbol or IPropertySymbol or IEventSymbol:
                if (symbol.ContainingType is { TypeKind: TypeKind.Interface })
                {
                    implementations = (await SymbolFinder.FindImplementationsAsync(
                        symbol, solution, cancellationToken: ct)).DistinctBy(DeclarationKey).Count();
                }

                if (symbol.IsVirtual || symbol.IsAbstract || symbol.IsOverride)
                {
                    overrides = (await SymbolFinder.FindOverridesAsync(
                        symbol, solution, cancellationToken: ct)).DistinctBy(DeclarationKey).Count();
                }
                break;
        }

        return (implementations, overrides);
    }

    /// <summary>A declaration once, however many target-framework flavors compile it.</summary>
    private static (string?, int) DeclarationKey(ISymbol symbol) =>
        SymbolResolver.GetLocation(symbol) is { } location ? (location.File, location.Line) : (symbol.ToDisplayString(), 0);

    /// <summary>
    /// Upper bound on caller searches per call. Each is a solution-wide search; a method
    /// reached through a widely implemented interface (IDisposable.Dispose) fans out to
    /// hundreds of callers per level. Hitting the bound sets Truncated.
    /// </summary>
    private const int MaxCallerSearches = 2000;

    private sealed record CallerWalk(List<string> Callers, int Indirect, bool Stopped);

    /// <summary>
    /// Breadth-first walk up the call graph, starting from the root's callers already found.
    /// Visited symbols are tracked across levels so a recursive or mutually-recursive call
    /// chain terminates. Callers that reach a method through the interface member it
    /// implements (or a base member it overrides) are included — Roslyn reports them as
    /// indirect, and in DI code they are most callers — and counted separately. Each level's
    /// searches run in parallel and are merged in frontier order, so the output matches a
    /// sequential walk.
    /// </summary>
    private static async Task<CallerWalk> CollectTransitiveCallersAsync(
        IMethodSymbol root,
        IReadOnlyList<SymbolCallerInfo> rootCallers,
        Solution solution,
        int depth,
        CancellationToken ct)
    {
        var visited = new HashSet<ISymbol>(SymbolEqualityComparer.Default) { root };
        var ordered = new List<string>();
        // Target-framework flavors of one caller are distinct symbols with one declaration.
        var listed = new HashSet<(string?, int)> { DeclarationKey(root) };
        var indirect = 0;
        var searches = 1;
        var stopped = false;

        IReadOnlyList<IReadOnlyList<SymbolCallerInfo>> levelResults = [rootCallers];

        for (var level = 0; level < depth; level++)
        {
            var next = new List<ISymbol>();

            foreach (var callers in levelResults)
            {
                // Direct calls first, so a caller reached both ways counts as direct.
                foreach (var caller in callers.OrderBy(c => c.IsDirect ? 0 : 1))
                {
                    if (!visited.Add(caller.CallingSymbol)) continue;
                    if (!listed.Add(DeclarationKey(caller.CallingSymbol))) continue;

                    if (!caller.IsDirect && !CallerDispatch.IsSameDeclaration(caller.CalledSymbol, root)) indirect++;
                    ordered.Add(SymbolResolver.BuildQualifiedName(caller.CallingSymbol));
                    next.Add(caller.CallingSymbol);
                }
            }

            if (stopped || level + 1 >= depth || next.Count == 0)
                break;

            if (searches + next.Count > MaxCallerSearches)
            {
                next = next.Take(Math.Max(0, MaxCallerSearches - searches)).ToList();
                stopped = true;
            }

            searches += next.Count;
            var results = new IReadOnlyList<SymbolCallerInfo>[next.Count];
            await Parallel.ForEachAsync(
                Enumerable.Range(0, next.Count),
                new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount },
                async (i, token) => results[i] = [.. await SymbolFinder.FindCallersAsync(next[i], solution, token)]);
            levelResults = results;
        }

        return new CallerWalk(ordered, indirect, stopped);
    }
    /// <summary>"Lib(net8.0)" → "Lib": target-framework flavors of one csproj are one project here.</summary>
    private static string ProjectDisplayName(Project project)
    {
        var open = project.Name.LastIndexOf('(');
        return open > 0 && project.Name.EndsWith(')') ? project.Name[..open] : project.Name;
    }

    private static HashSet<ProjectId> BuildTestProjectSet(
        Solution solution,
        CancellationToken ct)
    {
        var testProjects = new HashSet<ProjectId>();

        foreach (var project in solution.Projects)
        {
            ct.ThrowIfCancellationRequested();

            // Metadata references only — classifying must not force every project to compile.
            if (SourceClassifier.IsTestProject(project))
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
