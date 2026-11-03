using CWM.RoslynNavigator.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CWM.RoslynNavigator.Tests.Analyzers;

public class NameOccurrenceIndexTests
{
    private static readonly string[] Sources =
    [
        """
        class Order { void Ship() { } }
        class OrderService { Order Load() => new Order(); }
        """,
        """
        // Mentions Ship in a comment, and "Archive" in a string.
        class Consumer { string Name = "Archive"; int this[int i] => i; }
        """,
        """
        class Helper_2 { void Ship_Later() { } void Über() { } }
        """,
        "",
    ];

    private static readonly string[] Names =
    [
        "Order", "OrderService", "Ship", "Archive", "Helper_2", "Ship_Later", "Über",
        "Consumer", "Load", "Missing", "rder", "this[]", "Helper", "",
    ];

    private static List<SyntaxTree> Parse() =>
        Sources.Select(s => CSharpSyntaxTree.ParseText(s, cancellationToken: TestContext.Current.CancellationToken)).ToList();

    [Fact]
    public void AppearsOutside_EveryNameAndDeclaringTree_MatchesFullTextScan()
    {
        var trees = Parse();
        var index = new NameOccurrenceIndex(Names);
        foreach (var tree in trees)
            index.Add(tree, tree.GetText(TestContext.Current.CancellationToken));

        SyntaxTree?[] declaringTrees = [.. trees, null];

        foreach (var name in Names)
        {
            foreach (var declaring in declaringTrees)
            {
                var expected = trees.Any(t =>
                    t != declaring && NameOccurrenceIndex.ContainsIdentifier(t.GetText().ToString(), name));

                Assert.True(
                    expected == index.AppearsOutside(name, declaring),
                    $"'{name}' declared in tree {trees.IndexOf(declaring!)}: expected {expected}");
            }
        }
    }

    [Fact]
    public void AppearsOutside_NameOnlyInDeclaringTree_IsFalse()
    {
        var trees = Parse();
        var index = new NameOccurrenceIndex(["Load"]);
        foreach (var tree in trees)
            index.Add(tree, tree.GetText(TestContext.Current.CancellationToken));

        Assert.False(index.AppearsOutside("Load", trees[0]));
        Assert.True(index.AppearsOutside("Load", trees[1]));
    }

    [Fact]
    public void AppearsOutside_LongerIdentifierContainingName_DoesNotCount()
    {
        var trees = Parse();
        var index = new NameOccurrenceIndex(["Ship", "Helper"]);
        foreach (var tree in trees)
            index.Add(tree, tree.GetText(TestContext.Current.CancellationToken));

        // "Ship_Later" and "Helper_2" must not count as mentions of "Ship" / "Helper"...
        Assert.False(index.AppearsOutside("Helper", trees[0]));
        // ...but the comment in tree 1 does mention Ship.
        Assert.True(index.AppearsOutside("Ship", trees[0]));
    }
}
