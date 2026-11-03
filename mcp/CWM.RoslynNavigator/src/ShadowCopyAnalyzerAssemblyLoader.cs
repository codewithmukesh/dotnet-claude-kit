using System.Reflection;
using Microsoft.CodeAnalysis;

namespace CWM.RoslynNavigator;

/// <summary>
/// Wraps Roslyn's default analyzer loader so every analyzer and generator assembly is loaded
/// from a shadow copy instead of its original location. <see cref="AnalyzerReference.FullPath"/>
/// keeps reporting the original path; only the bytes Roslyn maps come from the copy.
/// </summary>
/// <remarks>
/// Roslyn's own loader still does all the hard work — one load context per directory,
/// compiler-assembly unification, satellite resolution — it is just handed shadow paths.
/// Because a directory is always copied whole, sibling dependencies resolve from the same
/// shadow directory exactly as they would have from the original.
/// </remarks>
internal sealed class ShadowCopyAnalyzerAssemblyLoader(IAnalyzerAssemblyLoader inner, AnalyzerShadowCopier copier)
    : IAnalyzerAssemblyLoader
{
    public void AddDependencyLocation(string fullPath) =>
        inner.AddDependencyLocation(copier.GetShadowPath(fullPath));

    public Assembly LoadFromPath(string fullPath)
    {
        var shadowPath = copier.GetShadowPath(fullPath);

        // Registration is idempotent. Repeating it covers the case where the original changed
        // on disk after the reference was created, which yields a new, not-yet-registered copy.
        inner.AddDependencyLocation(shadowPath);
        return inner.LoadFromPath(shadowPath);
    }
}
