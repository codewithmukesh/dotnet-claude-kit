namespace CWM.RoslynNavigator.Bench.Server;

/// <summary>
/// Finds the <c>dotnet</c> host as an absolute path. Passing a bare "dotnet" would let the
/// stdio transport wrap it in a shell on Windows, and then the process the harness measures
/// would be the shell, not the server.
/// </summary>
internal static class DotnetHost
{
    public static string Resolve()
    {
        var fromSdk = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrEmpty(fromSdk) && File.Exists(fromSdk)) return fromSdk;

        var executable = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        var searchPath = Environment.GetEnvironmentVariable("PATH") ?? "";

        foreach (var directory in searchPath.Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = System.IO.Path.Combine(directory.Trim(), executable);
            if (File.Exists(candidate)) return candidate;
        }

        throw new InvalidOperationException("Could not find the dotnet host on PATH. Set DOTNET_HOST_PATH.");
    }
}
