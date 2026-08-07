using System.ComponentModel;
using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ModelContextProtocol.Server;

namespace CWM.RoslynNavigator.Tools;

[McpServerToolType]
public static class GetEndpointMapTool
{
    private static readonly HashSet<string> MapVerbs =
        ["MapGet", "MapPost", "MapPut", "MapDelete", "MapPatch"];

    private static readonly Dictionary<string, string> HttpAttributeVerbs = new(StringComparer.Ordinal)
    {
        ["HttpGet"] = "GET",
        ["HttpPost"] = "POST",
        ["HttpPut"] = "PUT",
        ["HttpDelete"] = "DELETE",
        ["HttpPatch"] = "PATCH",
        ["HttpHead"] = "HEAD",
        ["HttpOptions"] = "OPTIONS",
    };

    [McpServerTool(Name = "get_endpoint_map"), Description("Inventory of ASP.NET Core endpoints: Minimal API Map{Get,Post,Put,Delete,Patch} calls (MapGroup prefixes composed when statically resolvable) and controller actions with Http* attributes. Reports auth posture per endpoint: 'authorized' ([Authorize]/RequireAuthorization), 'anonymous' ([AllowAnonymous]/AllowAnonymous()), or 'unmarked'. Best-effort static analysis: only string-literal routes resolve (dynamic routes show '?'), group variables are tracked per file only, and conventions applied elsewhere (global filters, UseEndpoints conventions) are not seen.")]
    public static async Task<string> ExecuteAsync(
        WorkspaceManager workspace,
        [Description("Optional: filter to endpoints in files whose path contains this value")] string? file = null,
        [Description("Maximum endpoints to return. TotalFound in the response reports the full count; re-query with a higher value if it exceeds Count.")] int maxResults = 100,
        CancellationToken ct = default)
    {
        var notReady = await workspace.EnsureReadyOrStatusAsync(ct);
        if (notReady is not null) return notReady;

        var solution = workspace.GetSolution();
        if (solution is null)
            return Serialize(Paging.Empty<EndpointEntry>(maxResults));

        var all = new List<EndpointEntry>();
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var document in solution.Projects.SelectMany(p => p.Documents))
        {
            ct.ThrowIfCancellationRequested();

            if (document.FilePath is null || !seenFiles.Add(document.FilePath))
                continue;

            if (file is not null && !document.FilePath.Contains(file, StringComparison.OrdinalIgnoreCase))
                continue;

            var root = await document.GetSyntaxRootAsync(ct);
            if (root is null) continue;

            foreach (var endpoint in AnalyzeRoot(root, document.FilePath))
            {
                all.Add(endpoint with { File = workspace.ToRelativePath(endpoint.File) });
            }
        }

