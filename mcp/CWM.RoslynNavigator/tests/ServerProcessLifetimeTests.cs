using System.Diagnostics;

namespace CWM.RoslynNavigator.Tests;

/// <summary>
/// A stdio MCP server must not outlive its client. When the client exits (or crashes) the OS
/// closes its end of the pipe, so stdin EOF is the signal; a server that ignores it lingers and
/// keeps holding file handles (#25 observed nine navigator processes alive at once).
/// </summary>
public sealed class ServerProcessLifetimeTests
{
    [Fact]
    public async Task Server_StdinClosed_ExitsPromptly()
    {
        var serverDll = Path.Combine(AppContext.BaseDirectory, "CWM.RoslynNavigator.dll");
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var startInfo = new ProcessStartInfo(dotnet)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(serverDll);
        startInfo.ArgumentList.Add("--solution");
        startInfo.ArgumentList.Add(Path.Combine(Path.GetTempPath(), "does-not-exist.sln"));

        using var server = Process.Start(startInfo)!;
        _ = server.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        _ = server.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);

        server.StandardInput.Close();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await server.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            server.Kill(entireProcessTree: true);
            Assert.Fail("Server was still running 30s after its stdin closed.");
        }

        Assert.Equal(0, server.ExitCode);
    }
}
