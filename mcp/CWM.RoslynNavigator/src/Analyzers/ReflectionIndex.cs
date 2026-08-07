using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CWM.RoslynNavigator.Analyzers;

/// <summary>
/// Solution-wide record of the two ways a symbol can be used without producing a
/// reference Roslyn can find: named in a string literal, or picked up by an assembly
/// scan. Both make a zero-reference result meaningless, so dead-code detection consults
/// this before calling anything unused.
/// </summary>
internal sealed class ReflectionIndex
{
    /// <summary>
    /// Method names whose presence anywhere in the solution means types are being
    /// discovered by scanning rather than by direct reference.
    /// </summary>
    private static readonly HashSet<string> ScanningMethods = new(StringComparer.Ordinal)
    {
        "GetType",
        "CreateInstance",
        "GetTypes",
        "GetExportedTypes",
        "AddClasses",
        "FromAssemblyOf",
        "FromAssemblies",
        "FromCallingAssembly",
        "FromExecutingAssembly",
        "FromApplicationDependencies",
        "ApplyConfigurationsFromAssembly",
        "RegisterServicesFromAssembly",
        "RegisterServicesFromAssemblies",
        "AddValidatorsFromAssembly",
        "AddValidatorsFromAssemblyContaining",
        "AddAutoMapper",
        "AddMediatR",
        "Scan"
    };

    private readonly HashSet<string> _literalNames = new(StringComparer.Ordinal);

    /// <summary>
    /// True when the solution discovers types by scanning. Every zero-reference result is
    /// softer in that case, so it is reported alongside the findings rather than silently.
    /// </summary>
    public bool AssemblyScanningDetected { get; private set; }

    /// <summary>
    /// Whether a symbol name appears in a string literal anywhere in the solution.
    /// Dotted literals contribute their final segment too, so "MyApp.Jobs.CleanupJob"
    /// also protects a type named CleanupJob.
    /// </summary>
    public bool IsNamedInStringLiteral(string name) => _literalNames.Contains(name);

    public void Add(SyntaxNode root, CancellationToken ct)
    {
        foreach (var node in root.DescendantNodes())
        {
            ct.ThrowIfCancellationRequested();

            switch (node)
            {
                case LiteralExpressionSyntax literal
                    when literal.IsKind(SyntaxKind.StringLiteralExpression):
                    AddLiteral(literal.Token.ValueText);
                    break;

                // Only the constant parts of an interpolated string are usable as names,
                // but a prefix like "MyApp.Handlers." still tells us nothing on its own,
                // so interpolated strings are deliberately not indexed.

                case InvocationExpressionSyntax invocation
                    when IsScanningCall(invocation):
                    AssemblyScanningDetected = true;
                    break;
            }
        }
    }

    private void AddLiteral(string value)
    {
        if (value.Length == 0 || value.Length > 512) return;

        _literalNames.Add(value);

        var lastDot = value.LastIndexOf('.');
        if (lastDot >= 0 && lastDot < value.Length - 1)
            _literalNames.Add(value[(lastDot + 1)..]);
    }

    private static bool IsScanningCall(InvocationExpressionSyntax invocation)
    {
        var name = invocation.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            GenericNameSyntax generic => generic.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => null
        };

        return name is not null && ScanningMethods.Contains(name);
    }
}
