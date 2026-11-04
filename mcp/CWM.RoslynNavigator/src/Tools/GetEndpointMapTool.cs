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

    [McpServerTool(Name = "get_endpoint_map"), Description("ASP.NET Core endpoints: Minimal API Map calls (MapGroup prefixes composed) and controller actions, with HTTP method, route, file, and auth (authorized, anonymous, or unmarked). Static analysis: only literal routes resolve; others show ?.")]
    public static async Task<string> ExecuteAsync(
        WorkspaceManager workspace,
        [Description("Path substring filter")] string? file = null,
        int maxResults = 500,
        int? offset = null,
        string? format = null,
        CancellationToken ct = default)
    {
        var notReady = await workspace.EnsureReadyOrStatusAsync(ct);
        if (notReady is not null) return notReady;
        if (ListFormat.Validate(format) is { } invalid) return invalid;

        var solution = workspace.GetSolution();
        if (solution is null)
            return ToolJson.Serialize(new EndpointMapResult([], 0, 0, false, Math.Max(1, maxResults)));

        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var documents = solution.Projects
            .SelectMany(p => p.Documents)
            .Where(d => d.FilePath is not null && seenFiles.Add(d.FilePath))
            .ToArray();

        bool Selected(Document d) => file is null || d.FilePath!.Contains(file, StringComparison.OrdinalIgnoreCase);

        // A filter naming no file is a mistake to report, not "this file has no endpoints".
        if (file is not null && !documents.Any(Selected))
            return ArgumentChecks.FileFilterMatchesNothing(file);

        // Pass 1, every file: class declarations, so a controller whose base controller lives
        // in another file (or outside the file filter) is still recognised.
        var roots = new SyntaxNode?[documents.Length];
        var declarations = new List<ControllerIndex.Declaration>[documents.Length];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, documents.Length),
            new ParallelOptions { CancellationToken = ct },
            async (index, token) =>
            {
                roots[index] = await documents[index].GetSyntaxRootAsync(token);
                declarations[index] = roots[index] is { } root ? ControllerIndex.Collect(root) : [];
            });

        var controllers = new ControllerIndex();
        foreach (var fileDeclarations in declarations)
            controllers.Add(fileDeclarations);

        // Pass 2, selected files, in parallel into per-document slots flattened in document
        // order, so the result and its paging match a sequential pass.
        var perDocument = new List<(EndpointEntry Entry, string Project)>[documents.Length];
        Parallel.For(0, documents.Length, new ParallelOptions { CancellationToken = ct }, index =>
        {
            var document = documents[index];
            perDocument[index] = roots[index] is { } root && Selected(document)
                ? [.. AnalyzeRoot(root, document.FilePath!, controllers)
                    .Select(endpoint => (endpoint with { File = workspace.ToRelativePath(endpoint.File) }, ListFormat.ProjectName(document.Project)))]
                : [];
        });

        var all = perDocument.SelectMany(entries => entries).ToList();
        var page = Paging.Apply(all, maxResults, offset ?? 0);
        var entries = page.WithItems(page.Items.Select(e => e.Entry).ToList());

        var byProject = entries.Truncated || entries.Offset > 0 ? ListFormat.CountBy(all, e => e.Project) : null;

        return ListFormat.Render(
            entries,
            format,
            p => ToolJson.Serialize(new EndpointMapResult(
                p.Items, p.Count, p.TotalFound, p.Truncated, p.Limit, p.Offset, p.NextOffset, ByProject: byProject)),
            p => ToolJson.Serialize(new EndpointMapResult(
                null, p.Count, p.TotalFound, p.Truncated, p.Limit, p.Offset, p.NextOffset,
                Files: ListFormat.GroupByFile(p.Items, e => e.File, CompactEntry),
                ByProject: byProject)));
    }

    /// <summary>"35 GET Tmdb/ClientConfiguration authorized"; an empty route shows as "(none)".</summary>
    private static string CompactEntry(EndpointEntry endpoint) =>
        $"{endpoint.Line} {endpoint.Method} {(endpoint.Route.Length > 0 ? endpoint.Route : "(none)")} {endpoint.Auth}";

    /// <summary>
    /// Syntax-only endpoint extraction for one file. <paramref name="controllers"/> resolves
    /// controller base classes declared elsewhere; without it only this file's classes are known.
    /// Internal for direct unit testing.
    /// </summary>
    internal static List<EndpointEntry> AnalyzeRoot(SyntaxNode root, string filePath, ControllerIndex? controllers = null)
    {
        var results = new List<EndpointEntry>();

        AnalyzeMinimalApis(root, filePath, results);
        AnalyzeControllers(root, filePath, controllers ?? ControllerIndex.For(root), results);

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

    private static void AnalyzeControllers(
        SyntaxNode root, string filePath, ControllerIndex controllers, List<EndpointEntry> results)
    {
        foreach (var cls in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            var className = cls.Identifier.Text;

            if (!controllers.IsController(className))
                continue;

            var controllerName = className.EndsWith("Controller", StringComparison.Ordinal)
                ? className[..^"Controller".Length]
                : className;

            // [Route] and [Authorize] are inherited: the nearest one on the class or a base applies.
            var classRoute = ReplaceTokens(controllers.Route(className) ?? "", controllerName, action: null);
            var classAuth = controllers.Auth(className);

            foreach (var method in cls.Members.OfType<MethodDeclarationSyntax>())
            {
                var auth = HasAttribute(method.AttributeLists, "AllowAnonymous") ? "anonymous"
                    : HasAttribute(method.AttributeLists, "Authorize") ? "authorized"
                    : classAuth ?? "unmarked";
                var line = method.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

                // Each Http* attribute is its own endpoint: [HttpGet("a")] [HttpHead("a")] is two.
                foreach (var (verb, template) in FindHttpAttributes(method.AttributeLists))
                {
                    var methodRoute = ReplaceTokens(
                        GetAttributeStringArgument(method.AttributeLists, "Route") ?? template ?? "",
                        controllerName,
                        method.Identifier.Text);

                    // A template starting with "/" or "~/" is absolute and ignores the controller's route.
                    var route = methodRoute.StartsWith('/') || methodRoute.StartsWith("~/", StringComparison.Ordinal)
                        ? methodRoute.TrimStart('~')
                        : CombineRoutes(classRoute, methodRoute);

                    results.Add(new EndpointEntry(
                        Method: verb,
                        Route: route,
                        Auth: auth,
                        Kind: "controller",
                        File: filePath,
                        Line: line));
                }
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

    /// <summary>Fills the [controller] and [action] route tokens.</summary>
    private static string ReplaceTokens(string template, string controller, string? action)
    {
        var route = template.Replace("[controller]", controller, StringComparison.OrdinalIgnoreCase);
        return action is null ? route : route.Replace("[action]", action, StringComparison.OrdinalIgnoreCase);
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

    internal static bool HasAttribute(SyntaxList<AttributeListSyntax> lists, string name) =>
        lists.SelectMany(l => l.Attributes)
            .Any(a => MatchesAttributeName(a, name));

    internal static string? GetAttributeStringArgument(SyntaxList<AttributeListSyntax> lists, string name)
    {
        var attribute = lists.SelectMany(l => l.Attributes)
            .FirstOrDefault(a => MatchesAttributeName(a, name));

        var arg = attribute?.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
        return arg is LiteralExpressionSyntax { Token.Value: string value } ? value : null;
    }

    private static IEnumerable<(string Verb, string? Template)> FindHttpAttributes(SyntaxList<AttributeListSyntax> lists)
    {
        foreach (var attribute in lists.SelectMany(l => l.Attributes))
        {
            var name = GetSimpleAttributeName(attribute);
            if (HttpAttributeVerbs.TryGetValue(name, out var verb))
            {
                var arg = attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
                var template = arg is LiteralExpressionSyntax { Token.Value: string value } ? value : null;
                yield return (verb, template);
            }
        }
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
