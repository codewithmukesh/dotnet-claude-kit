using System.IO.Compression;
using System.Text;
using CWM.RoslynNavigator.Caching;

namespace CWM.RoslynNavigator.Tests.Caching;

/// <summary>
/// The cache never makes a start fail: a corrupt or partial entry is a miss, a folder that
/// cannot be written is logged and skipped, and concurrent writers (two servers on the same
/// solution) never leave a partial entry behind.
/// </summary>
public sealed class LoadCacheRobustnessTests : IDisposable
{
    private readonly LoadCacheTestbed _bed = new();

    public static TheoryData<string> Corruptions => ["garbage", "truncated", "empty", "malformed json", "dangling index"];

    [Theory]
    [MemberData(nameof(Corruptions))]
    public async Task LoadSolution_CorruptEntry_LoadsWithMSBuildAndRewritesEntry(string corruption)
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await _bed.LoadAndCacheAsync(ct);
        Corrupt(_bed.Store.GetFilePath(_bed.Header), corruption);
        var workspace = _bed.CreateWorkspace();

        // Act
        await workspace.LoadSolutionAsync(_bed.SolutionPath, ct);
        await workspace.WaitForCacheWriteAsync();

        // Assert
        Assert.Equal(WorkspaceState.Ready, workspace.State);
        Assert.Equal("msbuild", workspace.CacheStatus!.Source);
        Assert.Matches("unreadable|malformed", workspace.CacheStatus.Detail);
        Assert.NotEmpty(await SymbolResolver.FindSymbolsByNameAsync(workspace, "OrderService", ct: ct));
        Assert.Equal("hit", _bed.Store.TryRead(_bed.Header, ct).Reason);
    }

    [Fact]
    public async Task LoadSolution_CacheFolderNotWritable_LoadsAndContinuesWithoutCache()
    {
        // Arrange: a cache root below a regular file can never be created.
        var ct = TestContext.Current.CancellationToken;
        var blocker = Path.Combine(_bed.Data.Root, "not-a-folder");
        await File.WriteAllTextAsync(blocker, "", ct);
        var store = new LoadCacheStore(Path.Combine(blocker, "cache"));
        var workspace = _bed.CreateWorkspace(store);

        // Act
        await workspace.LoadSolutionAsync(_bed.SolutionPath, ct);
        await workspace.WaitForCacheWriteAsync();

        // Assert
        Assert.Equal(WorkspaceState.Ready, workspace.State);
        Assert.Equal("msbuild", workspace.CacheStatus!.Source);
        Assert.NotEmpty(await SymbolResolver.FindSymbolsByNameAsync(workspace, "OrderService", ct: ct));
        Assert.Null(store.TryRead(_bed.Header, ct).File);

        // A second start fares the same: no entry, no error.
        var second = _bed.CreateWorkspace(store);
        await second.LoadSolutionAsync(_bed.SolutionPath, ct);
        Assert.Equal(WorkspaceState.Ready, second.State);
    }

    [Fact]
    public async Task Write_ConcurrentWritersAndReaders_NeverExposePartialEntry()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await _bed.LoadAndCacheAsync(ct);
        var entry = _bed.Store.ReadEntry(_bed.Header)!;
        using var stop = new CancellationTokenSource();
        List<string> readReasons = [];

        // Act: two "servers" rewriting the entry while a third keeps reading it.
        var reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                var reason = _bed.Store.TryRead(_bed.Header, ct).Reason;
                lock (readReasons) readReasons.Add(reason);
            }
        }, ct);
        var written = 0;
        var writers = Enumerable.Range(0, 2).Select(writer => Task.Run(() =>
        {
            for (var i = 0; i < 25; i++)
            {
                try
                {
                    _bed.Store.Write(entry with { LoadStartedUtc = entry.LoadStartedUtc.AddTicks(writer * 1000 + i) });
                    Interlocked.Increment(ref written);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Windows denies replacing a file another handle has open; under a reader
                    // that never pauses a write may give up, leaving the previous entry. The
                    // server logs that and carries on (LoadCacheFolderNotWritable covers it).
                }
            }
        }, ct)).ToArray();
        await Task.WhenAll(writers);
        await stop.CancelAsync();
        await reader;

        // Assert: readers only ever see a complete entry, old or new.
        Assert.True(written > 0);
        Assert.NotEmpty(readReasons);
        Assert.All(readReasons, reason => Assert.Equal("hit", reason));
        Assert.Equal("hit", _bed.Store.TryRead(_bed.Header, ct).Reason);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(_bed.Store.GetFilePath(_bed.Header))!, "*.tmp"));
    }

    [Fact]
    public async Task LoadSolution_TwoServersStartTogether_BothLoadAndLeaveOneValidEntry()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var first = _bed.CreateWorkspace();
        var second = _bed.CreateWorkspace();

        // Act
        await Task.WhenAll(first.LoadSolutionAsync(_bed.SolutionPath, ct), second.LoadSolutionAsync(_bed.SolutionPath, ct));
        await Task.WhenAll(first.WaitForCacheWriteAsync(), second.WaitForCacheWriteAsync());

        // Assert
        Assert.Equal(WorkspaceState.Ready, first.State);
        Assert.Equal(WorkspaceState.Ready, second.State);
        Assert.Equal("hit", _bed.Store.TryRead(_bed.Header, ct).Reason);
        var third = _bed.CreateWorkspace();
        await third.LoadSolutionAsync(_bed.SolutionPath, ct);
        Assert.Equal("cache", third.CacheStatus!.Source);
    }

    private static void Corrupt(string path, string corruption)
    {
        switch (corruption)
        {
            case "garbage":
                File.WriteAllBytes(path, Encoding.UTF8.GetBytes("this is not a cache entry"));
                break;
            case "truncated":
                var bytes = File.ReadAllBytes(path);
                File.WriteAllBytes(path, bytes[..(bytes.Length / 2)]);
                break;
            case "empty":
                File.WriteAllBytes(path, []);
                break;
            case "malformed json":
                WriteGzip(path, """{"Format":1,"Header":null}""");
                break;
            case "dangling index":
                // Well-formed JSON whose project reference points past the project list.
                string json;
                using (var reader = new StreamReader(new GZipStream(File.OpenRead(path), CompressionMode.Decompress)))
                    json = reader.ReadToEnd();

                const string marker = "\"ProjectReferences\":[{\"Project\":";
                var at = json.IndexOf(marker, StringComparison.Ordinal);
                Assert.True(at >= 0, "the sample has a project reference");
                var end = json.IndexOf(',', at + marker.Length);
                WriteGzip(path, json[..(at + marker.Length)] + "999" + json[end..]);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(corruption), corruption, null);
        }
    }

    private static void WriteGzip(string path, string text)
    {
        using var stream = File.Create(path);
        using var gzip = new GZipStream(stream, CompressionLevel.Fastest);
        gzip.Write(Encoding.UTF8.GetBytes(text));
    }

    public void Dispose() => _bed.Dispose();
}
