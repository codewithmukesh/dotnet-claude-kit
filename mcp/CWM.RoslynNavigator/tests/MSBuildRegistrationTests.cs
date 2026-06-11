namespace CWM.RoslynNavigator.Tests;

/// <summary>
/// Pure parsing tests for <see cref="MSBuildRegistration.PickBestSdkPath"/>.
/// No filesystem or process interaction — output strings only.
/// </summary>
public class MSBuildRegistrationTests
{
    [Fact]
    public void PickBestSdkPath_SingleSdk_ReturnsVersionedPath()
    {
        var output = "10.0.201 [/usr/local/share/dotnet/sdk]\n";

        var result = MSBuildRegistration.PickBestSdkPath(output, maxMajorVersion: 10);

        Assert.Equal(Path.Combine("/usr/local/share/dotnet/sdk", "10.0.201"), result);
    }

    [Fact]
    public void PickBestSdkPath_MultipleSdks_ReturnsNewest()
    {
        var output = """
            8.0.404 [/usr/local/share/dotnet/sdk]
            9.0.102 [/usr/local/share/dotnet/sdk]
            10.0.201 [/usr/local/share/dotnet/sdk]
            """;

        var result = MSBuildRegistration.PickBestSdkPath(output, maxMajorVersion: 10);

        Assert.Equal(Path.Combine("/usr/local/share/dotnet/sdk", "10.0.201"), result);
    }

    [Fact]
    public void PickBestSdkPath_SdkNewerThanRuntime_IsSkipped()
    {
        var output = """
            10.0.201 [/usr/local/share/dotnet/sdk]
            11.0.100 [/usr/local/share/dotnet/sdk]
            """;

        var result = MSBuildRegistration.PickBestSdkPath(output, maxMajorVersion: 10);

        Assert.Equal(Path.Combine("/usr/local/share/dotnet/sdk", "10.0.201"), result);
    }

    [Fact]
    public void PickBestSdkPath_PrereleaseVersion_KeepsFullVersionInPath()
    {
        var output = "10.0.100-rc.1.24452.12 [/usr/local/share/dotnet/sdk]\n";

        var result = MSBuildRegistration.PickBestSdkPath(output, maxMajorVersion: 10);

        Assert.Equal(Path.Combine("/usr/local/share/dotnet/sdk", "10.0.100-rc.1.24452.12"), result);
    }

    [Fact]
    public void PickBestSdkPath_PathWithSpaces_ParsesCorrectly()
    {
        var output = "10.0.201 [C:\\Program Files\\dotnet\\sdk]\n";

        var result = MSBuildRegistration.PickBestSdkPath(output, maxMajorVersion: 10);

        Assert.Equal(Path.Combine("C:\\Program Files\\dotnet\\sdk", "10.0.201"), result);
    }

    [Fact]
    public void PickBestSdkPath_EmptyOutput_ReturnsNull()
    {
        var result = MSBuildRegistration.PickBestSdkPath("", maxMajorVersion: 10);

        Assert.Null(result);
    }

    [Fact]
    public void PickBestSdkPath_MalformedLines_AreIgnored()
    {
        var output = """
            not an sdk line
            10.0.201 missing brackets
            10.0.201 [/usr/local/share/dotnet/sdk]
            """;

        var result = MSBuildRegistration.PickBestSdkPath(output, maxMajorVersion: 10);

        Assert.Equal(Path.Combine("/usr/local/share/dotnet/sdk", "10.0.201"), result);
    }
}
