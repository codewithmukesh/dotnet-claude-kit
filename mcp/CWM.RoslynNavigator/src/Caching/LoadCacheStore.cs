using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;

namespace CWM.RoslynNavigator.Caching;

/// <summary>
/// Where cached loads live and how they are read and written. One gzipped JSON file per
/// (solution set, options), under <c>%LOCALAPPDATA%/cwm-roslyn-navigator/cache</c> on Windows,
/// <c>$XDG_CACHE_HOME/cwm-roslyn-navigator</c> (default <c>~/.cache</c>) on Linux, and
/// <c>~/Library/Caches/cwm-roslyn-navigator</c> on macOS. <c>CWM_ROSLYN_CACHE_DIR</c> overrides
/// the root.
///
/// <para>Writes are atomic: the file is written under a unique temporary name and renamed over
/// the old one, so a reader — or a second server instance writing the same entry — never sees
/// a partial file. Every failure to read is a cache miss, never an error.</para>
/// </summary>
internal sealed class LoadCacheStore(string rootDirectory)
{
    public const string DirectoryEnvironmentVariable = "CWM_ROSLYN_CACHE_DIR";

    private const string AppDirectoryName = "cwm-roslyn-navigator";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly Lazy<string> s_navigatorBuild = new(ComputeNavigatorBuild);
    private static readonly Lazy<string> s_sdk = new(ComputeSdkIdentity);

    public string RootDirectory => rootDirectory;

    /// <summary>The default store for this machine, or one under <c>CWM_ROSLYN_CACHE_DIR</c>. Null when no location can be determined.</summary>
    public static LoadCacheStore? FromEnvironment(Func<string, string?>? readVariable = null)
    {
        readVariable ??= Environment.GetEnvironmentVariable;
        if (readVariable(DirectoryEnvironmentVariable) is { Length: > 0 } configured)
            return new LoadCacheStore(Path.GetFullPath(configured));

        string? root;
        if (OperatingSystem.IsWindows())
        {
            root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) is { Length: > 0 } local
                ? Path.Combine(local, AppDirectoryName, "cache")
                : null;
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var cacheHome = OperatingSystem.IsMacOS()
                ? (home.Length > 0 ? Path.Combine(home, "Library", "Caches") : null)
                : readVariable("XDG_CACHE_HOME") is { Length: > 0 } xdg ? xdg
                : home.Length > 0 ? Path.Combine(home, ".cache") : null;
            root = cacheHome is null ? null : Path.Combine(cacheHome, AppDirectoryName);
        }

