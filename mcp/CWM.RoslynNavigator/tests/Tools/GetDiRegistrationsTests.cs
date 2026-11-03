using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CWM.RoslynNavigator.Tests.Tools;

[Collection(SampleSolutionCollection.Name)]
public class GetDiRegistrationsTests(TestSolutionFixture fixture)
{
    private static List<DiRegistration> Analyze(string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        return GetDiRegistrationsTool.AnalyzeRoot(root, "Test.cs");
    }

    [Fact]
    public void Analyze_GenericRegistrations_ExtractServiceImplementationLifetime()
    {
        var registrations = Analyze("""
            services.AddScoped<IOrderRepository, OrderRepository>();
            services.AddSingleton<ICache, MemoryCache>();
            services.AddTransient<IEmailSender, EmailSender>();
            """);

        Assert.Equal(3, registrations.Count);

        var scoped = registrations.Single(r => r.Lifetime == "scoped");
        Assert.Equal("IOrderRepository", scoped.Service);
        Assert.Equal("OrderRepository", scoped.Implementation);
        Assert.False(scoped.Keyed);
        Assert.False(scoped.TryAdd);

        Assert.Equal("singleton", registrations.Single(r => r.Service == "ICache").Lifetime);
        Assert.Equal("transient", registrations.Single(r => r.Service == "IEmailSender").Lifetime);
    }

    [Fact]
    public void Analyze_SingleTypeArgument_ServiceIsImplementation()
    {
        var registrations = Analyze("services.AddSingleton<AppState>();");

        var registration = Assert.Single(registrations);
        Assert.Equal("AppState", registration.Service);
        Assert.Equal("AppState", registration.Implementation);
    }

    [Fact]
    public void Analyze_FactoryLambda_ReportsFactoryImplementation()
    {
        var registrations = Analyze("services.AddScoped<IQux>(sp => new Qux());");

        Assert.Equal("(factory)", Assert.Single(registrations).Implementation);
    }

    [Fact]
    public void Analyze_TypeofForm_ExtractsBothTypes()
    {
        var registrations = Analyze("services.AddSingleton(typeof(IBaz), typeof(Baz));");

        var registration = Assert.Single(registrations);
        Assert.Equal("IBaz", registration.Service);
        Assert.Equal("Baz", registration.Implementation);
    }

    [Fact]
    public void Analyze_KeyedAndTryAdd_AreFlagged()
    {
        var registrations = Analyze("""
            services.AddKeyedSingleton<IBar, Bar>("key");
            services.TryAddScoped<IFoo, Foo>();
            """);

        Assert.True(registrations.Single(r => r.Service == "IBar").Keyed);
        Assert.True(registrations.Single(r => r.Service == "IFoo").TryAdd);
    }

    [Fact]
    public void FindCaptiveRisks_SingletonDependingOnScoped_IsFlagged()
    {
        var source = """
            services.AddSingleton<IWorker, Worker>();
            services.AddScoped<IOrderRepository, OrderRepository>();

            public class Worker(IOrderRepository repository) : IWorker { }
            """;
        var root = CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken).GetRoot(TestContext.Current.CancellationToken);
        var registrations = GetDiRegistrationsTool.AnalyzeRoot(root, "Test.cs");
        var typeDeclarations = root.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .ToDictionary(t => t.Identifier.Text, t => t);

        var risks = GetDiRegistrationsTool.FindCaptiveRisks(registrations, typeDeclarations);

