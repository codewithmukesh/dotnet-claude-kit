using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;

namespace CWM.RoslynNavigator.Tests.Tools;

/// <summary>
/// Searches seeded with a type from a multi-targeted library must find types in consumers of
/// every flavor, not only those compiled against the flavor the name happened to resolve to.
/// </summary>
public sealed class CrossFlavorSearchTests(CrossFlavorSolutionFixture fixture) : IClassFixture<CrossFlavorSolutionFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WorkspaceManager Workspace => fixture.WorkspaceManager;

    [Fact]
    public void Load_LibraryConsumedByTwoFrameworks_LoadsBothFlavors()
    {
        // Assert — the premise: Lib(net8.0) and Lib(net10.0) plus the two consumers.
        Assert.Equal(4, Workspace.ProjectCount);
    }

    [Fact]
    public async Task GetTypeHierarchy_BaseInMultiTargetedLibrary_FindsDerivedTypesOfEveryFlavor()
    {
        // Act
        var json = await GetTypeHierarchyTool.ExecuteAsync(Workspace, "ModuleBase", ct: Ct);

        // Assert
        var result = JsonSerializer.Deserialize<TypeHierarchyResult>(json)!;
        Assert.Equal(["CoreModule", "LegacyModule", "ModernModule"], result.DerivedTypes);
        Assert.Equal(3, result.TotalDerived);
    }

    [Fact]
    public async Task FindImplementations_InterfaceInMultiTargetedLibrary_FindsImplementationsOfEveryFlavor()
    {
        // Act
        var json = await FindImplementationsTool.ExecuteAsync(Workspace, "IModule", ct: Ct);

        // Assert
        var result = JsonSerializer.Deserialize<ImplementationsResult>(json)!;
        Assert.Equal(["CoreModule", "LegacyModule", "ModernModule", "ModuleBase"], result.Implementations.Select(i => i.Type).Order());
    }

    [Fact]
    public async Task FindOverrides_VirtualInMultiTargetedLibrary_FindsOverridesOfEveryFlavor()
    {
        // Act
        var json = await FindOverridesTool.ExecuteAsync(Workspace, "Configure", "ModuleBase", ct: Ct);

        // Assert
        var result = JsonSerializer.Deserialize<OverridesResult>(json)!;
        Assert.Equal(["LegacyModule", "ModernModule"], result.Overrides.Select(o => o.ContainingType).Order());
    }

    [Fact]
    public async Task FindCallers_MethodInMultiTargetedLibrary_FindsCallersOfEveryFlavor()
    {
        // Act
        var json = await FindCallersTool.ExecuteAsync(Workspace, "ModuleBase.Configure", ct: Ct);

        // Assert
        var result = JsonSerializer.Deserialize<CallersResult>(json)!;
        Assert.Equal(["LegacyModule", "ModernModule"], result.Callers.Where(c => c.Method == "Run").Select(c => c.ContainingType).Order());
    }

    [Fact]
    public async Task FindReferences_TypeInMultiTargetedLibrary_FindsUsagesInEveryConsumer()
    {
        // Act
        var json = await FindReferencesTool.ExecuteAsync(Workspace, "ModuleBase", format: "full", ct: Ct);

        // Assert
        var files = JsonSerializer.Deserialize<ReferencesResult>(json)!.References!.Select(r => r.File).ToList();
        Assert.Contains(files, f => f.EndsWith("LegacyModule.cs", StringComparison.Ordinal));
        Assert.Contains(files, f => f.EndsWith("ModernModule.cs", StringComparison.Ordinal));
    }
}
