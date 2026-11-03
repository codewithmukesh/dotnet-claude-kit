using CWM.RoslynNavigator.Responses;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CWM.RoslynNavigator.Tools;

/// <summary>
/// Semantic detection of non-Microsoft DI containers (Prism IContainerRegistry, Autofac
/// ContainerBuilder) plus recognition of calls to other containers we do not map, so that a
/// zero count is never silently misleading. Matching is by the resolved receiver type, never
/// by method name alone — "Register" is far too common a name.
/// </summary>
internal static class DiContainerSupport
{
    private static readonly (string Namespace, string Label)[] KnownContainers =
    [
        ("Autofac", "Autofac"),
        ("DryIoc", "DryIoc"),
        ("Unity", "Unity"),
        ("Castle.MicroKernel", "Castle Windsor"),
        ("Castle.Windsor", "Castle Windsor"),
        ("SimpleInjector", "SimpleInjector"),
        ("Ninject", "Ninject"),
        ("StructureMap", "StructureMap"),
        ("Lamar", "Lamar"),
        ("Grace", "Grace"),
        ("LightInject", "LightInject"),
        ("Prism", "Prism"),
    ];

    /// <summary>Cheap syntactic pre-filter: only these calls justify building a semantic model.</summary>
    internal static bool IsContainerCandidate(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax access)
            return false;

