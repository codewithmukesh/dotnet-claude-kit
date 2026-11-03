using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CWM.RoslynNavigator.Analyzers;

/// <summary>
/// AP010: Detects EF Core read queries (via DbSet access) that materialize entities without
/// AsNoTracking(). Tracked queries allocate change-tracking state that is unnecessary for
/// read-only scenarios.
///
/// Only <em>materializing</em> terminals count. Aggregates (Count, Any, Sum, Min, Max,
/// Average) never materialize entities, so change tracking is not involved and AsNoTracking()
/// would change nothing — flagging them is always a false positive.
///
/// Queries in a method that also calls SaveChanges are load-to-mutate: tracking is exactly
/// what makes them work, so they are skipped. Queries with Include() are reported at Medium
/// confidence because loading a graph is a common prelude to mutation.
/// </summary>
internal sealed class EfCoreNoTrackingDetector : IAntiPatternDetector
{
    private static readonly HashSet<string> MutationMethods = new(StringComparer.Ordinal)
    {
        "Add", "AddAsync", "AddRange", "AddRangeAsync",
        "Update", "UpdateRange",
        "Remove", "RemoveRange",
        "Attach", "AttachRange"
    };

    /// <summary>
    /// Terminals that materialize entity instances into the change tracker. Deliberately
    /// excludes aggregates, which return scalars and never populate the tracker.
    /// </summary>
    private static readonly HashSet<string> MaterializingTerminals = new(StringComparer.Ordinal)
    {
        "ToListAsync", "ToArrayAsync", "ToDictionaryAsync",
        "FirstAsync", "FirstOrDefaultAsync",
        "SingleAsync", "SingleOrDefaultAsync",
        "LastAsync", "LastOrDefaultAsync",
        "ToList", "ToArray", "ToDictionary",
        "First", "FirstOrDefault",
        "Single", "SingleOrDefault",
        "Last", "LastOrDefault"
    };

    /// <summary>
    /// Context- and transaction-level operations that mark a scope as a write. Raw-SQL
    /// composition is included because <c>SELECT ... FOR UPDATE</c> row locks are loads taken
    /// specifically in order to mutate.
    /// </summary>
    private static readonly string[] ContextMutationMethods =
    [
        "SaveChanges", "SaveChangesAsync",
        "ExecuteUpdate", "ExecuteUpdateAsync",
        "ExecuteDelete", "ExecuteDeleteAsync",
        "BeginTransaction", "BeginTransactionAsync",
        "FromSql", "FromSqlRaw", "FromSqlInterpolated"
    ];

    /// <summary>
    /// Method-name prefixes that mark a read. Only queries inside these earn High confidence.
    /// </summary>
    /// Deliberately excludes ambiguous names like Handle or Process — a VSA command handler
    /// reads an entity in order to change it.
    private static readonly string[] ReadMethodPrefixes =
    [
        "Get", "List", "Find", "Query", "Search", "Fetch", "Read", "Lookup", "Exists"
    ];

    public bool RequiresSemanticModel => true;

    public SourceKind AppliesTo => SourceKind.Production;

