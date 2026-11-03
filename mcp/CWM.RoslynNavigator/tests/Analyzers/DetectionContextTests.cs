using CWM.RoslynNavigator.Analyzers;
using Microsoft.CodeAnalysis.CSharp;

namespace CWM.RoslynNavigator.Tests.Analyzers;

public class DetectionContextTests
{
    private static DetectionContext Parse(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: "src/Api/Thing.cs");
        return new DetectionContext(
            tree, model: null, SourceKind.Production, TestContext.Current.CancellationToken);
    }

    [Fact]
    public void InlineIgnore_MarkerOnSameLine_SuppressesThatLine()
    {
        var context = Parse("""
            class C
            {
                void M()
                {
                    var now = System.DateTime.UtcNow; // cwm:ignore AP004 — legacy seeder
                }
            }
            """);

        Assert.True(context.IsSuppressedAt("AP004", 5));
        Assert.False(context.IsSuppressedAt("AP005", 5));
    }

    [Fact]
    public void InlineIgnore_MarkerOnPrecedingLine_SuppressesLineBelow()
    {
        var context = Parse("""
            class C
            {
                void M()
                {
                    // cwm:ignore AP004 — documented wall-clock requirement
                    var now = System.DateTime.UtcNow;
                }
            }
            """);

        Assert.True(context.IsSuppressedAt("AP004", 6));
    }

    [Fact]
    public void InlineIgnore_SeveralIdsInOneMarker_SuppressesEach()
    {
        var context = Parse("""
            class C
            {
                void M()
                {
                    // cwm:ignore AP004, AP005 — both sanctioned here
                    var now = System.DateTime.UtcNow;
                }
            }
            """);

        Assert.True(context.IsSuppressedAt("AP004", 6));
        Assert.True(context.IsSuppressedAt("AP005", 6));
        Assert.False(context.IsSuppressedAt("AP010", 6));
    }

    [Fact]
    public void InlineIgnore_MarkerAboveOneLine_DoesNotSuppressDistantLines()
    {
        var context = Parse("""
            class C
            {
                void M()
                {
                    // cwm:ignore AP004 — only this one
                    var a = System.DateTime.UtcNow;
                    var b = System.DateTime.UtcNow;
                }
            }
            """);

        Assert.True(context.IsSuppressedAt("AP004", 6));
        Assert.False(context.IsSuppressedAt("AP004", 7));
    }

    [Fact]
    public void SuppressMessageAttribute_OnMethod_CoversWholeDeclaration()
    {
        var context = Parse("""
            class C
            {
                [System.Diagnostics.CodeAnalysis.SuppressMessage("CWM", "AP005", Justification = "boundary")]
                void M()
                {
                    try { } catch (System.Exception) { }
                }
            }
            """);

        Assert.True(context.IsSuppressedAt("AP005", 6));
        Assert.False(context.IsSuppressedAt("AP007", 6));
    }

    [Fact]
    public void SuppressMessageAttribute_PrefixedCheckId_Suppresses()
    {
        var context = Parse("""
            class C
            {
                [SuppressMessage("CWM", "CWM:AP010", Justification = "load to mutate")]
                void M() { }
            }
            """);

        Assert.True(context.IsSuppressedAt("AP010", 4));
    }

    [Fact]
    public void IsSuppressedAt_NoMarker_ReturnsFalse()
    {
        var context = Parse("""
            class C
            {
                // TODO: revisit this AP004 situation later
                void M() { }
            }
            """);

        Assert.False(context.IsSuppressedAt("AP004", 4));
    }
}
