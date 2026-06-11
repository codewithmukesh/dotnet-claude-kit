using System.Diagnostics;
using Microsoft.Build.Locator;

namespace CWM.RoslynNavigator;

/// <summary>
/// Registers MSBuild assemblies for MSBuildWorkspace. Falls back to asking the dotnet CLI
/// for SDK locations when <see cref="MSBuildLocator.RegisterDefaults"/> fails — which happens
/// on macOS/Linux when the `dotnet` on PATH is a wrapper script (e.g. Homebrew) and
/// DOTNET_ROOT is not set, so hostfxr cannot resolve the SDK layout.
/// </summary>
public static class MSBuildRegistration
{
    /// <summary>
    /// Must be called before any Roslyn/MSBuild types are loaded.
    /// </summary>
    public static void Register()
    {
        try
        {
            MSBuildLocator.RegisterDefaults();
        }
        catch (InvalidOperationException)
        {
            var sdkPath = ResolveSdkPathViaDotnetCli();
            if (sdkPath is null)
            {
                Console.Error.WriteLine(
                    "Failed to locate the .NET SDK. Set DOTNET_ROOT to your .NET installation root " +
                    "(the directory containing 'sdk/' and 'host/'), e.g. /usr/local/share/dotnet. " +
                    "See https://github.com/codewithmukesh/dotnet-claude-kit/issues/9 for details.");
                throw;
            }

            Console.Error.WriteLine(
                $"MSBuildLocator could not resolve the SDK from PATH/DOTNET_ROOT; " +
                $"using SDK at '{sdkPath}' (resolved via 'dotnet --list-sdks').");
            MSBuildLocator.RegisterMSBuildPath(sdkPath);
        }
    }

    /// <summary>
    /// Asks the dotnet CLI where its SDKs live. The CLI always knows its own root,
    /// even when hostfxr cannot resolve it from the PATH entry.
    /// </summary>
    private static string? ResolveSdkPathViaDotnetCli()
    {
        string output;
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "--list-sdks",
                RedirectStandardOutput = true,
                UseShellExecute = false,
            });
            if (process is null)
                return null;

            output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                return null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }

        var sdkPath = PickBestSdkPath(output);
        return sdkPath is not null && Directory.Exists(sdkPath) ? sdkPath : null;
    }

    /// <summary>
    /// Parses `dotnet --list-sdks` output ("10.0.201 [/usr/local/share/dotnet/sdk]") and
    /// returns the full path of the newest SDK whose major version does not exceed the
    /// running runtime — MSBuild assemblies from a newer SDK cannot load on an older runtime.
    /// </summary>
    public static string? PickBestSdkPath(string listSdksOutput, int? maxMajorVersion = null)
    {
        var runtimeMajor = maxMajorVersion ?? Environment.Version.Major;
        Version? bestVersion = null;
        string? bestPath = null;

        foreach (var line in listSdksOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bracketStart = line.IndexOf('[');
            var bracketEnd = line.LastIndexOf(']');
            if (bracketStart < 0 || bracketEnd <= bracketStart)
                continue;

            // Directory names keep the full version string ("10.0.100-rc.1"),
            // but prerelease suffixes must be stripped for Version.TryParse.
            var rawVersion = line[..bracketStart].Trim();
            var dash = rawVersion.IndexOf('-');
            var comparableVersion = dash >= 0 ? rawVersion[..dash] : rawVersion;
            if (!Version.TryParse(comparableVersion, out var version))
                continue;

            if (version.Major > runtimeMajor)
                continue;

            if (bestVersion is null || version > bestVersion)
            {
                bestVersion = version;
                bestPath = Path.Combine(line[(bracketStart + 1)..bracketEnd], rawVersion);
            }
        }

        return bestPath;
    }
}
