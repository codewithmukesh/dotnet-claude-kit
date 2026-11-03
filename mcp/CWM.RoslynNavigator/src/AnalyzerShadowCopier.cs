using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CWM.RoslynNavigator;

/// <summary>
/// Copies analyzer and source-generator directories into a per-process shadow root so the
/// originals — normally under ~/.nuget/packages — are never memory-mapped by this process.
/// Roslyn's default analyzer loader keeps every loaded assembly mapped for the life of the
/// process (dotnet/roslyn#78196), which on Windows makes `dotnet restore` fail with
/// "Access to the path ... is denied" whenever it needs to rewrite one of those files.
/// </summary>
/// <remarks>
/// The whole containing directory is copied, not just the requested file, because Roslyn
/// resolves an analyzer's dependencies (and satellite resource assemblies) from the
/// directory it was loaded from. A directory is copied once and reused until one of its
/// files changes on disk, at which point a fresh copy is made so the new bits get loaded.
/// </remarks>
internal sealed class AnalyzerShadowCopier(string shadowRoot) : IDisposable
{
    private static readonly Lazy<AnalyzerShadowCopier> s_currentProcess = new(CreateForCurrentProcess);

    private readonly Lock _gate = new();
    private readonly Dictionary<string, DirectoryCopy> _copies = new(StringComparer.OrdinalIgnoreCase);
    private int _generation;

    /// <summary>
    /// Directory holding every navigator process's shadow root. Scoped per user because the temp
    /// folder is shared on Linux and another user's directory would not be writable.
    /// </summary>
    public static string BaseDirectory { get; } =
        Path.Combine(Path.GetTempPath(), $"cwm-roslyn-navigator-{Environment.UserName}", "shadow");

    /// <summary>
    /// The copier for this process. Created on first use, which also sweeps shadow roots left
    /// behind by navigator processes that have since exited (or were killed).
    /// </summary>
    public static AnalyzerShadowCopier CurrentProcess => s_currentProcess.Value;

    public string ShadowRoot { get; } = shadowRoot;

    /// <summary>
    /// Returns the shadow-copy path to load <paramref name="originalPath"/> from, copying its
    /// directory first if needed. Falls back to the original path when the file does not exist
    /// (Roslyn then reports the missing analyzer as usual) or the copy fails — a locked file is
    /// a better outcome than an analyzer that silently stops loading.
    /// </summary>
    public string GetShadowPath(string originalPath)
    {
        var sourceDirectory = Path.GetDirectoryName(originalPath);
        var fileName = Path.GetFileName(originalPath);
        if (sourceDirectory is null || !File.Exists(originalPath))
            return originalPath;

        var stamp = FileStamp.Of(originalPath);

        lock (_gate)
        {
            if (_copies.TryGetValue(sourceDirectory, out var existing) &&
                existing.Stamps.TryGetValue(fileName, out var copiedStamp) &&
                copiedStamp == stamp)
            {
                return Path.Combine(existing.ShadowDirectory, fileName);
            }

            try
            {
                var copy = CopyDirectory(sourceDirectory);
                _copies[sourceDirectory] = copy;
                return Path.Combine(copy.ShadowDirectory, fileName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // stderr is the log channel for a stdio MCP server; stdout is reserved for JSON-RPC.
                Console.Error.WriteLine(
                    $"cwm-roslyn-navigator: could not shadow-copy analyzer directory '{sourceDirectory}' " +
                    $"({ex.Message}); loading it in place, which locks the file until this process exits.");
                return originalPath;
            }
        }
    }

    /// <summary>
    /// Best-effort removal of this process's shadow root. Assemblies already loaded stay mapped
    /// until the process exits, so on Windows some files survive; the next navigator to start
    /// sweeps them via <see cref="SweepStaleRoots"/>.
    /// </summary>
    public void Dispose() => TryDeleteDirectory(ShadowRoot);

    /// <summary>
    /// Shadow root name for a process: its id plus start time, so a recycled process id is
    /// never mistaken for the process that originally owned the directory.
    /// </summary>
    internal static string GetRootName(int processId, DateTimeOffset startTime) =>
        $"{processId}-{startTime.ToUnixTimeSeconds()}";

    /// <summary>
    /// Deletes shadow roots whose owning process is no longer running. Directories that do not
    /// follow the naming scheme are left alone, as is any root whose owner cannot be inspected.
    /// </summary>
    internal static void SweepStaleRoots(string baseDirectory, string? currentRootName = null)
    {
        if (!Directory.Exists(baseDirectory))
            return;

        foreach (var root in Directory.EnumerateDirectories(baseDirectory))
        {
            var name = Path.GetFileName(root);
            if (string.Equals(name, currentRootName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (TryParseRootName(name, out var processId, out var startSeconds) &&
                !IsProcessAlive(processId, startSeconds))
            {
                TryDeleteDirectory(root);
            }
        }
    }

    private static AnalyzerShadowCopier CreateForCurrentProcess()
    {
        using var process = Process.GetCurrentProcess();
        var rootName = GetRootName(process.Id, process.StartTime);
        SweepStaleRoots(BaseDirectory, rootName);

        var copier = new AnalyzerShadowCopier(Path.Combine(BaseDirectory, rootName));
        AppDomain.CurrentDomain.ProcessExit += (_, _) => copier.Dispose();
        return copier;
    }

    private DirectoryCopy CopyDirectory(string sourceDirectory)
    {
        // Hash of the source path keeps names short and filesystem-safe; the generation suffix
        // gives a changed directory a fresh location, since the old copy may still be loaded.
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceDirectory)))[..16];
        var target = Path.Combine(ShadowRoot, $"{hash}-{++_generation}");
        Directory.CreateDirectory(target);

        var stamps = new Dictionary<string, FileStamp>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            var name = Path.GetFileName(file);
            stamps[name] = FileStamp.Of(file);
            File.Copy(file, Path.Combine(target, name), overwrite: true);
        }

