using CWM.RoslynNavigator.Caching;
using CWM.RoslynNavigator.Tests.Fixtures;

namespace CWM.RoslynNavigator.Tests.Caching;

/// <summary>Where the cache lives and how it is switched off.</summary>
public sealed class LoadCacheOptionsTests
{
    [Fact]
    public void Apply_NoLoadCacheFlag_DisablesCache()
    {
        // Arrange
        var workspace = TestWorkspace.Create(TimeProvider.System);

        // Act
        WorkspaceOptions.Apply(["--solution", "x.sln", WorkspaceOptions.NoLoadCacheFlag], workspace, _ => null);

        // Assert
        Assert.Null(workspace.LoadCache);
        workspace.Dispose();
    }

    [Fact]
    public void Apply_CacheDirectoryVariable_UsesThatFolder()
    {
        // Arrange
        var workspace = TestWorkspace.Create(TimeProvider.System);
        var folder = Path.Combine(Path.GetTempPath(), "cwm-rn-cache-dir");

        // Act
        WorkspaceOptions.Apply(["--solution", "x.sln"], workspace,
            name => name == LoadCacheStore.DirectoryEnvironmentVariable ? folder : null);

        // Assert
        Assert.Equal(folder, workspace.LoadCache!.RootDirectory);
        workspace.Dispose();
    }

    [Fact]
    public void FromEnvironment_NoOverride_UsesTheUserCacheFolder()
    {
        // Act
        var store = LoadCacheStore.FromEnvironment(_ => null);

        // Assert
        Assert.NotNull(store);
        Assert.Contains("cwm-roslyn-navigator", store.RootDirectory);
        if (OperatingSystem.IsWindows())
        {
            Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), store.RootDirectory);
        }
    }

    [Fact]
    public void FromEnvironment_XdgCacheHomeOnLinux_IsHonoured()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "XDG_CACHE_HOME applies on Linux only.");

        // Act
        var store = LoadCacheStore.FromEnvironment(name => name == "XDG_CACHE_HOME" ? "/tmp/xdg-cache" : null);

        // Assert
        Assert.Equal("/tmp/xdg-cache/cwm-roslyn-navigator", store!.RootDirectory);
    }
}
