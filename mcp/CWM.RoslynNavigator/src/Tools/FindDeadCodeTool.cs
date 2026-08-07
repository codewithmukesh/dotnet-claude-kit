using System.ComponentModel;
using System.Text.Json;
using CWM.RoslynNavigator.Analyzers;
using CWM.RoslynNavigator.Responses;
using Microsoft.CodeAnalysis;
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

        var solution = workspace.GetSolution();
        if (solution is null)
            return JsonSerializer.Serialize(new DeadCodeResult([], 0, 0, false, Math.Max(1, maxResults)));

        var candidates = new List<(ISymbol Symbol, string File, int Line)>();
        var conventionFiltered = 0;

        var projects = GetProjectsForScope(solution, scope, path);

        foreach (var project in projects)
        {
            ct.ThrowIfCancellationRequested();

            var compilation = await workspace.GetCompilationAsync(project.Id, ct);
            if (compilation is null) continue;

            var isTestProject = SourceClassifier.IsTestProject(project, compilation);

            var trees = scope == "file" && path is not null
                ? compilation.SyntaxTrees.Where(t => t.FilePath?.Contains(path, StringComparison.OrdinalIgnoreCase) == true)
                : compilation.SyntaxTrees;

            foreach (var tree in trees)
            {
                ct.ThrowIfCancellationRequested();

                // Generated and migration source is authored by tooling — "unused" is meaningless.
                var sourceKind = SourceClassifier.Classify(
                    tree, workspace.ToRelativePath(tree.FilePath ?? ""), isTestProject, ct);
                if (sourceKind is SourceKind.Generated or SourceKind.Migration)
                    continue;

                var semanticModel = compilation.GetSemanticModel(tree);
                var root = await tree.GetRootAsync(ct);

                foreach (var node in root.DescendantNodes())
                {
                    var symbol = semanticModel.GetDeclaredSymbol(node, ct);
                    if (symbol is null) continue;

                    if (!MatchesKindFilter(symbol, kind)) continue;
                    if (ShouldSkip(symbol)) continue;

                    if (IsConventionDiscovered(symbol))
                    {
                        conventionFiltered++;
                        continue;
                    }

                    var location = SymbolResolver.GetLocation(symbol);
                    if (location.HasValue)
                        candidates.Add((symbol, location.Value.File, location.Value.Line));
                }
            }
        }

        // Deduplicate by display string
        var unique = candidates
            .DistinctBy(c => c.Symbol.ToDisplayString())
            .ToList();

        // Pre-collect all source texts for fast name-based pre-filter, and index the two
        // reflection paths a reference search is blind to. Both walks cover the whole
        // solution, not just the requested scope: a type scoped out of the scan can still
        // be the thing that names this one.
        var sourceTexts = new Dictionary<SyntaxTree, string>();
        var reflection = new ReflectionIndex();

        foreach (var proj in solution.Projects)
        {
            var comp = await workspace.GetCompilationAsync(proj.Id, ct);
            if (comp is null) continue;

            foreach (var tree in comp.SyntaxTrees)
            {
                if (!sourceTexts.TryAdd(tree, (await tree.GetTextAsync(ct)).ToString()))
                    continue;

                reflection.Add(await tree.GetRootAsync(ct), ct);
            }
        }

        // Check references for each candidate
        var deadCode = new List<DeadCodeInfo>();
        var totalFound = 0;

        foreach (var (symbol, symbolFile, symbolLine) in unique)
        {
            ct.ThrowIfCancellationRequested();

            // Fast pre-filter: if the symbol name appears as a whole identifier in another
            // file, it is likely referenced. Whole-token matching (not substring) so that
            // e.g. "OrderService" in another file does not mask a dead "Order" type.
            var declaringTree = symbol.DeclaringSyntaxReferences.FirstOrDefault()?.SyntaxTree;
            var likelyReferenced = sourceTexts.Any(kvp =>
                kvp.Key != declaringTree &&
                ContainsIdentifier(kvp.Value, symbol.Name));

            if (likelyReferenced) continue;

            // Expensive but necessary for remaining candidates
            var references = await SymbolFinder.FindReferencesAsync(symbol, solution, ct);
            var refCount = references.Sum(r => r.Locations.Count());

            if (refCount == 0)
            {
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
        }

        return JsonSerializer.Serialize(new DeadCodeResult(
            deadCode, deadCode.Count, totalFound,
            totalFound > deadCode.Count, Math.Max(1, maxResults), conventionFiltered,
            reflection.AssemblyScanningDetected));
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

    /// <summary>
    /// Checks whether <paramref name="name"/> occurs in <paramref name="text"/> as a whole
    /// identifier token (not as a substring of a longer identifier).
    /// </summary>
    private static bool ContainsIdentifier(string text, string name)
    {
        var index = 0;
        while ((index = text.IndexOf(name, index, StringComparison.Ordinal)) >= 0)
        {
            var before = index == 0 ? '\0' : text[index - 1];
            var afterIndex = index + name.Length;
            var after = afterIndex >= text.Length ? '\0' : text[afterIndex];

            if (!IsIdentifierChar(before) && !IsIdentifierChar(after))
                return true;

            index += 1;
        }

        return false;
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static bool MatchesKindFilter(ISymbol symbol, string kind) => kind.ToLowerInvariant() switch
    {
        "type" or "class" => symbol is INamedTypeSymbol,
        "method" => symbol is IMethodSymbol,
        "property" => symbol is IPropertySymbol,
        "all" => symbol is INamedTypeSymbol or IMethodSymbol or IPropertySymbol,
        _ => symbol is INamedTypeSymbol or IMethodSymbol or IPropertySymbol
    };

    private static bool ShouldSkip(ISymbol symbol)
    {
        // Skip constructors
        if (symbol is IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor })
            return true;

        // Skip property accessors
        if (symbol is IMethodSymbol { MethodKind: MethodKind.PropertyGet or MethodKind.PropertySet })
            return true;

        // Skip interface implementations
        if (symbol.ContainingType is not null)
        {
            foreach (var iface in symbol.ContainingType.AllInterfaces)
            {
                foreach (var member in iface.GetMembers())
                {
                    var impl = symbol.ContainingType.FindImplementationForInterfaceMember(member);
                    if (SymbolEqualityComparer.Default.Equals(impl, symbol))
                        return true;
                }
            }
        }

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

        // Skip non-private members that could be used externally
        // Only flag internal/private dead code
        if (symbol.DeclaredAccessibility == Accessibility.Public)
            return true;

        return false;
    }

    private static IEnumerable<Project> GetProjectsForScope(Solution solution, string scope, string? path) => scope switch
    {
        "project" when path is not null => solution.Projects.Where(p =>
            p.Name.Equals(path, StringComparison.OrdinalIgnoreCase)),
        "file" => solution.Projects,
        _ => solution.Projects
    };

}