        // Culture subdirectories carry the satellite assemblies (e.g. de/Foo.resources.dll) used
        // for localized diagnostics. Anything else is skipped: an analyzer referenced straight
        // from a build output or repo folder must not drag that whole tree into the copy.
        foreach (var subdirectory in Directory.EnumerateDirectories(sourceDirectory))
        {
            var culture = Path.GetFileName(subdirectory);
            if (!IsCultureName(culture))
                continue;

            var subTarget = Directory.CreateDirectory(Path.Combine(target, culture)).FullName;
            foreach (var file in Directory.EnumerateFiles(subdirectory, "*.dll"))
                File.Copy(file, Path.Combine(subTarget, Path.GetFileName(file)), overwrite: true);
        }

        return new DirectoryCopy(target, stamps);
    }

    private static bool IsCultureName(string name)
    {
        try
        {
            return CultureInfo.GetCultureInfo(name, predefinedOnly: true) is { Name.Length: > 0 };
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    private static bool TryParseRootName(string name, out int processId, out long startSeconds)
    {
        processId = 0;
        startSeconds = 0;
        var dash = name.IndexOf('-');
        return dash > 0 &&
               int.TryParse(name.AsSpan(0, dash), out processId) &&
               long.TryParse(name.AsSpan(dash + 1), out startSeconds);
    }

    private static bool IsProcessAlive(int processId, long startSeconds)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            // Start times derived from /proc on Linux can drift by a second between reads.
            var actual = new DateTimeOffset(process.StartTime).ToUnixTimeSeconds();
            return Math.Abs(actual - startSeconds) <= 2;
        }
        catch (ArgumentException)
        {
            return false; // No process with that id.
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception
                                       or NotSupportedException)
        {
            return true; // Exists but cannot be inspected (e.g. another user's process) — leave it.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Still mapped by a live or exiting process; a later sweep retries.
        }
    }

    private sealed record DirectoryCopy(string ShadowDirectory, IReadOnlyDictionary<string, FileStamp> Stamps);

    private readonly record struct FileStamp(DateTime LastWriteUtc, long Length)
    {
        public static FileStamp Of(string path)
        {
            var info = new FileInfo(path);
            return new FileStamp(info.LastWriteTimeUtc, info.Length);
        }
    }
}
