using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CWM.RoslynNavigator;

/// <summary>
/// Removes <see cref="UnresolvedAnalyzerReference"/> instances from a freshly loaded solution.
///
/// <para>MSBuildWorkspace creates one for every analyzer or generator path that does not exist
/// on disk — typically an in-solution analyzer project (<c>OutputItemType="Analyzer"</c>) on a
/// fresh clone that was restored but never built. Such a reference contributes nothing to a
/// compilation, but Roslyn's serializer cannot checksum it, so every search that builds the
/// derived-type index (<c>FindImplementationsAsync</c>, <c>FindDerivedClassesAsync</c>, and
/// anything else that checksums the project) threw <c>InvalidOperationException: Unexpected
/// value 'UnresolvedAnalyzerReference'</c>. Dropping the reference changes no compilation
/// result and makes those searches work; the dropped paths are recorded in
/// <see cref="WorkspaceHealth"/> so get_workspace_status still reports them.</para>
/// </summary>
internal static class UnresolvedAnalyzerReferences
{
    /// <summary>
    /// Returns <paramref name="solution"/> without unresolved analyzer references, recording
    /// each removed one against its project in <paramref name="health"/>. Returns the same
    /// instance when there is nothing to remove.
    /// </summary>
    public static Solution Strip(Solution solution, WorkspaceHealth health)
    {
        var result = solution;
        foreach (var project in solution.Projects)
        {
            var references = project.AnalyzerReferences;
            if (!references.Any(r => r is UnresolvedAnalyzerReference)) continue;

            List<AnalyzerReference> kept = [];
            List<string> removed = [];
            foreach (var reference in references)
            {
                if (reference is UnresolvedAnalyzerReference unresolved)
                    removed.Add(unresolved.Display);
                else
                    kept.Add(reference);
            }

            result = result.WithProjectAnalyzerReferences(project.Id, kept);
            health.RecordRemovedAnalyzers(project.Id, removed);
        }

        return result;
    }
}