        var name = access.Name.Identifier.Text;
        return name.StartsWith("Register", StringComparison.Ordinal)
            || name.StartsWith("Bind", StringComparison.Ordinal)
            || name == "For";
    }

    /// <summary>
    /// Handles one candidate invocation. Returns true when the call belongs to a known container
    /// (recognized registrations are added to <paramref name="results"/>; otherwise the call is
    /// counted in <paramref name="unrecognized"/>).
    /// </summary>
    internal static bool TryAnalyze(
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax access,
        SemanticModel model,
        string filePath,
        List<DiRegistration> results,
        Dictionary<string, int>? unrecognized)
    {
        var info = model.GetSymbolInfo(invocation);
        var method = info.Symbol as IMethodSymbol
            ?? info.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        if (method is null)
            return false;

        var receivers = ReceiverTypes(method).ToList();
        var line = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        var name = access.Name.Identifier.Text;

        List<DiRegistration> found;
        string label;

        if (receivers.Any(IsPrismRegistry))
        {
            label = name == "RegisterForNavigation" ? "Prism navigation" : "Prism";
            found = AnalyzePrism(invocation, access, method, filePath, line);
        }
        else if (receivers.Any(t => IsInNamespace(t.ContainingNamespace, "Autofac") && t.Name == "ContainerBuilder"))
        {
            label = "Autofac";
            found = AnalyzeAutofac(invocation, access, method, filePath, line);
        }
        else
        {
            var ns = receivers.Select(t => t.ContainingNamespace).FirstOrDefault(n => KnownLabel(n) is not null)
                ?? method.ContainingNamespace;
            var other = KnownLabel(ns);
            if (other is null)
                return false;

            Count(unrecognized, other);
            return true;
        }

        if (found.Count > 0)
            results.AddRange(found);
        else
            Count(unrecognized, label);

        return true;
    }

    private static List<DiRegistration> AnalyzePrism(
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax access,
        IMethodSymbol method,
        string filePath,
        int line)
    {
        var name = access.Name.Identifier.Text;
        var lifetime = name switch
        {
            "Register" or "RegisterMany" => "transient",
            "RegisterSingleton" or "RegisterInstance" or "RegisterManySingleton" => "singleton",
            "RegisterScoped" => "scoped",
            _ => null,
        };
        if (lifetime is null)
            return []; // RegisterForNavigation, RegisterDialog, ... are not DI registrations

        var (service, implementation) = ExtractTypes(invocation, access, method);
        if (service is null)
            return [];

        var isMany = name.StartsWith("RegisterMany", StringComparison.Ordinal);
        if (isMany)
            implementation = service; // RegisterMany<T>: T registered against its interfaces
        else if (name == "RegisterInstance")
            implementation = "(instance)";
        else
            implementation ??= HasFactory(invocation) ? "(factory)" : service;

        // Prism's named overloads (Register<TFrom,TTo>(string name)) are the keyed form
        var keyed = method.Parameters.Any(p => p.Name == "name" && p.Type.SpecialType == SpecialType.System_String);

        return [new DiRegistration(service, implementation, lifetime, keyed, false, filePath, line)];
    }

    private static List<DiRegistration> AnalyzeAutofac(
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax access,
        IMethodSymbol method,
        string filePath,
        int line)
    {
        var name = access.Name.Identifier.Text;
        if (name is not ("RegisterType" or "Register" or "RegisterInstance"))
            return []; // RegisterModule, RegisterAssemblyTypes, ... cannot be mapped statically

        var (implementation, _) = ExtractTypes(invocation, access, method);
        if (implementation is null)
            return [];

        var implLabel = name switch
        {
            "RegisterInstance" => "(instance)",
            "Register" => HasFactory(invocation) ? "(factory)" : implementation,
            _ => implementation,
        };

        // Autofac default lifetime is InstancePerDependency; RegisterInstance is always a singleton
        var lifetime = name == "RegisterInstance" ? "singleton" : "transient";
        var services = new List<(string Service, bool Keyed)>();
        var selfService = implementation;

        // Walk the fluent chain: .As<I>().AsSelf().SingleInstance() ...
        SyntaxNode node = invocation;
        while (node.Parent is MemberAccessExpressionSyntax chainAccess
               && chainAccess.Expression == node
               && chainAccess.Parent is InvocationExpressionSyntax chainCall)
        {
            var chainName = chainAccess.Name.Identifier.Text;
            switch (chainName)
            {
                case "SingleInstance": lifetime = "singleton"; break;
                case "InstancePerLifetimeScope" or "InstancePerRequest" or "InstancePerMatchingLifetimeScope" or "InstancePerOwned":
                    lifetime = "scoped"; break;
                case "InstancePerDependency": lifetime = "transient"; break;
                case "AsSelf" or "AsImplementedInterfaces": services.Add((selfService, false)); break;
                case "As" or "Keyed" or "Named":
                    var (asService, _) = ExtractTypes(chainCall, chainAccess, null);
                    if (asService is not null)
                        services.Add((asService, chainName != "As"));
                    break;
            }

            node = chainCall;
        }

        if (services.Count == 0)
            services.Add((selfService, false));

        return services
            .Select(s => new DiRegistration(s.Service, implLabel, lifetime, s.Keyed, false, filePath, line))
            .ToList();
    }

    /// <summary>Service/implementation type names from type arguments, typeof() arguments, or inferred type arguments.</summary>
    private static (string? Service, string? Implementation) ExtractTypes(
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax access,
        IMethodSymbol? method)
    {
        if (access.Name is GenericNameSyntax generic)
        {
            var typeArgs = generic.TypeArgumentList.Arguments;
            return (typeArgs.Count > 0 ? typeArgs[0].ToString() : null,
                    typeArgs.Count > 1 ? typeArgs[1].ToString() : null);
        }

        var typeofArgs = invocation.ArgumentList.Arguments
            .Select(a => a.Expression)
            .OfType<TypeOfExpressionSyntax>()
            .ToList();
        if (typeofArgs.Count > 0)
            return (typeofArgs[0].Type.ToString(), typeofArgs.Count > 1 ? typeofArgs[1].Type.ToString() : null);

        // Inferred generics, e.g. registry.RegisterInstance(foo) or builder.Register(c => new Foo())
        if (method is { TypeArguments.Length: > 0 })
        {
            var args = method.TypeArguments;
            return (args[0].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    args.Length > 1 ? args[1].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) : null);
        }

        return (null, null);
    }

    private static bool HasFactory(InvocationExpressionSyntax invocation) =>
        invocation.ArgumentList.Arguments
            .Any(a => a.Expression is SimpleLambdaExpressionSyntax or ParenthesizedLambdaExpressionSyntax);

    private static IEnumerable<ITypeSymbol> ReceiverTypes(IMethodSymbol method)
    {
        if (method.ReceiverType is not null)
            yield return method.ReceiverType;
        if (method.ReducedFrom is { Parameters.Length: > 0 } reduced)
            yield return reduced.Parameters[0].Type;
        if (method.IsExtensionMethod && method.Parameters.Length > 0)
            yield return method.Parameters[0].Type;
        yield return method.ContainingType;
    }

    private static bool IsPrismRegistry(ITypeSymbol type) =>
        IsPrismRegistryType(type) || type.AllInterfaces.Any(IsPrismRegistryType);

    private static bool IsPrismRegistryType(ITypeSymbol type) =>
        type.Name == "IContainerRegistry" && IsInNamespace(type.ContainingNamespace, "Prism.Ioc");

    private static string? KnownLabel(INamespaceSymbol? ns)
    {
        foreach (var (prefix, label) in KnownContainers)
        {
            if (IsInNamespace(ns, prefix))
                return label;
        }

        return null;
    }

    private static bool IsInNamespace(INamespaceSymbol? ns, string prefix)
    {
        if (ns is null)
            return false;

        var full = ns.ToDisplayString();
        return full == prefix || full.StartsWith(prefix + ".", StringComparison.Ordinal);
    }

    private static void Count(Dictionary<string, int>? unrecognized, string label)
    {
        if (unrecognized is null)
            return;

        unrecognized[label] = unrecognized.GetValueOrDefault(label) + 1;
    }

    /// <summary>
    /// Caveat text for the response, or null when nothing was skipped. Ensures a low or zero
    /// count is not mistaken for "this solution has no DI".
    /// </summary>
    internal static string? BuildNote(int recognized, Dictionary<string, int> unrecognized)
    {
        if (unrecognized.Count == 0)
            return null;

        var total = unrecognized.Values.Sum();
        var breakdown = string.Join(", ", unrecognized.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Value} {kv.Key}"));

        return $"Recognized {recognized} registration(s), but also saw {total} call(s) that look like registrations against containers this tool does not map ({breakdown}). "
            + "The registration list is incomplete" + (recognized == 0 ? "; a count of 0 does not mean the solution has no DI." : ".")
            + " Supported: Microsoft.Extensions.DependencyInjection, Prism IContainerRegistry, Autofac ContainerBuilder.";
    }
}
