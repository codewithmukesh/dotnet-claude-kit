using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CWM.RoslynNavigator;

/// <summary>
/// Enumerates the nodes that declare named members, without descending into member bodies.
/// Calling <c>GetDeclaredSymbol</c> on every descendant node binds method bodies to resolve
/// locals and lambda parameters — on a 39-project solution that walk took 26.5s, against
/// about 1s for declaration nodes only. Tools that only report types and members use these
/// walks instead.
/// </summary>
internal static class DeclarationNodes
{
    /// <summary>
    /// Every node in <paramref name="root"/> for which <c>GetDeclaredSymbol</c> returns a type
    /// or member symbol, in document order: types (classes, structs, interfaces, records, enums),
    /// delegates, methods, constructors, operators, properties, indexers, events, enum members,
    /// and the individual variable declarators of field and event-field declarations (the
    /// declaration itself has no symbol). Namespace declarations are yielded too, so callers
    /// filter by symbol kind. Local functions, lambdas, accessors and anything
    /// else inside a member body is not visited.
    /// </summary>
    public static IEnumerable<SyntaxNode> Members(SyntaxNode root)
    {
        foreach (var member in root
            .DescendantNodes(static node => node is CompilationUnitSyntax
                or BaseNamespaceDeclarationSyntax
                or BaseTypeDeclarationSyntax)
            .OfType<MemberDeclarationSyntax>())
        {
            if (member is BaseFieldDeclarationSyntax field)
            {
                foreach (var variable in field.Declaration.Variables)
                    yield return variable;
            }
            else
            {
                yield return member;
            }
        }
    }

    /// <summary>
    /// Every type and delegate declaration in <paramref name="root"/>, including nested types,
    /// in document order.
    /// </summary>
    public static IEnumerable<MemberDeclarationSyntax> Types(SyntaxNode root) =>
        root.DescendantNodes(static node => node is CompilationUnitSyntax
                or BaseNamespaceDeclarationSyntax
                or TypeDeclarationSyntax)
            .OfType<MemberDeclarationSyntax>()
            .Where(static node => node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax);
}
