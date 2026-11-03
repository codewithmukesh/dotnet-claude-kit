using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text.Json;
using CWM.RoslynNavigator.Analyzers;
using CWM.RoslynNavigator.Responses;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using ModelContextProtocol.Server;

namespace CWM.RoslynNavigator.Tools;

[McpServerToolType]
public static class FindDeadCodeTool
{
    /// <summary>
    /// Interfaces whose implementers are discovered by assembly scanning or DI registration
    /// rather than by a direct reference. A zero-reference result for these means nothing.
    /// </summary>
    private static readonly HashSet<string> ConventionDiscoveredInterfaces = new(StringComparer.Ordinal)
    {
        "IEntityTypeConfiguration",
        "IHostedService",
        "IEndpointGroup",
        "IExceptionHandler",
        "IValidator",
        "IRequestHandler",
        "INotificationHandler",
        "ICommandHandler",
        "IQueryHandler",
        "IConsumer",
        "IStartupFilter",
        "IConfigureOptions",
        "IAuthorizationHandler",
        "IClaimsTransformation",
        "IHealthCheck",
        "IInterceptor",
        "IAsyncActionFilter",
        "IActionFilter"
    };

    /// <summary>
    /// Base types that mark a symbol as framework-instantiated.
    /// </summary>
    private static readonly HashSet<string> ConventionDiscoveredBaseTypes = new(StringComparer.Ordinal)
    {
        "Migration",
        "ModelSnapshot",
        "BackgroundService",
        "DbContext",
        "Profile",
        "Attribute"
    };

    /// <summary>
    /// Name suffixes that a scanner or source generator commonly binds by convention. These
    /// are still reported, but at Medium confidence with a note, because a reference search
    /// cannot see reflection.
    /// </summary>
    private static readonly string[] ConventionSuffixes =
    [
        "Configuration", "Handler", "Endpoint", "Endpoints", "Validator",
        "Profile", "Seeder", "Migration", "Extensions", "Setup", "Startup",
        "Module", "Installer", "Registrar", "Factory", "Converter"
    ];

    [McpServerTool(Name = "find_dead_code"), Description("Find unused types, methods, and properties across the solution. Identifies symbols with zero references that are not public API entry points, interface implementations, or overrides. Symbols discovered by convention (EF entity configurations, hosted services, migrations, extension-method hosts) are filtered out because a reference search cannot see assembly scanning or DI registration; the count removed is reported as conventionFiltered. Findings are graded: 'low' when the name appears in a string literal (likely reflection-bound), 'medium' when it matches a convention suffix, 'high' otherwise — delete only high-confidence hits without checking. AssemblyScanningDetected reports whether the solution registers types by scanning at all, which softens every result. Uses a fast identifier-match pre-filter: symbols whose exact name appears as an identifier token in another file are assumed referenced without a full reference search, so occasional false negatives are possible for heavily-reused names.")]
    public static async Task<string> ExecuteAsync(
        WorkspaceManager workspace,
        [Description("Scope: 'file', 'project', or 'solution'")] string scope = "solution",
        [Description("Optional: file path or project name depending on scope")] string? path = null,
        [Description("Kind filter: 'type', 'method', 'property', or 'all'")] string kind = "all",
        [Description("Maximum results to return")] int maxResults = 50,
        CancellationToken ct = default)
    {
        var notReady = await workspace.EnsureReadyOrStatusAsync(ct);
        if (notReady is not null) return notReady;

        var invalid = ArgumentChecks.Choice("scope", scope, "file", "project", "solution")
            ?? ArgumentChecks.Choice("kind", kind, "type", "method", "property", "all");
        if (invalid is not null) return invalid;
        scope = scope.ToLowerInvariant();

        var solution = workspace.GetSolution();
        if (solution is null)
            return JsonSerializer.Serialize(new DeadCodeResult([], 0, 0, false, Math.Max(1, maxResults)));

        if (scope == "project" && ArgumentChecks.UnknownProject(solution, path) is { } unknownProject)
            return unknownProject;

        if (scope == "file" && path is not null
            && !solution.Projects.SelectMany(p => p.Documents)
                .Any(d => d.FilePath?.Contains(path, StringComparison.OrdinalIgnoreCase) == true))
            return ArgumentChecks.FileFilterMatchesNothing(path);

        var projects = GetProjectsForScope(solution, scope, path);

        // Which trees to scan, in project order. A multi-targeted project compiles each file
        // once per framework. A file without preprocessor directives declares the same
        // symbols in every flavor, so it is scanned once; a file with #if is scanned in every
        // flavor, since each can declare different ones.
        var plan = new List<(Compilation Compilation, SyntaxTree Tree, SyntaxNode Root)>();
        var scannedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in projects)
        {
            ct.ThrowIfCancellationRequested();

            var compilation = await workspace.GetCompilationAsync(project.Id, ct);
            if (compilation is null) continue;

            var isTestProject = SourceClassifier.IsTestProject(project);

            var trees = scope == "file" && path is not null
                ? compilation.SyntaxTrees.Where(t => t.FilePath?.Contains(path, StringComparison.OrdinalIgnoreCase) == true)
                : compilation.SyntaxTrees;

            foreach (var tree in trees)
            {
                // Generated and migration source is authored by tooling — "unused" is meaningless.
                var sourceKind = SourceClassifier.Classify(
                    tree, workspace.ToRelativePath(tree.FilePath ?? ""), isTestProject, ct);
                if (sourceKind is SourceKind.Generated or SourceKind.Migration)
                    continue;

                var root = await tree.GetRootAsync(ct);
                if (tree.FilePath is { Length: > 0 } treePath && !scannedPaths.Add(treePath) && !root.ContainsDirectives)
                    continue;

                plan.Add((compilation, tree, root));
            }
        }

