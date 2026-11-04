using System.ComponentModel;
using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ModelContextProtocol.Server;

namespace CWM.RoslynNavigator.Tools;

[McpServerToolType]
public static class GetDiRegistrationsTool
{
    private static readonly HashSet<string> RegistrationMethods =
    [
        "AddSingleton", "AddScoped", "AddTransient",
        "AddKeyedSingleton", "AddKeyedScoped", "AddKeyedTransient",
        "TryAddSingleton", "TryAddScoped", "TryAddTransient",
        "TryAddKeyedSingleton", "TryAddKeyedScoped", "TryAddKeyedTransient",
    ];

    [McpServerTool(Name = "get_di_registrations"), Description("Dependency-injection registrations (Microsoft.Extensions.DependencyInjection, Prism, Autofac): service, implementation, lifetime, file and line, flagging duplicates and captive dependencies (singleton taking a scoped service). Static: assembly scanning, reflection, and extension-method indirection are missed; a Note lists unmapped containers, so Count 0 with a Note does not mean no DI.")]
    public static async Task<string> ExecuteAsync(
        WorkspaceManager workspace,
        [Description("Path substring filter")] string? file = null,
        int maxResults = 500,
        int? offset = null,
        string? format = null,
        CancellationToken ct = default)
    {
        var notReady = await workspace.EnsureReadyOrStatusAsync(ct);
        if (notReady is not null) return notReady;
        if (ListFormat.Validate(format) is { } invalid) return invalid;

        var solution = workspace.GetSolution();
        if (solution is null)
            return ToolJson.Serialize(new DiRegistrationsResult(
                [], [], [], 0, 0, false, Math.Max(1, maxResults)));

        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var documents = solution.Projects
            .SelectMany(p => p.Documents)
            .Where(d => d.FilePath is not null && seenFiles.Add(d.FilePath))
            .ToArray();

        // Every document is still scanned (type lookups span files), but a filter naming no
        // file is a mistake to report, not "this file registers nothing".
        if (file is not null && !documents.Any(d => d.FilePath!.Contains(file, StringComparison.OrdinalIgnoreCase)))
            return ArgumentChecks.FileFilterMatchesNothing(file);

        // Files are analysed in parallel into per-document slots, then merged in document
        // order so first-wins type lookup, registration order and truncation match a
        // sequential pass exactly.
        var scans = new DocumentScan[documents.Length];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, documents.Length),
            new ParallelOptions { CancellationToken = ct },
            async (index, token) => scans[index] = await ScanDocumentAsync(documents[index], file, workspace, token));

        var registrations = new List<DiRegistration>();
        var projects = new List<string>();
        var typeDeclarations = new Dictionary<string, TypeDeclarationSyntax>(StringComparer.Ordinal);
        var unrecognized = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var index = 0; index < scans.Length; index++)
        {
            var scan = scans[index];
            foreach (var typeDecl in scan.TypeDeclarations)
                typeDeclarations.TryAdd(typeDecl.Identifier.Text, typeDecl);

            registrations.AddRange(scan.Registrations);
            projects.AddRange(Enumerable.Repeat(ListFormat.ProjectName(documents[index].Project), scan.Registrations.Count));

            foreach (var (label, count) in scan.Unrecognized)
                unrecognized[label] = unrecognized.GetValueOrDefault(label) + count;
        }

        var allDuplicates = FindDuplicates(registrations);
        var duplicates = allDuplicates
            .OrderByDescending(d => d.Count)
            .ThenBy(d => d.Service, StringComparer.Ordinal)
            .Take(MaxDuplicates)
            .ToList();
        int? duplicateCount = allDuplicates.Count > duplicates.Count ? allDuplicates.Count : null;
        var captiveRisks = FindCaptiveRisks(registrations, typeDeclarations);
        var note = DiContainerSupport.BuildNote(registrations.Count, unrecognized);

        var page = Paging.Apply(registrations, maxResults, offset ?? 0);
        var byLifetime = ListFormat.CountBy(registrations, r => r.Lifetime);
        var byProject = page.Truncated || page.Offset > 0 ? ListFormat.CountBy(projects, p => p) : null;

        return ListFormat.Render(
            page,
            format,
            p => ToolJson.Serialize(new DiRegistrationsResult(
                p.Items, duplicates, captiveRisks, p.Count, p.TotalFound, p.Truncated, p.Limit, note, p.Offset, p.NextOffset,
                ByLifetime: byLifetime, ByProject: byProject, DuplicateCount: duplicateCount)),
            p => ToolJson.Serialize(new DiRegistrationsResult(
                null, duplicates, captiveRisks, p.Count, p.TotalFound, p.Truncated, p.Limit, note, p.Offset, p.NextOffset,
                Files: ListFormat.GroupByFile(p.Items, r => r.File, CompactEntry),
                ByLifetime: byLifetime, ByProject: byProject, DuplicateCount: duplicateCount)));
    }

    /// <summary>Most duplicated services listed; a large modular app registers hundreds of multi-registrations.</summary>
    private const int MaxDuplicates = 25;

    /// <summary>"505 singleton IFoo -> Foo keyed try"; the arrow is omitted when the service is its own implementation.</summary>
    private static string CompactEntry(DiRegistration r) =>
        $"{r.Line} {r.Lifetime} {r.Service}{(r.Implementation == r.Service ? "" : $" -> {r.Implementation}")}{(r.Keyed ? " keyed" : "")}{(r.TryAdd ? " try" : "")}";

    private sealed record DocumentScan(
        List<TypeDeclarationSyntax> TypeDeclarations,
        List<DiRegistration> Registrations,
        Dictionary<string, int> Unrecognized);

    private static async Task<DocumentScan> ScanDocumentAsync(
        Document document,
        string? file,
        WorkspaceManager workspace,
        CancellationToken ct)
    {
        var unrecognized = new Dictionary<string, int>(StringComparer.Ordinal);

        var root = await document.GetSyntaxRootAsync(ct);
        if (root is null)
            return new DocumentScan([], [], unrecognized);

        // Collect type declarations solution-wide for captive-dependency analysis
        var typeDeclarations = root.DescendantNodes().OfType<TypeDeclarationSyntax>().ToList();

        if (file is not null && !document.FilePath!.Contains(file, StringComparison.OrdinalIgnoreCase))
            return new DocumentScan(typeDeclarations, [], unrecognized);

        // Only pay for a semantic model when the file has Register*/Bind*/For calls worth resolving
        var model = root.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(DiContainerSupport.IsContainerCandidate)
            ? await document.GetSemanticModelAsync(ct)
            : null;

        var registrations = AnalyzeRoot(root, document.FilePath!, model, unrecognized)
            .Select(registration => registration with { File = workspace.ToRelativePath(registration.File) })
            .ToList();

        return new DocumentScan(typeDeclarations, registrations, unrecognized);
    }

    /// <summary>
    /// Registration extraction for one file. Microsoft DI is matched syntactically by method name;
    /// Prism and Autofac need a <paramref name="model"/> (matched by receiver type). Calls on other
    /// known containers are counted into <paramref name="unrecognized"/>. Internal for direct unit testing.
    /// </summary>
    internal static List<DiRegistration> AnalyzeRoot(
        SyntaxNode root,
        string filePath,
        SemanticModel? model = null,
        Dictionary<string, int>? unrecognized = null)
    {
        var results = new List<DiRegistration>();

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax access)
                continue;

            var methodName = access.Name.Identifier.Text;
            if (!RegistrationMethods.Contains(methodName))
            {
                if (model is not null && DiContainerSupport.IsContainerCandidate(invocation))
                    DiContainerSupport.TryAnalyze(invocation, access, model, filePath, results, unrecognized);

                continue;
            }

            var lifetime = methodName.Contains("Singleton") ? "singleton"
                : methodName.Contains("Scoped") ? "scoped"
                : "transient";
            var keyed = methodName.Contains("Keyed");
            var tryAdd = methodName.StartsWith("TryAdd", StringComparison.Ordinal);

            string? service = null;
            string? implementation = null;

            if (access.Name is GenericNameSyntax generic)
            {
                var typeArgs = generic.TypeArgumentList.Arguments;
                service = typeArgs.Count > 0 ? typeArgs[0].ToString() : null;
                implementation = typeArgs.Count > 1 ? typeArgs[1].ToString() : null;
            }
            else
            {
                // Non-generic form: AddSingleton(typeof(IFoo), typeof(Foo))
                var typeofArgs = invocation.ArgumentList.Arguments
                    .Select(a => a.Expression)
                    .OfType<TypeOfExpressionSyntax>()
                    .ToList();

                if (typeofArgs.Count > 0) service = typeofArgs[0].Type.ToString();
                if (typeofArgs.Count > 1) implementation = typeofArgs[1].Type.ToString();
            }

            if (service is null)
                continue; // instance/factory-only overload without a resolvable type

            var arguments = invocation.ArgumentList.Arguments;
            var hasFactory = arguments
                .Any(a => a.Expression is SimpleLambdaExpressionSyntax or ParenthesizedLambdaExpressionSyntax);

            // AddSingleton<IFoo>(existing) registers an instance; reporting IFoo as its own
            // implementation read as "IFoo is a concrete type".
            var hasInstance = !hasFactory && access.Name is GenericNameSyntax && arguments.Count > 0;

            implementation ??= hasFactory ? "(factory)" : hasInstance ? "(instance)" : service;

            results.Add(new DiRegistration(
                Service: service,
                Implementation: implementation,
                Lifetime: lifetime,
                Keyed: keyed,
                TryAdd: tryAdd,
                File: filePath,
                Line: invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1));
        }

        return results;
    }

    /// <summary>
    /// Same service registered more than once outside TryAdd — may be intentional
    /// multi-registration (IEnumerable&lt;T&gt; resolution) or an accidental override.
    /// </summary>
    internal static List<DiDuplicate> FindDuplicates(List<DiRegistration> registrations) =>
        registrations
            .Where(r => !r.TryAdd)
            .GroupBy(r => r.Service, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => new DiDuplicate(g.Key, g.Count()))
            .ToList();

    /// <summary>
    /// Finds singleton implementations whose constructors take a parameter whose type is
    /// registered as scoped — the classic captive-dependency bug. Name-based matching.
    /// </summary>
    internal static List<DiCaptiveRisk> FindCaptiveRisks(
        List<DiRegistration> registrations,
        Dictionary<string, TypeDeclarationSyntax> typeDeclarations)
    {
        var scopedServices = registrations
            .Where(r => r.Lifetime == "scoped")
            .Select(r => SimpleTypeName(r.Service))
            .ToHashSet(StringComparer.Ordinal);

        if (scopedServices.Count == 0)
            return [];

        var risks = new List<DiCaptiveRisk>();

        foreach (var registration in registrations.Where(r => r.Lifetime == "singleton"))
        {
            var implName = SimpleTypeName(registration.Implementation);
            if (implName is "(factory)" or "(instance)" || !typeDeclarations.TryGetValue(implName, out var typeDecl))
                continue;

            foreach (var parameter in ConstructorParameters(typeDecl))
            {
                var parameterType = SimpleTypeName(parameter.Type?.ToString() ?? "");
                if (scopedServices.Contains(parameterType))
                {
                    risks.Add(new DiCaptiveRisk(
                        Service: registration.Service,
                        Implementation: registration.Implementation,
                        DependsOn: parameterType,
                        File: registration.File,
                        Line: registration.Line));
                }
            }
        }

        return risks;
    }

    private static IEnumerable<ParameterSyntax> ConstructorParameters(TypeDeclarationSyntax typeDecl)
    {
        // Primary constructor
        if (typeDecl.ParameterList is not null)
        {
            foreach (var parameter in typeDecl.ParameterList.Parameters)
                yield return parameter;
        }

        // Explicit constructors
        foreach (var ctor in typeDecl.Members.OfType<ConstructorDeclarationSyntax>())
        {
            foreach (var parameter in ctor.ParameterList.Parameters)
                yield return parameter;
        }
    }

    /// <summary>Strips namespace qualifiers and generic arguments: "My.Ns.IFoo&lt;T&gt;" → "IFoo".</summary>
    internal static string SimpleTypeName(string typeName)
    {
        var name = typeName;

        var angle = name.IndexOf('<');
        if (angle >= 0)
            name = name[..angle];

        var dot = name.LastIndexOf('.');
        if (dot >= 0)
            name = name[(dot + 1)..];

        return name.Trim();
    }
}
