using System.Text.Json;
using System.Text.Json.Nodes;
using CWM.RoslynNavigator.Responses;
using ModelContextProtocol.Protocol;

namespace CWM.RoslynNavigator;

/// <summary>
/// Adds a <c>WorkspaceWarning</c> property to an empty tool result when the workspace loaded
/// incompletely, so "nothing found" is never mistaken for "nothing exists" while a project is
/// missing documents or references.
///
/// <para>Applied once, in the server's call-tool filter, rather than in each tool: no tool's
/// response record changes, a tool added later is covered automatically, and the cost on a
/// healthy workspace is one cached list check. A result counts as empty when it reports
/// <c>TotalFound</c> (or, failing that, <c>Count</c>) of zero, or is a SymbolNotFound /
/// FileNotFound error. Not-ready responses and other errors are left alone.</para>
/// </summary>
internal static class IncompleteWorkspaceNote
{
    public const string PropertyName = "WorkspaceWarning";

    private const int MaxNamedProjects = 5;

    /// <summary>The warning to attach, or null when the workspace loaded completely.</summary>
    public static string? BuildWarning(WorkspaceManager workspace)
    {
        if (workspace.State != WorkspaceState.Ready) return null;

        var problems = workspace.GetProjectProblems();
        var unattributedFailures = problems.Count == 0
            ? workspace.Health.GetDiagnostics(workspace.GetSolution())
                .Count(d => d.Kind == "failure" && d.ProjectPath is null)
            : 0;

        if (problems.Count == 0 && unattributedFailures == 0) return null;

        if (problems.Count == 0)
            return $"The workspace reported {unattributedFailures} load failure(s); this empty result may be incomplete. Call get_workspace_status for details.";

        var names = string.Join(", ", problems.Select(p => p.Name).Distinct().Take(MaxNamedProjects));
        var more = problems.Count > MaxNamedProjects ? ", ..." : "";
        return $"{problems.Count} project(s) loaded incompletely or not at all ({names}{more}); this empty result may be missing matches from them. Call get_workspace_status for details.";
    }

    /// <summary>
    /// Applies the warnings to the text payload of a tool result: the incomplete-workspace
    /// warning when the result is empty, and the staleness warning (a reload in flight or a
    /// failed one, see <see cref="WorkspaceManager.StalenessWarning"/>) to every result.
    /// </summary>
    public static CallToolResult Apply(CallToolResult result, WorkspaceManager workspace)
    {
        if (result.IsError == true || result.Content is not { Count: > 0 } content) return result;
        if (content[0] is not TextContentBlock text) return result;

        var stale = workspace.StalenessWarning;
        var incomplete = BuildWarning(workspace);
        if (stale is null && incomplete is null) return result;

        var annotated = Annotate(text.Text, incomplete, stale);
        if (ReferenceEquals(annotated, text.Text)) return result;

        content[0] = new TextContentBlock { Text = annotated };
        return result;
    }

    /// <summary>
    /// Returns <paramref name="json"/> with the warning added when it is an empty result,
    /// otherwise the same string instance.
    /// </summary>
    public static string Annotate(string json, string warning) => Annotate(json, warning, null);

    /// <summary>
    /// Returns <paramref name="json"/> with <paramref name="emptyWarning"/> added when it is an
    /// empty result and <paramref name="anyWarning"/> added regardless, joined into the one
    /// <c>WorkspaceWarning</c> property; otherwise the same string instance. Not-ready status
    /// responses and already-annotated results are never changed.
    /// </summary>
    public static string Annotate(string json, string? emptyWarning, string? anyWarning)
    {
        JsonObject? root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return json;
        }

        if (root is null
            || root.ContainsKey(nameof(StatusResponse.State))
            || root.ContainsKey(PropertyName))
            return json;

        List<string> warnings = [];
        if (anyWarning is not null) warnings.Add(anyWarning);
        if (emptyWarning is not null && IsEmptyResult(root)) warnings.Add(emptyWarning);
        if (warnings.Count == 0) return json;

        root[PropertyName] = string.Join(" ", warnings);
        return root.ToJsonString();
    }

    private static bool IsEmptyResult(JsonObject root)
    {
        if (root.TryGetPropertyValue(nameof(ErrorResponse.Error), out var error))
        {
            var code = error?.GetValueKind() == JsonValueKind.String ? error.GetValue<string>() : null;
            return code is ErrorCodes.SymbolNotFound or ErrorCodes.FileNotFound;
        }

        return IsZero(root, "TotalFound") ?? IsZero(root, "Count") ?? false;
    }

    private static bool? IsZero(JsonObject root, string property) =>
        root.TryGetPropertyValue(property, out var node) && node?.GetValueKind() == JsonValueKind.Number
            ? node.GetValue<int>() == 0
            : null;
}