        // Declared symbols are bound per tree in parallel and merged in plan order, so the
        // candidate list matches a sequential scan.
        var interfaceImplementations = new ConcurrentDictionary<INamedTypeSymbol, HashSet<ISymbol>>(SymbolEqualityComparer.Default);
        var scans = new (List<(ISymbol Symbol, string File, int Line)> Candidates, int ConventionFiltered)[plan.Count];

        Parallel.For(0, plan.Count, new ParallelOptions { CancellationToken = ct }, i =>
        {
            var (compilation, tree, root) = plan[i];
            var semanticModel = compilation.GetSemanticModel(tree);
            var found = new List<(ISymbol Symbol, string File, int Line)>();
            var filtered = 0;

            foreach (var node in DeclarationNodes.Members(root))
            {
                var symbol = semanticModel.GetDeclaredSymbol(node, ct);
                if (symbol is null) continue;

                if (!MatchesKindFilter(symbol, kind)) continue;
                if (ShouldSkip(symbol, interfaceImplementations)) continue;

                if (IsConventionDiscovered(symbol))
                {
                    filtered++;
                    continue;
                }

                var location = SymbolResolver.GetLocation(symbol);
                if (location.HasValue)
                    found.Add((symbol, location.Value.File, location.Value.Line));
            }

            scans[i] = (found, filtered);
        });

        var candidates = scans.SelectMany(s => s.Candidates).ToList();
        var conventionFiltered = scans.Sum(s => s.ConventionFiltered);

        // Deduplicate by display string
        var unique = candidates
            .DistinctBy(c => c.Symbol.ToDisplayString())
            .ToList();

        // Index, in one pass per file, where each candidate name appears for the fast
        // name-based pre-filter, and the two reflection paths a reference search is blind to.
        // Both walks cover the whole solution, not just the requested scope: a type scoped
        // out of the scan can still be the thing that names this one.
        // Text is identical in every target-framework flavor of a file, so each path is
        // indexed once and stands for all its flavors in the "appears in another file" test —
        // indexing every flavor made a name look as if it appeared in a second file, which
        // hid every candidate in a multi-targeted project. String literals can sit in a
        // region one flavor compiles out, so files with #if feed every flavor to the
        // reflection index.
        var occurrences = new NameOccurrenceIndex(unique.Select(c => c.Symbol.Name));
        var indexedByPath = new Dictionary<string, SyntaxTree>(StringComparer.OrdinalIgnoreCase);
        var indexedTrees = new HashSet<SyntaxTree>();
        var toIndex = new List<(SyntaxTree Tree, SyntaxNode Root, bool Names)>();

        foreach (var proj in solution.Projects)
        {
            var comp = await workspace.GetCompilationAsync(proj.Id, ct);
            if (comp is null) continue;

            foreach (var tree in comp.SyntaxTrees)
            {
                if (!indexedTrees.Add(tree))
                    continue;

                ct.ThrowIfCancellationRequested();
                var root = await tree.GetRootAsync(ct);
                var firstFlavor = tree.FilePath is not { Length: > 0 } filePath || indexedByPath.TryAdd(filePath, tree);

                if (firstFlavor || root.ContainsDirectives)
                    toIndex.Add((tree, root, firstFlavor));
            }
        }

