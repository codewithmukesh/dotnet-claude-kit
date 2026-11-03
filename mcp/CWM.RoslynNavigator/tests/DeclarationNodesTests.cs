using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CWM.RoslynNavigator.Tests;

public class DeclarationNodesTests
{
    private const string Source = """
        using System;

        namespace Outer
        {
            public delegate void Handler(int value);

            public enum Color { Red, Green }

            public record Person(string Name, int Age)
            {
                public string Upper => Name.ToUpperInvariant();
            }

            public sealed class Service(string name)
            {
                private readonly int _a = 1, _b = 2;
                private const string Prefix = "x";
                public event EventHandler? Changed, Closed;
                public event EventHandler Explicit { add { } remove { } }
                public int Count { get; set; }
                public int this[int i] => i;
                public Service() : this("default") { }
                ~Service() { }
                public static Service operator +(Service left, Service right) => left;
                public static implicit operator string(Service s) => s.ToString();

                public int Compute(int x)
                {
                    var local = x + 1;
                    Func<int, int> lambda = y => y * 2;
                    var anonymous = new { Value = local };
                    var tuple = (First: local, Second: "s");
                    int LocalFunction(int z) => z;
                    return lambda(LocalFunction(anonymous.Value)) + tuple.First + _a + _b + name.Length;
                }

                private sealed class Nested
                {
                    internal void Inner() { }
                }
            }

            public interface IShape { double Area(); }

            public struct Point { public int X; }
        }

        namespace FileScoped.Like
        {
            internal static class Extensions
            {
                public static int Twice(this int value) => value * 2;
            }
        }
        """;

    private static (SemanticModel Model, SyntaxNode Root) Compile()
    {
        var tree = CSharpSyntaxTree.ParseText(Source, cancellationToken: TestContext.Current.CancellationToken);
        var compilation = CSharpCompilation.Create(
            "DeclarationNodesTests",
            [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return (compilation.GetSemanticModel(tree), tree.GetRoot(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Symbols that live inside a member body (or are accessors/parameters of a member).
    /// These are the only symbols the declaration walk is allowed to drop.
    /// </summary>
    private static bool IsBodyOrSignatureLocal(ISymbol symbol)
    {
        if (symbol is ILocalSymbol or IParameterSymbol or IRangeVariableSymbol or ILabelSymbol or ITypeParameterSymbol)
            return true;

        if (symbol is IMethodSymbol method && method.MethodKind is MethodKind.LocalFunction
                or MethodKind.AnonymousFunction or MethodKind.PropertyGet or MethodKind.PropertySet
                or MethodKind.EventAdd or MethodKind.EventRemove)
            return true;

        var type = symbol as INamedTypeSymbol ?? symbol.ContainingType;
        return type is { IsAnonymousType: true } or { IsTupleType: true };
    }

    [Fact]
    public void Members_FindsEveryTypeAndMemberSymbol_TheFullWalkFinds()
    {
        var (model, root) = Compile();
        var ct = TestContext.Current.CancellationToken;

        var fullWalk = root.DescendantNodes()
            .Select(n => model.GetDeclaredSymbol(n, ct))
            .Where(s => s is not null and not INamespaceSymbol && !IsBodyOrSignatureLocal(s))
            .Select(s => s!.ToDisplayString())
            .ToList();

        var declarationWalk = DeclarationNodes.Members(root)
            .Select(n => model.GetDeclaredSymbol(n, ct))
            .Where(s => s is not null and not INamespaceSymbol)
            .Select(s => s!.ToDisplayString())
            .ToList();

        Assert.Equal(fullWalk, declarationWalk);
        Assert.Contains("Outer.Handler", declarationWalk);
        Assert.Contains("Outer.Color.Green", declarationWalk);
        Assert.Contains("Outer.Person", declarationWalk);
        Assert.Contains("Outer.Service._b", declarationWalk);
        Assert.Contains("Outer.Service.Closed", declarationWalk);
        Assert.Contains("Outer.Service.Explicit", declarationWalk);
        Assert.Contains("Outer.Service.Nested.Inner()", declarationWalk);
        Assert.Contains("FileScoped.Like.Extensions.Twice(int)", declarationWalk);
    }

    [Fact]
    public void Members_SkipsSymbolsInsideMemberBodies()
    {
        var (model, root) = Compile();
        var ct = TestContext.Current.CancellationToken;

        var symbols = DeclarationNodes.Members(root)
            .Select(n => model.GetDeclaredSymbol(n, ct))
            .ToList();

        Assert.DoesNotContain(symbols, s => s is ILocalSymbol or IParameterSymbol);
        Assert.DoesNotContain(symbols, s => s is IMethodSymbol { MethodKind: MethodKind.LocalFunction });
        Assert.DoesNotContain(symbols, s => s is INamedTypeSymbol { IsAnonymousType: true });
        Assert.DoesNotContain(symbols, s => s is INamedTypeSymbol { IsTupleType: true });
    }

    [Fact]
    public void Types_FindsEveryNamedTypeTheFullWalkFinds()
    {
        var (model, root) = Compile();
        var ct = TestContext.Current.CancellationToken;

        var fullWalk = root.DescendantNodes()
            .Select(n => model.GetDeclaredSymbol(n, ct))
            .OfType<INamedTypeSymbol>()
            .Where(t => !t.IsAnonymousType && !t.IsTupleType)
            .Select(t => t.ToDisplayString())
            .ToList();

        var declarationWalk = DeclarationNodes.Types(root)
            .Select(n => model.GetDeclaredSymbol(n, ct))
            .OfType<INamedTypeSymbol>()
            .Select(t => t.ToDisplayString())
            .ToList();

        Assert.Equal(fullWalk, declarationWalk);
        Assert.Equal(
            ["Outer.Handler", "Outer.Color", "Outer.Person", "Outer.Service", "Outer.Service.Nested",
             "Outer.IShape", "Outer.Point", "FileScoped.Like.Extensions"],
            declarationWalk);
    }
}