        return Serialize(Paging.Apply(all, maxResults));
    }

    private static string Serialize(Paging.Page<EndpointEntry> page) =>
        JsonSerializer.Serialize(new EndpointMapResult(
            page.Items, page.Count, page.TotalFound, page.Truncated, page.Limit));

    /// <summary>
    /// Syntax-only endpoint extraction for one file. Internal for direct unit testing.
    /// </summary>
    internal static List<EndpointEntry> AnalyzeRoot(SyntaxNode root, string filePath)
    {
        var results = new List<EndpointEntry>();

        AnalyzeMinimalApis(root, filePath, results);
        AnalyzeControllers(root, filePath, results);

        return results;
    }

    private static void AnalyzeMinimalApis(SyntaxNode root, string filePath, List<EndpointEntry> results)
    {
        // Pass 1: group variables — var group = app.MapGroup("/api/orders")...;
        var groupPrefixes = new Dictionary<string, string>(StringComparer.Ordinal);
        var groupAuth = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var declarator in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (declarator.Initializer?.Value is not { } initializer)
                continue;

            var (prefix, receiver, hasAuth, hasAnon) = AnalyzeGroupChain(initializer);
            if (prefix is null)
                continue;

            var basePrefix = receiver is not null && groupPrefixes.TryGetValue(receiver, out var parent) ? parent : "";
            var name = declarator.Identifier.Text;
            groupPrefixes[name] = CombineRoutes(basePrefix, prefix);

            var inherited = receiver is not null && groupAuth.TryGetValue(receiver, out var parentAuth) ? parentAuth : null;
            var auth = hasAnon ? "anonymous" : hasAuth ? "authorized" : inherited;
            if (auth is not null)
                groupAuth[name] = auth;
        }

        // Pass 2: standalone group.RequireAuthorization(); / group.AllowAnonymous(); statements
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Parent is not ExpressionStatementSyntax) continue;
            if (invocation.Expression is not MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax id } access) continue;
            if (!groupPrefixes.ContainsKey(id.Identifier.Text)) continue;

            switch (access.Name.Identifier.Text)
            {
                case "RequireAuthorization":
                    groupAuth[id.Identifier.Text] = "authorized";
                    break;
                case "AllowAnonymous":
                    groupAuth[id.Identifier.Text] = "anonymous";
                    break;
            }
        }

        // Pass 3: Map{Verb} invocations
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax access) continue;

            var methodName = access.Name.Identifier.Text;
            if (!MapVerbs.Contains(methodName)) continue;

            var route = GetFirstStringArgument(invocation) ?? "?";

            var prefix = "";
            string? inheritedAuth = null;

            if (access.Expression is IdentifierNameSyntax receiverId)
            {
                if (groupPrefixes.TryGetValue(receiverId.Identifier.Text, out var p))
                    prefix = p;
                if (groupAuth.TryGetValue(receiverId.Identifier.Text, out var a))
                    inheritedAuth = a;
            }
            else
            {
                // Chained: app.MapGroup("/x").MapGet(...)
                var (chainPrefix, chainReceiver, chainAuth, chainAnon) = AnalyzeGroupChain(access.Expression);
                if (chainPrefix is not null)
                {
                    var basePrefix = chainReceiver is not null && groupPrefixes.TryGetValue(chainReceiver, out var parent) ? parent : "";
                    prefix = CombineRoutes(basePrefix, chainPrefix);
                    inheritedAuth = chainAnon ? "anonymous" : chainAuth ? "authorized" : inheritedAuth;
                }
            }

            var auth = GetFluentAuth(invocation) ?? inheritedAuth ?? "unmarked";
            var line = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

            results.Add(new EndpointEntry(
                Method: methodName[3..].ToUpperInvariant(),
                Route: CombineRoutes(prefix, route),
                Auth: auth,
                Kind: "minimal-api",
                File: filePath,
                Line: line));
        }
    }

    private static void AnalyzeControllers(SyntaxNode root, string filePath, List<EndpointEntry> results)
    {
        foreach (var cls in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            var isController = HasAttribute(cls.AttributeLists, "ApiController")
                || (cls.BaseList?.Types.Any(t =>
                        t.Type.ToString() is "ControllerBase" or "Controller") ?? false);

            if (!isController) continue;

            var controllerName = cls.Identifier.Text.EndsWith("Controller", StringComparison.Ordinal)
                ? cls.Identifier.Text[..^"Controller".Length]
                : cls.Identifier.Text;

            var classRoute = (GetAttributeStringArgument(cls.AttributeLists, "Route") ?? "")
                .Replace("[controller]", controllerName, StringComparison.OrdinalIgnoreCase);

            var classAuth = HasAttribute(cls.AttributeLists, "AllowAnonymous") ? "anonymous"
                : HasAttribute(cls.AttributeLists, "Authorize") ? "authorized"
                : null;

            foreach (var method in cls.Members.OfType<MethodDeclarationSyntax>())
            {
                var http = FindHttpAttribute(method.AttributeLists);
                if (http is null) continue;

                var (verb, template) = http.Value;
                var methodRoute = (GetAttributeStringArgument(method.AttributeLists, "Route") ?? template ?? "")
                    .Replace("[controller]", controllerName, StringComparison.OrdinalIgnoreCase);

                var auth = HasAttribute(method.AttributeLists, "AllowAnonymous") ? "anonymous"
                    : HasAttribute(method.AttributeLists, "Authorize") ? "authorized"
                    : classAuth ?? "unmarked";

                results.Add(new EndpointEntry(
                    Method: verb,
                    Route: CombineRoutes(classRoute, methodRoute),
                    Auth: auth,
                    Kind: "controller",
                    File: filePath,
                    Line: method.GetLocation().GetLineSpan().StartLinePosition.Line + 1));
            }
        }
    }

    /// <summary>
    /// Walks a fluent chain inward looking for a MapGroup call. Returns its string-literal
    /// prefix, the identifier it was called on (a parent group variable, if any), and
    /// whether the chain applies RequireAuthorization/AllowAnonymous.
    /// </summary>
    private static (string? Prefix, string? Receiver, bool HasAuth, bool HasAnon) AnalyzeGroupChain(ExpressionSyntax expression)
    {
        string? prefix = null;
        string? receiver = null;
        var hasAuth = false;
        var hasAnon = false;

        var current = expression;
        while (current is InvocationExpressionSyntax invocation
               && invocation.Expression is MemberAccessExpressionSyntax access)
        {
            switch (access.Name.Identifier.Text)
            {
                case "RequireAuthorization":
                    hasAuth = true;
                    break;
                case "AllowAnonymous":
                    hasAnon = true;
                    break;
                case "MapGroup":
                    prefix = GetFirstStringArgument(invocation) ?? "?";
                    if (access.Expression is IdentifierNameSyntax id)
                        receiver = id.Identifier.Text;
                    return (prefix, receiver, hasAuth, hasAnon);
            }

            current = access.Expression;
        }

        return (prefix, receiver, hasAuth, hasAnon);
    }

    /// <summary>
    /// Checks the fluent calls applied to a Map invocation's result, e.g.
    /// app.MapGet(...).WithName("x").RequireAuthorization().
    /// </summary>
    private static string? GetFluentAuth(InvocationExpressionSyntax mapInvocation)
    {
        SyntaxNode node = mapInvocation;
        while (node.Parent is MemberAccessExpressionSyntax access
               && access.Parent is InvocationExpressionSyntax outer)
        {
            switch (access.Name.Identifier.Text)
            {
                case "RequireAuthorization":
                    return "authorized";
                case "AllowAnonymous":
                    return "anonymous";
            }

            node = outer;
        }

        return null;
    }

    internal static string CombineRoutes(string prefix, string route)
    {
        if (string.IsNullOrEmpty(prefix)) return route;
        if (string.IsNullOrEmpty(route) || route == "/") return prefix;
        return $"{prefix.TrimEnd('/')}/{route.TrimStart('/')}";
    }

    private static string? GetFirstStringArgument(InvocationExpressionSyntax invocation)
    {
        var arg = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
        return arg is LiteralExpressionSyntax { Token.Value: string value } ? value : null;
    }

    private static bool HasAttribute(SyntaxList<AttributeListSyntax> lists, string name) =>
        lists.SelectMany(l => l.Attributes)
            .Any(a => MatchesAttributeName(a, name));

    private static string? GetAttributeStringArgument(SyntaxList<AttributeListSyntax> lists, string name)
    {
        var attribute = lists.SelectMany(l => l.Attributes)
            .FirstOrDefault(a => MatchesAttributeName(a, name));

        var arg = attribute?.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
        return arg is LiteralExpressionSyntax { Token.Value: string value } ? value : null;
    }

    private static (string Verb, string? Template)? FindHttpAttribute(SyntaxList<AttributeListSyntax> lists)
    {
        foreach (var attribute in lists.SelectMany(l => l.Attributes))
        {
            var name = GetSimpleAttributeName(attribute);
            if (HttpAttributeVerbs.TryGetValue(name, out var verb))
            {
                var arg = attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
                var template = arg is LiteralExpressionSyntax { Token.Value: string value } ? value : null;
                return (verb, template);
            }
        }

        return null;
    }

    private static bool MatchesAttributeName(AttributeSyntax attribute, string name) =>
        GetSimpleAttributeName(attribute) == name; // suffix already stripped

    private static string GetSimpleAttributeName(AttributeSyntax attribute)
    {
        var name = attribute.Name switch
        {
            QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
            SimpleNameSyntax simple => simple.Identifier.Text,
            _ => attribute.Name.ToString()
        };

        return name.EndsWith("Attribute", StringComparison.Ordinal) ? name[..^"Attribute".Length] : name;
    }
}