        var risk = Assert.Single(risks);
        Assert.Equal("IWorker", risk.Service);
        Assert.Equal("Worker", risk.Implementation);
        Assert.Equal("IOrderRepository", risk.DependsOn);
    }

    [Fact]
    public void FindCaptiveRisks_SingletonDependingOnSingleton_NotFlagged()
    {
        var source = """
            services.AddSingleton<IWorker, Worker>();
            services.AddSingleton<ICache, MemoryCache>();

            public class Worker(ICache cache) : IWorker { }
            """;
        var root = CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken).GetRoot(TestContext.Current.CancellationToken);
        var registrations = GetDiRegistrationsTool.AnalyzeRoot(root, "Test.cs");
        var typeDeclarations = root.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .ToDictionary(t => t.Identifier.Text, t => t);

        Assert.Empty(GetDiRegistrationsTool.FindCaptiveRisks(registrations, typeDeclarations));
    }

    [Fact]
    public void FindDuplicates_SameServiceTwice_IsFlagged_TryAddIgnored()
    {
        var registrations = Analyze("""
            services.AddScoped<IDup, FirstImpl>();
            services.AddScoped<IDup, SecondImpl>();
            services.TryAddScoped<ISafe, SafeImpl>();
            services.TryAddScoped<ISafe, SafeImpl>();
            """);

        var duplicate = Assert.Single(GetDiRegistrationsTool.FindDuplicates(registrations));
        Assert.Equal("IDup", duplicate.Service);
        Assert.Equal(2, duplicate.Count);
    }

    [Theory]
    [InlineData("My.Ns.IFoo<T>", "IFoo")]
    [InlineData("IFoo", "IFoo")]
    [InlineData("(factory)", "(factory)")]
    public void SimpleTypeName_QualifiedGenericName_StripsNamespaceAndGenerics(string input, string expected)
    {
        Assert.Equal(expected, GetDiRegistrationsTool.SimpleTypeName(input));
    }

    // Minimal stand-ins for third-party container APIs; avoids NuGet dependencies in tests.
    private const string ContainerStubs = """
        namespace Prism.Ioc
        {
            using System;
            public interface IContainerRegistry
            {
                IContainerRegistry Register(Type from, Type to);
                IContainerRegistry RegisterSingleton(Type from, Type to);
                IContainerRegistry RegisterScoped(Type from, Type to);
                IContainerRegistry RegisterInstance(Type type, object instance);
            }
            public static class ContainerRegistryExtensions
            {
                public static IContainerRegistry Register<TFrom, TTo>(this IContainerRegistry r) => r;
                public static IContainerRegistry Register<TFrom, TTo>(this IContainerRegistry r, string name) => r;
                public static IContainerRegistry Register<T>(this IContainerRegistry r, Func<T> factory) => r;
                public static IContainerRegistry RegisterSingleton<TFrom, TTo>(this IContainerRegistry r) => r;
                public static IContainerRegistry RegisterSingleton<T>(this IContainerRegistry r) => r;
                public static IContainerRegistry RegisterScoped<TFrom, TTo>(this IContainerRegistry r) => r;
                public static IContainerRegistry RegisterInstance<T>(this IContainerRegistry r, T instance) => r;
                public static IContainerRegistry RegisterMany<T>(this IContainerRegistry r) => r;
                public static IContainerRegistry RegisterManySingleton<T>(this IContainerRegistry r) => r;
                public static void RegisterForNavigation<TView>(this IContainerRegistry r) { }
            }
        }
        namespace Autofac
        {
            using System;
            public sealed class ContainerBuilder { }
            public interface IComponentContext { }
            public sealed class Reg<T> { }
            public static class RegistrationExtensions
            {
                public static Reg<T> RegisterType<T>(this ContainerBuilder b) => new();
                public static Reg<T> Register<T>(this ContainerBuilder b, Func<IComponentContext, T> f) => new();
                public static Reg<T> RegisterInstance<T>(this ContainerBuilder b, T instance) => new();
                public static void RegisterModule<T>(this ContainerBuilder b) { }
                public static Reg<T> As<T, TService>(this Reg<T> r) => r;
                public static Reg<T> AsSelf<T>(this Reg<T> r) => r;
                public static Reg<T> SingleInstance<T>(this Reg<T> r) => r;
                public static Reg<T> InstancePerLifetimeScope<T>(this Reg<T> r) => r;
                public static Reg<T> InstancePerDependency<T>(this Reg<T> r) => r;
            }
        }
        namespace DryIoc
        {
            public sealed class Container { public void Register<TS, TI>() { } public void RegisterDelegate<T>() { } }
        }
        namespace Unrelated
        {
            public sealed class Ledger { public void Register(string entry) { } }
        }
        """;

    private static (List<DiRegistration> Registrations, Dictionary<string, int> Unrecognized) AnalyzeSemantic(string body)
    {
        var tree = CSharpSyntaxTree.ParseText(
            ContainerStubs + "\nnamespace App { using System; using Prism.Ioc; using Autofac; "
            + "public class Probe { void Run(IContainerRegistry registry, ContainerBuilder builder, DryIoc.Container dry, Unrelated.Ledger ledger) {\n"
            + body + "\n} } }",
            cancellationToken: TestContext.Current.CancellationToken);

        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var compilation = CSharpCompilation.Create(
            "DiProbe",
            [tree],
            trusted.Select(p => MetadataReference.CreateFromFile(p)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var unrecognized = new Dictionary<string, int>();
        var root = tree.GetRoot(TestContext.Current.CancellationToken);
        var registrations = GetDiRegistrationsTool.AnalyzeRoot(root, "Probe.cs", compilation.GetSemanticModel(tree), unrecognized);
        return (registrations, unrecognized);
    }

    [Fact]
    public void Prism_RegisterMethods_MapToLifetimes()
    {
        var (registrations, unrecognized) = AnalyzeSemantic("""
            registry.RegisterSingleton<IFoo, Foo>();
            registry.Register<IBar, Bar>();
            registry.RegisterScoped<IBaz, Baz>();
            registry.RegisterInstance<IQux>(new Qux());
            registry.RegisterSingleton(typeof(IA), typeof(A));
            registry.RegisterMany<Many>();
            """);

        Assert.Empty(unrecognized);
        Assert.Equal(6, registrations.Count);
        Assert.Equal("singleton", registrations.Single(r => r.Service == "IFoo").Lifetime);
        Assert.Equal("Foo", registrations.Single(r => r.Service == "IFoo").Implementation);
        Assert.Equal("transient", registrations.Single(r => r.Service == "IBar").Lifetime);
        Assert.Equal("scoped", registrations.Single(r => r.Service == "IBaz").Lifetime);
        var instance = registrations.Single(r => r.Service == "IQux");
        Assert.Equal("singleton", instance.Lifetime);
        Assert.Equal("(instance)", instance.Implementation);
        Assert.Equal("A", registrations.Single(r => r.Service == "IA").Implementation);
        Assert.Equal("Many", registrations.Single(r => r.Service == "Many").Implementation);
    }

    [Fact]
    public void Prism_FactoryAndNamedOverloads_AreHandled()
    {
        var (registrations, _) = AnalyzeSemantic("""
            registry.Register<IFoo>(() => new Foo());
            registry.Register<IBar, Bar>("named");
            """);

        Assert.Equal("(factory)", registrations.Single(r => r.Service == "IFoo").Implementation);
        Assert.True(registrations.Single(r => r.Service == "IBar").Keyed);
    }

    [Fact]
    public void Prism_RegisterForNavigation_IsNotARegistration_ButIsNoted()
    {
        var (registrations, unrecognized) = AnalyzeSemantic("registry.RegisterForNavigation<ViewA>();");

        Assert.Empty(registrations);
        Assert.Equal(1, unrecognized["Prism navigation"]);
    }

    [Fact]
    public void Register_OnUnrelatedType_IsNotMatched()
    {
        var (registrations, unrecognized) = AnalyzeSemantic("ledger.Register(\"x\");");

        Assert.Empty(registrations);
        Assert.Empty(unrecognized);
    }

    [Fact]
    public void Autofac_RegisterTypeChains_MapServiceAndLifetime()
    {
        var (registrations, unrecognized) = AnalyzeSemantic("""
            builder.RegisterType<Foo>().As<IFoo>().SingleInstance();
            builder.RegisterType<Bar>().As<IBar>().InstancePerLifetimeScope();
            builder.RegisterType<Baz>().As<IBaz>();
            builder.RegisterType<Self>().AsSelf().InstancePerDependency();
            builder.Register(c => new Qux()).As<IQux>().SingleInstance();
            builder.RegisterModule<SomeModule>();
            """);

        Assert.Equal(("IFoo", "Foo", "singleton"), Triple(registrations, "IFoo"));
        Assert.Equal(("IBar", "Bar", "scoped"), Triple(registrations, "IBar"));
        Assert.Equal(("IBaz", "Baz", "transient"), Triple(registrations, "IBaz"));
        Assert.Equal(("Self", "Self", "transient"), Triple(registrations, "Self"));
        Assert.Equal("(factory)", registrations.Single(r => r.Service == "IQux").Implementation);
        Assert.Equal(5, registrations.Count);
        Assert.Equal(1, unrecognized["Autofac"]); // RegisterModule cannot be mapped
    }

    private static (string, string, string) Triple(List<DiRegistration> registrations, string service)
    {
        var r = registrations.Single(x => x.Service == service);
        return (r.Service, r.Implementation, r.Lifetime);
    }

    [Fact]
    public void AnalyzeSemantic_UnsupportedContainer_CountedAsUnrecognized()
    {
        var (registrations, unrecognized) = AnalyzeSemantic("""
            dry.Register<IFoo, Foo>();
            dry.Register<IBar, Bar>();
            """);

        Assert.Empty(registrations);
        Assert.Equal(2, unrecognized["DryIoc"]);
    }

    [Fact]
    public void BuildNote_ZeroRecognizedWithSkippedCalls_SaysZeroIsNotNoDi()
    {
        var note = DiContainerSupport.BuildNote(0, new Dictionary<string, int> { ["DryIoc"] = 12, ["Prism navigation"] = 3 });

        Assert.NotNull(note);
        Assert.Contains("15 call(s)", note);
        Assert.Contains("12 DryIoc", note);
        Assert.Contains("3 Prism navigation", note);
        Assert.Contains("0 does not mean", note);
    }

    [Fact]
    public void BuildNote_NothingSkipped_ReturnsNull() =>
        Assert.Null(DiContainerSupport.BuildNote(5, []));

    [Fact]
    public void Prism_DuplicatesAndCaptiveRisks_AreDetected()
    {
        var (registrations, _) = AnalyzeSemantic("""
            registry.RegisterSingleton<IWorker, Worker>();
            registry.RegisterScoped<IOrderRepository, OrderRepository>();
            registry.Register<IDup, A>();
            registry.Register<IDup, B>();
            """);
        var typeDeclarations = new Dictionary<string, TypeDeclarationSyntax>();
        var tree = CSharpSyntaxTree.ParseText("public class Worker(IOrderRepository repository) : IWorker { }", cancellationToken: TestContext.Current.CancellationToken);
        foreach (var t in tree.GetRoot(TestContext.Current.CancellationToken).DescendantNodes().OfType<TypeDeclarationSyntax>())
            typeDeclarations[t.Identifier.Text] = t;

        var risk = Assert.Single(GetDiRegistrationsTool.FindCaptiveRisks(registrations, typeDeclarations));
        Assert.Equal("IOrderRepository", risk.DependsOn);
        Assert.Equal("IDup", Assert.Single(GetDiRegistrationsTool.FindDuplicates(registrations)).Service);
    }

    [Fact]
    public async Task GetDiRegistrations_SampleSolutionHasNone_ReturnsEmpty()
    {
        var json = await GetDiRegistrationsTool.ExecuteAsync(
            fixture.WorkspaceManager, ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<DiRegistrationsResult>(json)!;

        Assert.Equal(0, result.Count);
        Assert.Empty(result.Duplicates);
        Assert.Empty(result.CaptiveRisks);
    }
}
