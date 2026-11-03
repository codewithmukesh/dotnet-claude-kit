using System.Composition;
using System.Composition.Hosting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;

namespace CWM.RoslynNavigator;

/// <summary>
/// Replaces Roslyn's default <see cref="IAnalyzerService"/> so MSBuildWorkspace builds every
/// <c>AnalyzerFileReference</c> on a shadow-copying loader. MSBuildWorkspace asks this service
/// for its loader when it resolves a project's analyzers, so plugging in here covers the
/// initial load and every reload without touching the references afterwards.
/// </summary>
/// <remarks>
/// Exported at <see cref="ServiceLayer.Host"/>, which outranks the default implementation's
/// <see cref="ServiceLayer.Default"/> export. Fixes codewithmukesh/dotnet-claude-kit#25.
/// </remarks>
[ExportWorkspaceService(typeof(IAnalyzerService), ServiceLayer.Host)]
[Shared]
[method: ImportingConstructor]
internal sealed class ShadowCopyAnalyzerService() : IAnalyzerService
{
    private static readonly Lazy<IAnalyzerAssemblyLoader> s_loader = new(CreateLoader);

    private static readonly Lazy<HostServices> s_hostServices = new(() =>
        MefHostServices.Create(new ContainerConfiguration()
            .WithAssemblies(MSBuildMefHostServices.DefaultAssemblies)
            .WithPart<ShadowCopyAnalyzerService>()
            .CreateContainer()));

    /// <summary>
    /// Roslyn's default MSBuild host services plus this service. Pass to
    /// <c>MSBuildWorkspace.Create</c>. Built once: MEF composition is not free.
    /// </summary>
    public static HostServices HostServices => s_hostServices.Value;

    public IAnalyzerAssemblyLoader GetLoader() => s_loader.Value;

    private static IAnalyzerAssemblyLoader CreateLoader()
    {
        // Roslyn's own loader type is internal, but the default host hands it out through this
        // same public service. Borrowing it keeps load-context and dependency handling identical
        // to stock MSBuildWorkspace; only the paths it is given change.
        using var defaultWorkspace = new AdhocWorkspace();
        var defaultLoader = defaultWorkspace.Services.GetRequiredService<IAnalyzerService>().GetLoader();
        return new ShadowCopyAnalyzerAssemblyLoader(defaultLoader, AnalyzerShadowCopier.CurrentProcess);
    }
}