    public IEnumerable<AntiPatternViolation> Detect(DetectionContext context)
    {
        var model = context.Model;
        if (model is null || !CanSeeEntityQueries(model.Compilation))
            yield break;

        foreach (var invocation in context.Root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            context.Ct.ThrowIfCancellationRequested();

            if (invocation.Expression is not MemberAccessExpressionSyntax terminalAccess)
                continue;

            var terminalMethod = terminalAccess.Name.Identifier.Text;
            if (!MaterializingTerminals.Contains(terminalMethod))
                continue;

            // Walk the invocation chain to collect all method names
            var chainMethods = CollectChainMethods(invocation);

            // Skip if chain already has AsNoTracking
            if (chainMethods.Any(m => m is "AsNoTracking" or "AsNoTrackingWithIdentityResolution"))
                continue;

            // Skip if chain has projection (Select) — projections are not tracked
            if (chainMethods.Contains("Select"))
                continue;

            // Skip if chain has mutation methods
            if (chainMethods.Any(MutationMethods.Contains))
                continue;

            // Check if chain starts from a DbSet<T> property. Checked before load-to-mutate:
            // both bind, but this one binds a single expression and rejects every
            // LINQ-to-objects chain, which is most ToList/First calls in a codebase.
            var chainRoot = GetChainRoot(invocation);
            if (chainRoot is null)
                continue;

            var typeInfo = model.GetTypeInfo(chainRoot, context.Ct);

            // The chain must start from an entity query. DatabaseFacade (db.Database.SqlQuery)
            // and ChangeTracker (context.ChangeTracker.Entries) also live in the EF Core
            // assembly but return scalars and tracker entries, which AsNoTracking cannot affect.
            if (!IsEntityQuerySource(typeInfo.Type))
                continue;

            // Load-to-mutate: tracking is required for SaveChanges to detect the edit.
            if (IsLoadToMutate(invocation, model, context.Ct))
                continue;

            var line = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            var snippet = invocation.ToString();
            if (snippet.Length > 80) snippet = snippet[..77] + "...";

            // Whether an entity is mutated is ultimately an interprocedural question — the edit
            // often happens in the caller. High confidence is reserved for reads that are
            // unambiguous from the signature alone: a read-shaped method name and no graph load.
            var loadsGraph = chainMethods.Any(m => m is "Include" or "ThenInclude");
            var confidence = !loadsGraph && IsReadShapedMethod(invocation)
                ? AntiPatternConfidence.High
                : AntiPatternConfidence.Medium;

            yield return new AntiPatternViolation(
                Id: "AP010",
                Severity: AntiPatternSeverity.Warning,
                Message: "EF Core read query without AsNoTracking() allocates unnecessary change-tracking state",
                File: context.FilePath,
                Line: line,
                Snippet: snippet,
                Suggestion: "Add .AsNoTracking() before the terminal method for read-only queries",
                Confidence: confidence,
                Member: AnalyzerHelpers.EnclosingMember(invocation));
        }
    }

    /// <summary>
    /// Whether the loaded entities are edited rather than merely read. Two signals: the scope
    /// persists changes, or the query result has a property assigned to it. The second matters
    /// because the SaveChanges call often lives in a pipeline behaviour or interceptor rather
    /// than in the handler that loads the entity.
    /// </summary>
    private static bool IsLoadToMutate(
        InvocationExpressionSyntax query,
        SemanticModel model,
        CancellationToken ct)
    {
        var body = AnalyzerHelpers.EnclosingBody(query);
        if (body is null)
            return false;

        if (AnalyzerHelpers.ContainsInvocationOf(body, ContextMutationMethods))
            return true;

        if (ContainsDbSetMutation(body, model, ct))
            return true;

        var resultVariable = GetResultVariableName(query);
        return resultVariable is not null && IsMutatedInScope(body, resultVariable);
    }

