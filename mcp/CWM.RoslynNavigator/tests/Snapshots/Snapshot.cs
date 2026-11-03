using System.Runtime.CompilerServices;

namespace CWM.RoslynNavigator.Tests.Snapshots;

/// <summary>
/// Minimal approval-style snapshot assertion. Compares text against a committed
/// <c>Verified/{name}.verified.txt</c>; on a mismatch or a missing file it writes
/// <c>{name}.received.txt</c> beside it and fails. Accept a change by renaming the received
/// file over the verified one, or run with <c>CWM_SNAPSHOT_ACCEPT=1</c> to overwrite all.
///
/// <para>Why not Verify: Verify 33.x fails the build (SponsorCheck SC021) until the consumer
/// declares a sponsorship or a fee exemption, which is a licensing decision for the repo
/// owner, not something a test commit should assert. File names follow Verify's convention,
/// so switching to it later is a drop-in.</para>
/// </summary>
public static class Snapshot
{
    private const string AcceptVariable = "CWM_SNAPSHOT_ACCEPT";

    public static void Match(string actual, string name, [CallerFilePath] string sourceFile = "")
    {
        var directory = Path.Combine(Path.GetDirectoryName(sourceFile)!, "Verified");
        var verifiedPath = Path.Combine(directory, $"{name}.verified.txt");
        var receivedPath = Path.Combine(directory, $"{name}.received.txt");
        actual = Normalize(actual);

        var verified = File.Exists(verifiedPath) ? Normalize(File.ReadAllText(verifiedPath)) : null;
        if (verified == actual)
        {
            File.Delete(receivedPath);
            return;
        }

        Directory.CreateDirectory(directory);
        if (Environment.GetEnvironmentVariable(AcceptVariable) == "1")
        {
            File.WriteAllText(verifiedPath, actual);
            File.Delete(receivedPath);
            return;
        }

        File.WriteAllText(receivedPath, actual);
        Assert.Fail(verified is null
            ? $"No verified snapshot for '{name}'. Review {receivedPath} and rename it to .verified.txt."
            : $"Snapshot '{name}' changed. Diff {receivedPath} against {verifiedPath}; if the change is intended, accept it (or set {AcceptVariable}=1).");
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n') + "\n";
}
