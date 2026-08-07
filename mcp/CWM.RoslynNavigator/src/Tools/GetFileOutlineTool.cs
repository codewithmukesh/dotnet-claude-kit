using System.ComponentModel;
using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ModelContextProtocol.Server;

namespace CWM.RoslynNavigator.Tools;

[McpServerToolType]
public static class GetFileOutlineTool
{
    private const int MaxNestingDepth = 3;

    [McpServerTool(Name = "get_file_outline"), Description("Get a token-cheap skeleton of one source file: namespace, types, and member signatures with line numbers — no bodies, no usings. Use before reading a file to decide which lines are worth reading. Nested types are included up to 3 levels deep.")]
    public static async Task<string> ExecuteAsync(
        WorkspaceManager workspace,
        [Description("File path (suffix match, e.g. 'OrderService.cs' or 'Services/OrderService.cs')")] string filePath,
        [Description("Maximum members to return across all types. TotalFound in the response reports the full count; re-query with a higher value if it exceeds Count.")] int maxResults = 200,
        CancellationToken ct = default)
    {
        var notReady = await workspace.EnsureReadyOrStatusAsync(ct);
        if (notReady is not null) return notReady;

        var solution = workspace.GetSolution();
        if (solution is null)
            return JsonSerializer.Serialize(new FileOutlineResult(
                "unknown", null, 0, [], 0, 0, false, Math.Max(1, maxResults)));

        var document = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.FilePath?.EndsWith(filePath, StringComparison.OrdinalIgnoreCase) == true);

        if (document?.FilePath is null)
            return JsonSerializer.Serialize(new ErrorResponse(ErrorCodes.FileNotFound,
                $"File '{filePath}' is not part of any project in the solution."));

        var root = await document.GetSyntaxRootAsync(ct);
        if (root is null)
            return JsonSerializer.Serialize(new ErrorResponse(ErrorCodes.NoSource,
                $"File '{filePath}' has no syntax tree."));

        var usingCount = root.DescendantNodes(n => n is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax)
            .OfType<UsingDirectiveSyntax>()
            .Count();

        var namespaceName = root.DescendantNodes()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .FirstOrDefault()?.Name.ToString();

        var budget = new OutlineBudget(Math.Max(1, maxResults));
        var types = new List<TypeOutline>();

        foreach (var typeDecl in TopLevelTypes(root))
        {
            ct.ThrowIfCancellationRequested();
            types.Add(BuildTypeOutline(typeDecl, depth: 1, budget));
        }

        return JsonSerializer.Serialize(new FileOutlineResult(
            File: workspace.ToRelativePath(document.FilePath),
            Namespace: namespaceName,
            UsingCount: usingCount,
            Types: types,
            Count: budget.Returned,
            TotalFound: budget.Total,
            Truncated: budget.Total > budget.Returned,
            Limit: Math.Max(1, maxResults)));
    }

    private sealed class OutlineBudget(int maxMembers)
    {
        public int MaxMembers { get; } = maxMembers;
        public int Returned { get; set; }
        public int Total { get; set; }
    }

    private static IEnumerable<BaseTypeDeclarationSyntax> TopLevelTypes(SyntaxNode root) =>
        root.DescendantNodes(n => n is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax)
            .OfType<BaseTypeDeclarationSyntax>();

    private static TypeOutline BuildTypeOutline(BaseTypeDeclarationSyntax typeDecl, int depth, OutlineBudget budget)
    {
        var members = new List<MemberOutline>();
        var nested = new List<TypeOutline>();

        IEnumerable<MemberDeclarationSyntax> children = typeDecl switch
        {
            TypeDeclarationSyntax t => t.Members,
            EnumDeclarationSyntax e => e.Members,
            _ => []
        };

        foreach (var member in children)
        {
            if (member is BaseTypeDeclarationSyntax nestedType)
            {
                if (depth < MaxNestingDepth)
                    nested.Add(BuildTypeOutline(nestedType, depth + 1, budget));
                continue;
            }

            budget.Total++;
            if (budget.Returned >= budget.MaxMembers)
                continue;

            budget.Returned++;
            members.Add(new MemberOutline(
                Kind: SyntaxSignatures.GetMemberKind(member),
                Signature: SyntaxSignatures.GetMemberSignature(member),
                Line: member.GetLocation().GetLineSpan().StartLinePosition.Line + 1));
        }

        return new TypeOutline(
            Name: typeDecl.Identifier.Text,
            Kind: SyntaxSignatures.GetMemberKind(typeDecl),
            Line: typeDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
            Members: members,
            NestedTypes: nested);
    }
}