    /// <summary>
    /// Whether the scope calls a mutating method directly on a <c>DbSet&lt;T&gt;</c>. Resolved
    /// semantically so that an ordinary <c>list.Add(...)</c> is not mistaken for
    /// <c>db.Orders.Add(...)</c>.
    /// </summary>
    private static bool ContainsDbSetMutation(SyntaxNode scope, SemanticModel model, CancellationToken ct)
    {
        foreach (var invocation in scope.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                continue;

            if (!MutationMethods.Contains(memberAccess.Name.Identifier.Text))
                continue;

            if (IsEntityQuerySource(model.GetTypeInfo(memberAccess.Expression, ct).Type))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the enclosing method reads as a query rather than a command. Only these earn
    /// High confidence, because for anything else the loaded entity may be edited downstream.
    /// </summary>
    private static bool IsReadShapedMethod(SyntaxNode query)
    {
        var method = query.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (method is null)
            return false;

        var name = method.Identifier.Text;

        foreach (var prefix in ReadMethodPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The local variable a query result is assigned to, unwrapping <c>await</c>.
    /// </summary>
    private static string? GetResultVariableName(InvocationExpressionSyntax query)
    {
        SyntaxNode? current = query;

        // Step past await and parentheses to reach the declaration.
        while (current?.Parent is AwaitExpressionSyntax or ParenthesizedExpressionSyntax)
            current = current.Parent;

        return current?.Parent switch
        {
            EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator } =>
                declarator.Identifier.Text,
            AssignmentExpressionSyntax { Left: IdentifierNameSyntax identifier } =>
                identifier.Identifier.Text,
            _ => null
        };
    }

    /// <summary>
    /// Whether any property of <paramref name="variableName"/> is assigned within the scope —
    /// for example <c>tenant.TrialEndsAt = null</c>.
    /// </summary>
    private static bool IsMutatedInScope(SyntaxNode scope, string variableName)
    {
        foreach (var assignment in scope.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.Left is not MemberAccessExpressionSyntax memberAccess)
                continue;

            var target = memberAccess.Expression;

            // Unwrap null-conditional and nested access to reach the root identifier.
            while (target is MemberAccessExpressionSyntax nested)
                target = nested.Expression;

            if (target is IdentifierNameSyntax identifier
                && identifier.Identifier.Text == variableName)
                return true;
        }

        return false;
    }

    private static List<string> CollectChainMethods(ExpressionSyntax expression)
    {
        var methods = new List<string>();
        var current = expression;

        while (current is InvocationExpressionSyntax inv)
        {
            if (inv.Expression is MemberAccessExpressionSyntax access)
            {
                methods.Add(access.Name.Identifier.Text);
                current = access.Expression;
            }
            else
            {
                break;
            }
        }

        return methods;
    }

    private static ExpressionSyntax? GetChainRoot(ExpressionSyntax expression)
    {
        var current = expression;

        while (current is InvocationExpressionSyntax inv)
        {
            if (inv.Expression is MemberAccessExpressionSyntax access)
                current = access.Expression;
            else
                break;
        }

        return current;
    }

    /// <summary>
    /// Whether a chain root is a <c>DbSet&lt;T&gt;</c> — a query directly over tracked entities.
    ///
    /// Deliberately narrow on both sides. It excludes other EF Core types (DatabaseFacade for
    /// <c>db.Database.SqlQuery</c>, ChangeTracker for <c>context.ChangeTracker.Entries</c>)
    /// which return scalars and tracker entries that AsNoTracking cannot affect. It also
    /// excludes bare <c>IQueryable&lt;T&gt;</c> locals, because a composed query may already
    /// have had AsNoTracking applied further upstream where this chain cannot see it.
    /// </summary>
    private static bool IsEntityQuerySource(ITypeSymbol? type) =>
        type?.Name is "DbSet" or "DbQuery";

    private static readonly ConditionalWeakTable<Compilation, StrongBox<bool>> EntityQueryVisibility = new();

    /// <summary>
    /// Whether any DbSet/DbQuery type is visible to the compilation — declared in source or in
    /// a referenced EF Core / EF6 assembly. A project that cannot see one cannot produce a
    /// finding, so its trees skip the binding entirely; most projects in a solution are not
    /// the data layer.
    /// </summary>
    private static bool CanSeeEntityQueries(Compilation compilation) =>
        EntityQueryVisibility.GetValue(compilation, static c => new StrongBox<bool>(
            c.ContainsSymbolsWithName(name => name is "DbSet" or "DbQuery", SymbolFilter.Type)
            || c.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbSet`1") is not null
            || c.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbQuery`1") is not null
            || c.GetTypeByMetadataName("System.Data.Entity.DbSet`1") is not null
            || c.GetTypeByMetadataName("System.Data.Entity.Infrastructure.DbQuery`1") is not null)).Value;
}
