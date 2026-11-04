using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CWM.RoslynNavigator.Tools;

/// <summary>
/// Which classes in the solution are MVC controllers, and the route and auth each inherits.
/// <para>
/// A controller is rarely declared against <c>ControllerBase</c> directly in a large codebase:
/// Jellyfin's API controllers derive from <c>BaseJellyfinApiController</c>, which carries
/// <c>[ApiController]</c> and <c>[Route("[controller]")]</c>. Looking only at a class's own
/// declaration found one controller in Jellyfin. The index follows each class's base chain by
/// simple name across every file, the way ASP.NET Core inherits <c>[ApiController]</c>,
/// <c>[Route]</c> and <c>[Authorize]</c> (all are <c>Inherited = true</c>). Syntax only: names
/// are matched without binding, and the first declaration of a name wins.
/// </para>
/// </summary>
internal sealed class ControllerIndex
{
    private static readonly HashSet<string> FrameworkBases = ["ControllerBase", "Controller"];

    private readonly Dictionary<string, Declaration> _classes = new(StringComparer.Ordinal);

    /// <summary>What one class declaration contributes: its base class name and class-level attributes.</summary>
    internal sealed record Declaration(string Name, string? Base, string? Route, string? Auth, bool ApiController);

    /// <summary>The classes declared in one syntax tree; pure, so safe to call in parallel.</summary>
    public static List<Declaration> Collect(SyntaxNode root) =>
        [.. root.DescendantNodes().OfType<ClassDeclarationSyntax>().Select(Describe)];

    /// <summary>An index over a single tree.</summary>
    public static ControllerIndex For(SyntaxNode root)
    {
        var index = new ControllerIndex();
        index.Add(Collect(root));
        return index;
    }

    /// <summary>Adds declarations from <see cref="Collect"/>; the first declaration of a name wins.</summary>
    public void Add(IEnumerable<Declaration> declarations)
    {
        foreach (var declaration in declarations)
            _classes.TryAdd(declaration.Name, declaration);
    }

    /// <summary>
    /// True for a class with <c>[ApiController]</c>, or deriving — directly or through other
    /// classes in the solution — from <c>ControllerBase</c>/<c>Controller</c> or an
    /// <c>[ApiController]</c> class.
    /// </summary>
    public bool IsController(string name) =>
        Walk(name).Any(c => c.ApiController || (c.Base is not null && FrameworkBases.Contains(c.Base)));

    /// <summary>The nearest <c>[Route]</c> template on the class or its bases, or null.</summary>
    public string? Route(string name) => Walk(name).Select(c => c.Route).FirstOrDefault(r => r is not null);

    /// <summary>The nearest class-level auth posture ("authorized"/"anonymous") on the class or its bases.</summary>
    public string? Auth(string name) => Walk(name).Select(c => c.Auth).FirstOrDefault(a => a is not null);

    /// <summary>The class and its bases in the solution, nearest first; stops at a cycle or an unknown base.</summary>
    private IEnumerable<Declaration> Walk(string name)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = name;
        while (current is not null && seen.Add(current) && _classes.TryGetValue(current, out var declaration))
        {
            yield return declaration;
            current = declaration.Base;
        }
    }

    private static Declaration Describe(ClassDeclarationSyntax cls)
    {
        // C# lists the base class first; an interface in that slot never resolves to a controller.
        var baseName = cls.BaseList?.Types.FirstOrDefault()?.Type is { } type ? SimpleName(type) : null;
        var lists = cls.AttributeLists;

        var auth = GetEndpointMapTool.HasAttribute(lists, "AllowAnonymous") ? "anonymous"
            : GetEndpointMapTool.HasAttribute(lists, "Authorize") ? "authorized"
            : null;

        return new Declaration(
            cls.Identifier.Text,
            baseName,
            GetEndpointMapTool.GetAttributeStringArgument(lists, "Route"),
            auth,
            GetEndpointMapTool.HasAttribute(lists, "ApiController"));
    }

    /// <summary>"Ns.Base&lt;T&gt;" → "Base".</summary>
    private static string SimpleName(TypeSyntax type) => type switch
    {
        QualifiedNameSyntax qualified => SimpleName(qualified.Right),
        AliasQualifiedNameSyntax alias => SimpleName(alias.Name),
        SimpleNameSyntax simple => simple.Identifier.Text,
        _ => type.ToString(),
    };
}
