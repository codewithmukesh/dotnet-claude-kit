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

    [McpServerTool(Name = "get_di_registrations"), Description("Map of dependency-injection registrations from Add{Singleton,Scoped,Transient}/AddKeyed*/TryAdd* calls: service, implementation, lifetime, and file:line. Flags duplicate registrations of the same service (which may be intentional multi-registration) and captive-dependency risks — a singleton implementation whose constructor takes a service registered as scoped. Best-effort static analysis: factory-lambda registrations report '(factory)' as the implementation, matching is by type name, and registrations built via reflection, Scrutor scanning, or extension-method indirection are not seen.")]
    public static async Task<string> ExecuteAsync(
        WorkspaceManager workspace,
        [Description("Optional: filter to registrations in files whose path contains this value")] string? file = null,
        [Description("Maximum registrations to return. TotalFound in the response reports the full count; re-query with a higher value if it exceeds Count. Duplicates and captive risks are always computed from the full set.")] int maxResults = 100,
        CancellationToken ct = default)
    {
        var notReady = await workspace.EnsureReadyOrStatusAsync(ct);
        if (notReady is not null) return notReady;

        var solution = workspace.GetSolution();
        if (solution is null)
            return JsonSerializer.Serialize(new DiRegistrationsResult(
                [], [], [], 0, 0, false, Math.Max(1, maxResults)));

        var registrations = new List<DiRegistration>();
        var typeDeclarations = new Dictionary<string, TypeDeclarationSyntax>(StringComparer.Ordinal);
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var document in solution.Projects.SelectMany(p => p.Documents))
        {
            ct.ThrowIfCancellationRequested();

            if (document.FilePath is null || !seenFiles.Add(document.FilePath))
                continue;

            var root = await document.GetSyntaxRootAsync(ct);
            if (root is null) continue;

            // Collect type declarations solution-wide for captive-dependency analysis
            foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                typeDeclarations.TryAdd(typeDecl.Identifier.Text, typeDecl);

            if (file is not null && !document.FilePath.Contains(file, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var registration in AnalyzeRoot(root, document.FilePath))
            {
                registrations.Add(registration with { File = workspace.ToRelativePath(registration.File) });
            }
        }

        var duplicates = FindDuplicates(registrations);
        var captiveRisks = FindCaptiveRisks(registrations, typeDeclarations);

        var page = Paging.Apply(registrations, maxResults);

        return JsonSerializer.Serialize(new DiRegistrationsResult(
            page.Items, duplicates, captiveRisks, page.Count, page.TotalFound, page.Truncated, page.Limit));
    }

    /// <summary>
    /// Syntax-only registration extraction for one file. Internal for direct unit testing.
    /// </summary>
    internal static List<DiRegistration> AnalyzeRoot(SyntaxNode root, string filePath)
    {
        var results = new List<DiRegistration>();

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax access)
                continue;

            var methodName = access.Name.Identifier.Text;
            if (!RegistrationMethods.Contains(methodName))
                continue;

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

            var hasFactory = invocation.ArgumentList.Arguments
                .Any(a => a.Expression is SimpleLambdaExpressionSyntax or ParenthesizedLambdaExpressionSyntax);

            implementation ??= hasFactory ? "(factory)" : service;

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
            if (implName == "(factory)" || !typeDeclarations.TryGetValue(implName, out var typeDecl))
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