        // Both indexes are built per tree in parallel and recorded afterwards.
        var names = new (List<string>? Names, Microsoft.CodeAnalysis.Text.SourceText? Text)[toIndex.Count];
        var reflection = new ReflectionIndex();
        var reflectionGate = new Lock();

        Parallel.For(
            0,
            toIndex.Count,
            new ParallelOptions { CancellationToken = ct },
            () => new ReflectionIndex(),
            (i, _, partial) =>
            {
                var (tree, root, indexNames) = toIndex[i];
                if (indexNames)
                {
                    var text = tree.GetText(ct);
                    names[i] = (occurrences.Find(text), text);
                }

                partial.Add(root, ct);
                return partial;
            },
            partial =>
            {
                lock (reflectionGate)
                    reflection.Merge(partial);
            });

        for (var i = 0; i < toIndex.Count; i++)
        {
            if (names[i] is { Names: { } found, Text: { } text })
                occurrences.Record(toIndex[i].Tree, found, text);
        }
        // Fast pre-filter: if the symbol name appears as a whole identifier in another file,
        // it is likely referenced. Whole-token matching (not substring) so that e.g.
        // "OrderService" in another file does not mask a dead "Order" type.
        var survivors = new List<(ISymbol Symbol, string File, int Line)>();
        foreach (var candidate in unique)
        {
            var declaringTree = candidate.Symbol.DeclaringSyntaxReferences.FirstOrDefault()?.SyntaxTree;
            if (declaringTree?.FilePath is { Length: > 0 } declaringPath && indexedByPath.TryGetValue(declaringPath, out var indexed))
                declaringTree = indexed;
            if (!occurrences.AppearsOutside(candidate.Symbol.Name, declaringTree))
                survivors.Add(candidate);
        }

        // A survivor's name appears in no other file, so a reference to it can only sit in the
        // file declaring it: bind that file's matching names once, for all its survivors,
        // instead of a solution-wide reference search per survivor — whose cost grew with the
        // solution while the survivors did too, which made this tool quadratic. Members the
        // compiler binds without naming them (foreach → GetEnumerator, operators, query
        // syntax, ...) can be used from a file that never names them, so they keep the
        // solution-wide search.
        var referenced = await FindReferencedInDeclaringFilesAsync(
            solution, survivors.Where(s => !BindsImplicitly(s.Symbol)).Select(s => s.Symbol).ToList(), ct);

        var deadCode = new List<DeadCodeInfo>();
        var totalFound = 0;

        foreach (var (symbol, symbolFile, symbolLine) in survivors)
        {
            ct.ThrowIfCancellationRequested();

            var isReferenced = BindsImplicitly(symbol)
                ? (await SymbolFinder.FindReferencesAsync(symbol, solution, ct)).Any(r => r.Locations.Any())
                : referenced.Contains(symbol);
            if (isReferenced) continue;

            totalFound++;
            if (deadCode.Count < maxResults)
            {
                var (confidence, note) = Grade(symbol, reflection);

                deadCode.Add(new DeadCodeInfo(
                    Name: symbol.Name,
                    Kind: SymbolResolver.GetKindString(symbol),
                    File: workspace.ToRelativePath(symbolFile),
                    Line: symbolLine,
                    ContainingType: symbol.ContainingType?.Name,
                    Confidence: confidence,
                    Note: note));
            }
        }