        return root is null ? null : new LoadCacheStore(root);
    }

    /// <summary>The header a load of <paramref name="solutionPaths"/> with these options has today.</summary>
    public static LoadCacheHeader CreateHeader(IReadOnlyList<string> solutionPaths, bool allTargetFrameworks) =>
        new(s_navigatorBuild.Value, s_sdk.Value, allTargetFrameworks, [.. solutionPaths.Select(Path.GetFullPath)]);

    /// <summary>
    /// The cache file for a solution set: one folder per set of solution paths and options, so
    /// servers started with different options never overwrite each other's entry.
    /// </summary>
    public string GetFilePath(LoadCacheHeader header)
    {
        var identity = string.Join('\n', header.SolutionPaths.Select(p => OperatingSystem.IsLinux() ? p : p.ToUpperInvariant()))
                       + $"\nall-target-frameworks={header.AllTargetFrameworks}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16].ToLowerInvariant();
        return Path.Combine(rootDirectory, hash, $"load-cache.v{LoadCacheFile.CurrentFormat}.json.gz");
    }

    /// <summary>
    /// The cached load for <paramref name="header"/> if it is still valid, or null and why not.
    /// Never throws for a missing, unreadable, corrupt, or stale file.
    /// </summary>
    public (LoadCacheFile? File, string Reason) TryRead(LoadCacheHeader header, CancellationToken ct = default)
    {
        LoadCacheFile? file;
        try
        {
            file = ReadEntry(header);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return (null, "no cache entry yet");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Truncated, corrupt, or unreadable: a miss. The next successful load rewrites it.
            return (null, $"cache entry unreadable ({ex.GetType().Name}: {ex.Message})");
        }

        if (file is null || !file.IsWellFormed())
            return (null, "cache entry malformed");
        if (file.Header.NavigatorBuild != header.NavigatorBuild)
            return (null, "navigator build changed");
        if (file.Header.Sdk != header.Sdk)
            return (null, ".NET SDK changed");
        if (file.Header.AllTargetFrameworks != header.AllTargetFrameworks
            || !file.Header.SolutionPaths.SequenceEqual(header.SolutionPaths, PathComparison.Comparer))
        {
            return (null, "solutions or options changed");
        }

        ct.ThrowIfCancellationRequested();
        var current = LoadCacheInputs.Compute(file.Snapshot, header.SolutionPaths);
        if (LoadCacheInputs.FirstDifference(file.Inputs, current) is { } difference)
            return (null, difference);

        return (file, "hit");
    }

    /// <summary>
    /// The entry for <paramref name="header"/> exactly as stored, without any validation.
    /// Throws when it is missing or unreadable.
    /// </summary>
    internal LoadCacheFile? ReadEntry(LoadCacheHeader header)
    {
        // Shared for delete: a concurrent writer can still rename its new file over this one.
        using var stream = new FileStream(GetFilePath(header), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonSerializer.Deserialize<LoadCacheFile>(gzip, JsonOptions);
    }

    /// <summary>
    /// Writes <paramref name="file"/> atomically. Throws <see cref="IOException"/> or
    /// <see cref="UnauthorizedAccessException"/> when the cache folder is not writable.
    /// </summary>
    public void Write(LoadCacheFile file)
    {
        var path = GetFilePath(file.Header);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);

        var temp = Path.Combine(directory, $"{Path.GetFileName(path)}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var gzip = new GZipStream(stream, CompressionLevel.Fastest))
            {
                JsonSerializer.Serialize(gzip, file, JsonOptions);
            }

            MoveWithRetry(temp, path);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    /// <summary>
    /// The rename can collide with another instance renaming over the same file, or with a
    /// reader on a platform that does not allow replacing an open file; both are momentary.
    /// </summary>
    private static void MoveWithRetry(string source, string destination)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(source, destination, overwrite: true);
                return;
            }
            catch (Exception ex) when (attempt < 5 && ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(20 * attempt);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A stray temp file costs disk space only.
        }
    }

    /// <summary>Serializes a snapshot the way the file does: two snapshots are equivalent when these bytes match.</summary>
    public static byte[] Serialize(CachedSnapshot snapshot) => JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);

    public static byte[] Serialize(CachedProject project) => JsonSerializer.SerializeToUtf8Bytes(project, JsonOptions);

    /// <summary>
    /// This build of the navigator and of Roslyn. The module version id changes with every
    /// build of the navigator, so a cache never outlives a change to the code that loads it.
    /// </summary>
    private static string ComputeNavigatorBuild() =>
        $"{typeof(LoadCacheStore).Assembly.ManifestModule.ModuleVersionId:N};roslyn={typeof(Workspace).Assembly.GetName().Version}";

    /// <summary>
    /// The SDK the server registered and every SDK installed beside it: the build host picks
    /// its SDK by global.json (an input) from the installed ones, so installing an SDK can
    /// change what loads.
    /// </summary>
    private static string ComputeSdkIdentity()
    {
        // Microsoft.Build.Framework is excluded from the navigator's output, so it loads from
        // the SDK MSBuildLocator registered.
        var sdkDirectory = Path.GetDirectoryName(typeof(Microsoft.Build.Framework.ITask).Assembly.Location) ?? "";
        var installed = "";
        try
        {
            if (Path.GetDirectoryName(sdkDirectory) is { } sdks && Directory.Exists(sdks))
                installed = string.Join(',', Directory.GetDirectories(sdks).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return $"{sdkDirectory};installed={installed}";
    }
}
