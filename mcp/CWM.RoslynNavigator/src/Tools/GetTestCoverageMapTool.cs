using System.ComponentModel;
using System.Text.Json;
using CWM.RoslynNavigator.Analyzers;
using CWM.RoslynNavigator.Responses;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ModelContextProtocol.Server;

namespace CWM.RoslynNavigator.Tools;

[McpServerToolType]
public static class GetTestCoverageMapTool
{
    /// <summary>
    /// Assemblies that indicate a behaviour-driven suite, where tests are organised by feature
    /// or endpoint rather than one test class per production type.
    /// </summary>
    private static readonly string[] IntegrationTestAssemblies =
    [
        "Microsoft.AspNetCore.Mvc.Testing",
        "Testcontainers",
        "Microsoft.AspNetCore.TestHost",
        "Respawn",
        "WireMock.Net"
    ];

    [McpServerTool(Name = "get_test_coverage_map"), Description("Which production types have a test class named after them (OrderServiceTests for OrderService). Structural, not runtime coverage. When applicable=false (integration or feature-style suites) the percentage is meaningless: do not report it as coverage.")]
    public static async Task<string> ExecuteAsync(
        WorkspaceManager workspace,
        [Description("Production project name")] string? projectFilter = null,
        int maxResults = 50,
        CancellationToken ct = default)
    {
        var notReady = await workspace.EnsureReadyOrStatusAsync(ct);
        if (notReady is not null) return notReady;

        var solution = workspace.GetSolution();
        if (solution is null)
            return ToolJson.Serialize(new TestCoverageMapResult([], 0, 0, 0));

        // Identify test projects vs production projects
        var testProjects = new List<Project>();
        var productionProjects = new List<Project>();
        var usesIntegrationHarness = false;

        // Classification reads metadata references only, so it never forces a compilation.
        foreach (var project in solution.Projects)
        {
            if (SourceClassifier.IsTestProject(project))
                testProjects.Add(project);
            else
                productionProjects.Add(project);
        }

        // Filter production projects if specified
        if (projectFilter is not null)
        {
            if (ArgumentChecks.UnknownProject(solution, projectFilter) is { } unknown)
                return unknown;

            productionProjects = productionProjects
                .Where(p => p.Name.Equals(projectFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        // Collect all test type names from test projects
        var testTypeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var testTypeFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var testMethodCount = 0;
        var testClassCount = 0;
        var seenTestFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var testProject in testProjects)
        {
            ct.ThrowIfCancellationRequested();

            var compilation = await workspace.GetCompilationAsync(testProject.Id, ct);
            if (compilation is null) continue;

            usesIntegrationHarness |= ReferencesIntegrationHarness(compilation);

            foreach (var tree in compilation.SyntaxTrees)
            {
                // A multi-targeted test project compiles each file once per framework.
                if (!seenTestFiles.Add(tree.FilePath ?? ""))
                    continue;

                var semanticModel = compilation.GetSemanticModel(tree);
                var root = await tree.GetRootAsync(ct);

                foreach (var node in DeclarationNodes.Members(root))
                {
                    // Count actual tests, which is the number the structural metric hides.
                    if (node is MethodDeclarationSyntax method
                        && SourceClassifier.HasTestAttribute(method))
                    {
                        testMethodCount++;
                        continue;
                    }

                    var symbol = semanticModel.GetDeclaredSymbol(node, ct);
                    if (symbol is not INamedTypeSymbol typeSymbol) continue;
                    if (typeSymbol.TypeKind != TypeKind.Class) continue;

                    testClassCount++;
                    testTypeNames.Add(typeSymbol.Name);
                    var location = SymbolResolver.GetLocation(typeSymbol);
                    if (location.HasValue)
                        testTypeFiles.TryAdd(typeSymbol.Name, workspace.ToRelativePath(location.Value.File));
                }
            }
        }

        // Check each production type for matching test class
        var coverage = new List<TestCoverageEntry>();
        var totalTypes = 0;
        var countedTypes = new HashSet<(string?, int, string)>();
        var testedTypes = 0;

        foreach (var project in productionProjects)
        {
            ct.ThrowIfCancellationRequested();

            var compilation = await workspace.GetCompilationAsync(project.Id, ct);
            if (compilation is null) continue;

            foreach (var tree in compilation.SyntaxTrees)
            {
                // Generated and migration types have no test surface to match against.
                var sourceKind = SourceClassifier.Classify(
                    tree, workspace.ToRelativePath(tree.FilePath ?? ""), isTestProject: false, ct);
                if (sourceKind is SourceKind.Generated or SourceKind.Migration)
                    continue;

                var semanticModel = compilation.GetSemanticModel(tree);
                var root = await tree.GetRootAsync(ct);

                foreach (var node in DeclarationNodes.Types(root))
                {
                    var symbol = semanticModel.GetDeclaredSymbol(node, ct);
                    if (symbol is not INamedTypeSymbol typeSymbol) continue;
                    if (typeSymbol.TypeKind is not (TypeKind.Class or TypeKind.Struct)) continue;
                    if (typeSymbol.IsAbstract && typeSymbol.TypeKind == TypeKind.Class
                        && typeSymbol.GetMembers().All(m => m.IsAbstract || m.IsImplicitlyDeclared)) continue;

                    // One type per declaration, not per target-framework flavor of its project.
                    var declared = SymbolResolver.GetLocation(typeSymbol);
                    if (!countedTypes.Add((declared?.File, declared?.Line ?? 0, typeSymbol.Name)))
                        continue;

                    totalTypes++;

                    var typeName = typeSymbol.Name;
                    var (hasTests, testFile) = FindMatchingTestClass(typeName, testTypeNames, testTypeFiles);

                    if (hasTests)
                        testedTypes++;

                    if (coverage.Count < maxResults)
                    {
                        var location = SymbolResolver.GetLocation(typeSymbol);
                        coverage.Add(new TestCoverageEntry(
                            Type: typeName,
                            File: location.HasValue ? workspace.ToRelativePath(location.Value.File) : "unknown",
                            HasTests: hasTests,
                            TestFile: testFile));
                    }
                }
            }
        }

        var percentage = totalTypes > 0 ? (testedTypes * 100) / totalTypes : 0;

        var notApplicableReason = DetermineNotApplicableReason(
            percentage, testMethodCount, totalTypes, usesIntegrationHarness);

        return ToolJson.Serialize(new TestCoverageMapResult(
            coverage,
            totalTypes,
            testedTypes,
            percentage,
            Applicable: notApplicableReason is null,
            NotApplicableReason: notApplicableReason,
            TestMethodCount: testMethodCount,
            TestClassCount: testClassCount));
    }

    /// <summary>
    /// Why the structural metric cannot be trusted for this solution, or null when it can.
    /// A substantial suite that does not name-match means tests are organised by behaviour,
    /// not by type — the percentage measures naming convention, not coverage.
    /// </summary>
    private static string? DetermineNotApplicableReason(
        int percentage,
        int testMethodCount,
        int totalTypes,
        bool usesIntegrationHarness)
    {
        if (testMethodCount == 0 || percentage >= 50)
            return null;

        if (usesIntegrationHarness)
            return $"Integration-driven suite ({testMethodCount} test methods) using WebApplicationFactory/Testcontainers. "
                + "Tests exercise features end-to-end rather than one class per type, so name matching "
                + $"({percentage}%) measures naming convention, not coverage. Use a runtime coverage tool instead.";

        if (testMethodCount * 4 > totalTypes)
            return $"Behaviour-driven suite ({testMethodCount} test methods across {totalTypes} production types) "
                + $"where only {percentage}% of type names have a matching *Tests class. Tests appear to be "
                + "organised by feature rather than by type, so this metric understates real coverage.";

        return null;
    }

    private static bool ReferencesIntegrationHarness(Compilation? compilation)
    {
        if (compilation is null)
            return false;

        foreach (var reference in compilation.ReferencedAssemblyNames)
        {
            foreach (var harness in IntegrationTestAssemblies)
            {
                if (reference.Name.StartsWith(harness, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private static (bool HasTests, string? TestFile) FindMatchingTestClass(
        string typeName,
        HashSet<string> testTypeNames,
        Dictionary<string, string> testTypeFiles)
    {
        // Try common test naming conventions
        string[] testNamePatterns =
        [
            $"{typeName}Tests",
            $"{typeName}Test",
            $"{typeName}_Tests",
            $"{typeName}Specs",
            $"{typeName}Spec"
        ];

        foreach (var pattern in testNamePatterns)
        {
            if (testTypeNames.Contains(pattern))
            {
                testTypeFiles.TryGetValue(pattern, out var testFile);
                return (true, testFile);
            }
        }

        return (false, null);
    }
}