        return JsonSerializer.Serialize(new DeadCodeResult(
            deadCode, deadCode.Count, totalFound,
            totalFound > deadCode.Count, Math.Max(1, maxResults), conventionFiltered,
            reflection.AssemblyScanningDetected));
    }

    /// <summary>
    /// Member names the compiler binds by pattern rather than by an identifier at the use site:
    /// foreach, await, using, deconstruction, collection initializers, query expressions,
    /// interpolated string handlers, index/range patterns, collection builders.
    /// </summary>
    private static readonly HashSet<string> PatternBoundNames = new(StringComparer.Ordinal)
    {
        "GetEnumerator", "GetAsyncEnumerator", "MoveNext", "MoveNextAsync", "Current",
        "Dispose", "DisposeAsync", "Deconstruct", "GetAwaiter", "GetResult", "IsCompleted",
        "OnCompleted", "UnsafeOnCompleted", "Add", "GetPinnableReference", "Slice", "Length",
        "Count", "Item", "Select", "Where", "SelectMany", "Join", "GroupJoin", "OrderBy",
        "OrderByDescending", "ThenBy", "ThenByDescending", "GroupBy", "Cast",
        "AppendLiteral", "AppendFormatted", "Create",
    };

    private static bool BindsImplicitly(ISymbol symbol) => symbol switch
    {
        IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator or MethodKind.Conversion or MethodKind.Destructor } => true,
        IPropertySymbol { IsIndexer: true } => true,
        _ => PatternBoundNames.Contains(symbol.Name),
    };

    /// <summary>
    /// The candidates referenced from the file declaring them, in any target-framework flavor
    /// of it. The file's text is searched for each candidate's name as a whole identifier;
    /// each hit that is a name node (doc-comment crefs included; a declaration's own
    /// identifier is not one) is bound, and its symbol — or, when binding failed, its
    /// candidate symbols, which a reference search also counts — is matched to the candidate
    /// by declaration. Files are processed in parallel.
    /// </summary>
    private static async Task<HashSet<ISymbol>> FindReferencedInDeclaringFilesAsync(
        Solution solution, IReadOnlyList<ISymbol> candidates, CancellationToken ct)
    {
        var byFile = candidates
            .GroupBy(c => c.DeclaringSyntaxReferences.FirstOrDefault()?.SyntaxTree.FilePath ?? "", StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Key.Length > 0)
            .ToList();

        var found = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var gate = new Lock();

        await Parallel.ForEachAsync(byFile, new ParallelOptions { CancellationToken = ct }, async (file, token) =>
        {
            var byName = file.GroupBy(c => c.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            var documents = solution.GetDocumentIdsWithFilePath(file.Key)
                .Select(solution.GetDocument)
                .OfType<Document>()
                .ToList();
            if (documents.Count == 0) return;

            // Text is the same in every flavor; find where the names are spelled first, so only
            // those positions are bound rather than every name node in the file.
            var text = (await documents[0].GetTextAsync(token)).ToString();
            var positions = byName.Keys
                .SelectMany(name => NameOccurrenceIndex.IdentifierPositions(text, name).Select(position => (Position: position, Name: name)))
                .ToList();
            if (positions.Count == 0) return;

            var hits = new List<ISymbol>();
            foreach (var document in documents)
            {
                if (await document.GetSyntaxRootAsync(token) is not { } root) continue;

                SemanticModel? model = null;
                foreach (var (position, name) in positions)
                {
                    // A declaration's own identifier is not a name node, so it never counts.
                    var tokenAt = root.FindToken(position, findInsideTrivia: true);
                    if (tokenAt.Parent is not SimpleNameSyntax simpleName || simpleName.Identifier != tokenAt) continue;

                    model ??= await document.GetSemanticModelAsync(token);
                    if (model is null) break;

                    var info = model.GetSymbolInfo(simpleName, token);
                    foreach (var bound in info.Symbol is { } single ? [single] : info.CandidateSymbols)
                    {
                        var target = (bound as IMethodSymbol)?.ReducedFrom ?? bound;
                        hits.AddRange(byName[name].Where(c => CallerDispatch.IsSameDeclaration(target.OriginalDefinition, c)));
                    }
                }
            }

            lock (gate)
                found.UnionWith(hits);
        });
        return found;
    }

    /// <summary>
    /// Whether a symbol is instantiated by a framework rather than by a call site — EF entity
    /// configurations applied via ApplyConfigurationsFromAssembly, hosted services, migrations,
    /// and static classes that exist only to host extension methods. A reference search cannot
    /// see any of these, so a zero-reference result carries no information.
    /// </summary>
    private static bool IsConventionDiscovered(ISymbol symbol)
    {
        if (symbol is not INamedTypeSymbol type)
            return false;

        foreach (var iface in type.AllInterfaces)
        {
            if (ConventionDiscoveredInterfaces.Contains(iface.Name))
                return true;
        }

        for (var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (ConventionDiscoveredBaseTypes.Contains(baseType.Name))
                return true;
        }

        // A static class holding extension methods is referenced through the extended type,
        // never by its own name.
        if (type.IsStatic && type.GetMembers().OfType<IMethodSymbol>().Any(m => m.IsExtensionMethod))
            return true;

        return false;
    }

    /// <summary>
    /// Rates how safe a zero-reference symbol is to delete. A name that appears in a string
    /// literal is the strongest counter-signal available — that is exactly how reflection
    /// binds a type it never references — so it outranks the weaker suffix heuristic.
    /// </summary>
    private static (string Confidence, string? Note) Grade(ISymbol symbol, ReflectionIndex reflection)
    {
        if (reflection.IsNamedInStringLiteral(symbol.Name))
        {
            return ("low",
                $"'{symbol.Name}' appears in a string literal — likely resolved by reflection " +
                "(Type.GetType, Activator.CreateInstance, or configuration). Confirm before removing.");
        }

        var conventionSuffix = MatchingConventionSuffix(symbol.Name);
        if (conventionSuffix is not null)
        {
            return ("medium",
                $"Name ends in '{conventionSuffix}' — verify it is not bound by reflection, " +
                "DI scanning, or a source generator before removing");
        }

        return ("high", null);
    }

    private static string? MatchingConventionSuffix(string name)
    {
        foreach (var suffix in ConventionSuffixes)
        {
            if (name.EndsWith(suffix, StringComparison.Ordinal))
                return suffix;
        }

        return null;
    }

    private static bool MatchesKindFilter(ISymbol symbol, string kind) => kind.ToLowerInvariant() switch
    {
        "type" or "class" => symbol is INamedTypeSymbol,
        "method" => symbol is IMethodSymbol,
        "property" => symbol is IPropertySymbol,
        "all" => symbol is INamedTypeSymbol or IMethodSymbol or IPropertySymbol,
        _ => symbol is INamedTypeSymbol or IMethodSymbol or IPropertySymbol
    };

    private static bool ShouldSkip(
        ISymbol symbol,
        ConcurrentDictionary<INamedTypeSymbol, HashSet<ISymbol>> interfaceImplementations)
    {
        // Skip non-private members that could be used externally — only internal/private
        // dead code is flagged. Checked first because it is the cheapest test and rejects
        // most symbols.
        if (symbol.DeclaredAccessibility == Accessibility.Public)
            return true;

        // Skip constructors
        if (symbol is IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor })
            return true;

        // Skip property accessors
        if (symbol is IMethodSymbol { MethodKind: MethodKind.PropertyGet or MethodKind.PropertySet })
            return true;

        // Skip interface implementations
        if (symbol.ContainingType is { } containingType
            && InterfaceImplementations(containingType, interfaceImplementations).Contains(symbol))
            return true;

        // Skip overrides
        if (symbol is IMethodSymbol { IsOverride: true })
            return true;

        if (symbol is IPropertySymbol { IsOverride: true })
            return true;

        // Skip symbols with certain attributes (entry points, test classes, etc.)
        var attributes = symbol.GetAttributes();
        foreach (var attr in attributes)
        {
            var attrName = attr.AttributeClass?.Name ?? "";
            if (attrName is "Fact" or "Theory" or "Test" or "TestMethod"
                or "ApiController" or "McpServerToolType" or "McpServerTool")
                return true;
        }

        // Skip types containing Main method (entry points)
        if (symbol is INamedTypeSymbol type)
        {
            if (type.GetMembers("Main").Length > 0)
                return true;

            // Skip enums and delegates
            if (type.TypeKind is TypeKind.Enum or TypeKind.Delegate)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Every member of <paramref name="type"/> that implements an interface member, computed
    /// once per type rather than once per member — the per-member scan was quadratic in the
    /// size of the type's interface surface.
    /// </summary>
    private static HashSet<ISymbol> InterfaceImplementations(
        INamedTypeSymbol type,
        ConcurrentDictionary<INamedTypeSymbol, HashSet<ISymbol>> cache)
    {
        if (cache.TryGetValue(type, out var cached))
            return cached;

        var implementations = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        foreach (var iface in type.AllInterfaces)
        {
            foreach (var member in iface.GetMembers())
            {
                if (type.FindImplementationForInterfaceMember(member) is { } implementation)
                    implementations.Add(implementation);
            }
        }

        cache[type] = implementations;
        return implementations;
    }

    private static IEnumerable<Project> GetProjectsForScope(Solution solution, string scope, string? path) => scope switch
    {
        "project" when path is not null => solution.Projects.Where(p =>
            p.Name.Equals(path, StringComparison.OrdinalIgnoreCase)),
        "file" => solution.Projects,
        _ => solution.Projects
    };

}
