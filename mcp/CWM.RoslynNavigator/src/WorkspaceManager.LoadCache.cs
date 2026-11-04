using System.Diagnostics;
using CWM.RoslynNavigator.Caching;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace CWM.RoslynNavigator;

/// <summary>
/// The persistent load cache (<see cref="LoadCache"/>). Nearly all of a large solution's start
/// is MSBuild evaluating and design-time-building every project (OrchardCore: ~45 s of a 50 s
/// start). What that produces — each project's files, references, and options — rarely
/// changes between sessions, so the first load writes it to disk and the next start whose
/// inputs (<see cref="LoadCacheInputs"/>) are unchanged rebuilds the solution from it and is
/// Ready within seconds.
///
/// <para>A restored snapshot is then verified: the real MSBuild load runs in the background,
/// exactly as a reload does. When MSBuild produces the same projects, documents, references,
/// and options, the restored snapshot stays — with the compilations warm-up has built for it
/// meanwhile. When anything differs, MSBuild's snapshot is swapped in as after any reload.
/// Either way the cache is rewritten from the MSBuild result.</para>
/// </summary>
public sealed partial class WorkspaceManager
{
    private const string CacheVerificationUnchanged = "unchanged";
    private const string CacheVerificationChanged = "changed";

    /// <summary>
    /// Restores the solution from the cache and swaps it in, then starts the background
    /// verification. False — with the reason recorded — when the cache has no valid entry or
    /// restoring it fails; the caller then loads with MSBuild. Must hold <see cref="_loadLock"/>.
    /// </summary>
    private async Task<bool> TryRestoreFromCacheAsync(LoadCacheStore cache, string[] solutionPaths, CancellationToken ct)
    {
        var profile = new LoadProfile();
        LoadCacheFile? file;
        string reason;
        try
        {
            var header = LoadCacheStore.CreateHeader(solutionPaths, AllTargetFrameworks);
            (file, reason) = await Task.Run(() => cache.TryRead(header, ct), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            (file, reason) = (null, $"cache check failed ({ex.Message})");
        }

        profile.Step("check load cache");
        if (file is null)
        {
            _cacheDetail = $"loaded with MSBuild: {reason}";
            logger.LogInformation("Load cache miss ({Reason}); loading with MSBuild.", reason);
            return false;
        }

        var workspace = new AdhocWorkspace(ShadowCopyAnalyzerService.HostServices, WorkspaceKind.MSBuild);
        Interlocked.Increment(ref _liveWorkspaces);
        var published = false;
        try
        {
            var existing = ExistingFiles(SnapshotMapper.DocumentPaths(file.Snapshot));
            var snapshot = SnapshotMapper.WithoutMissingFiles(file.Snapshot, existing, out var dropped);
            var (solution, projectIds) = SnapshotMapper.Restore(snapshot, workspace, solutionPaths[0]);

            var health = new WorkspaceHealth(clock);
            foreach (var diagnostic in file.Diagnostics)
                health.RecordDiagnostic(diagnostic.Kind, diagnostic.Message);
            foreach (var removed in file.RemovedAnalyzers)
                health.RecordRemovedAnalyzers(projectIds[removed.Project], removed.Paths);

            var loaded = file.Solutions.Select(s => new LoadedSolution(s.Path, s.ProjectPaths, s.Error)).ToList();
            profile.Step("restore from cache");

            _cacheVerification = CacheVerificationRunning;
            _cacheDetail = dropped > 0
                ? $"restored from the cache without {dropped} document(s) deleted since it was written"
                : null;

            // The original load's start, not now: a file created since then is new to this
            // snapshot and is picked up by the next refresh like any file created after a load.
            await SwapAsync(new PendingLoad(
                solutionPaths, workspace, solution, loaded, health, file.LoadStartedUtc,
                new Dictionary<string, DateTime>(), profile, file.LoadStartedUtc, FromCache: true));
            published = true;

            logger.LogInformation(
                "Load cache hit: {ProjectCount} projects restored in {Elapsed} ms ({Dropped} deleted documents left out); verifying against MSBuild in the background.",
                solution.ProjectIds.Count, profile.TotalMs, dropped);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _cacheVerification = null;
            _cacheDetail = $"loaded with MSBuild: cache entry could not be restored ({ex.Message})";
            logger.LogWarning(ex, "Load cache entry could not be restored; loading with MSBuild.");
            return false;
        }
        finally
        {
            if (!published)
                DisposeWorkspace(workspace);
        }

        StartCacheVerification(solutionPaths);
        return true;
    }

    /// <summary>Runs <see cref="VerifyCachedSnapshotAsync"/> in the background, after the load that started it releases the load lock.</summary>
    private void StartCacheVerification(string[] solutionPaths)
    {
        var token = _backgroundCts.Token;
        _cacheVerificationTask = Task.Run(async () =>
        {
            try
            {
                await _loadLock.WaitAsync(token);
                try
                {
                    await VerifyCachedSnapshotAsync(solutionPaths, token);
                }
                finally
                {
                    _loadLock.Release();
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                _cacheVerification = "cancelled";
            }
            catch (Exception ex)
            {
                // The restored snapshot stays: it matched every input when it was restored.
                _cacheDetail = $"MSBuild verification failed: {ex.Message}";
                _cacheVerification = "failed";
                logger.LogWarning(ex, "Verifying the cached snapshot with MSBuild failed; keeping the cached snapshot.");
            }
        }, CancellationToken.None);
    }

    /// <summary>
    /// Loads the solutions with MSBuild and compares the result with the current snapshot.
    /// Equivalent: the snapshot (and its warm compilations) stays. Different: MSBuild's
    /// snapshot is swapped in. Must hold <see cref="_loadLock"/>.
    /// </summary>
    private async Task VerifyCachedSnapshotAsync(string[] solutionPaths, CancellationToken ct)
    {
        // A reload that got the load lock first has already replaced the cached snapshot.
        if (!_snapshotFromCache || !ReferenceEquals(_solutionPaths, solutionPaths))
        {
            _cacheVerification = "superseded";
            return;
        }

        var started = Stopwatch.GetTimestamp();
        var pending = await OpenSolutionsAsync(solutionPaths, ct);
        try
        {
            if (BeforeSwap is { } beforeSwap)
                await beforeSwap(ct);

            // Compared with the snapshot as it is now, refreshes included: a document deleted
            // or added since the restore is in both or in neither. Files MSBuild lists but
            // that do not exist load as empty documents and are left out of both sides.
            var fresh = SnapshotMapper.Capture(pending.Solution, out var notCacheable);
            var current = SnapshotMapper.Capture(_solution!, out _);
            var difference = fresh is null || current is null
                ? $"the MSBuild load cannot be cached ({notCacheable})"
                : SnapshotMapper.Compare(
                    SnapshotMapper.WithoutMissingFiles(fresh, ExistingFiles(SnapshotMapper.DocumentPaths(fresh)), out _),
                    current);
            var elapsed = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

            if (difference is null)
            {
                await _writeLock.WaitAsync(ct);
                try
                {
                    // MSBuild has now evaluated every project: a file that existed before this
                    // load and is not a document was excluded by it.
                    _lastLoadStartedUtc = pending.StartedUtc;
                }
                finally
                {
                    _writeLock.Release();
                }

                ScheduleCacheWrite(pending);
                _cacheVerification = CacheVerificationUnchanged;
                logger.LogInformation(
                    "Load cache verified in {Elapsed} ms: MSBuild produced the same {ProjectCount} projects; keeping the cached snapshot and its compilations.",
                    elapsed, pending.Solution.ProjectIds.Count);
                return;
            }

            logger.LogInformation(
                "Load cache verification found a difference after {Elapsed} ms ({Difference}); swapping in the MSBuild snapshot.",
                elapsed, difference);
            var verified = pending;
            await SwapAsync(verified);
            pending = null;
            ScheduleCacheWrite(verified);
            _cacheDetail = $"MSBuild load differed from the cache ({difference}); its snapshot replaced the cached one";
            _cacheVerification = CacheVerificationChanged;
        }
        finally
        {
            if (pending is not null)
                DisposeWorkspace(pending.Workspace);
        }
    }

    /// <summary>
    /// Writes the cache entry for a completed MSBuild load in the background. The description
    /// is captured now, from the immutable snapshot the load produced; the slow part (input
    /// fingerprints, serialization, the write) runs off the request path, one write at a time.
    /// </summary>
    private void ScheduleCacheWrite(PendingLoad load)
    {
        if (LoadCache is not { } cache || _disposed) return;

        var snapshot = SnapshotMapper.Capture(load.Solution, out var notCacheable);
        if (snapshot is null)
        {
            logger.LogInformation("Load not cached: {Reason}.", notCacheable);
            return;
        }

        var projectIndex = new Dictionary<ProjectId, int>();
        foreach (var id in load.Solution.ProjectIds)
            projectIndex[id] = projectIndex.Count;

        var file = new LoadCacheFile(
            LoadCacheFile.CurrentFormat,
            LoadCacheStore.CreateHeader(load.SolutionPaths, AllTargetFrameworks),
            Inputs: [],
            load.DiskTimeAtStart,
            [.. load.Loaded.Select(s => new CachedSolutionFile(s.Path, [.. s.ProjectPaths], s.Error))],
            [.. load.Health.RecordedDiagnostics.Select(d => new CachedDiagnostic(d.Kind, d.Message))],
            [.. load.Health.RemovedAnalyzers
                .Where(r => projectIndex.ContainsKey(r.Key))
                .Select(r => new CachedRemovedAnalyzers(projectIndex[r.Key], [.. r.Value]))
                .OrderBy(r => r.Project)],
            snapshot);

        _cacheWrite = Task.Run(async () =>
        {
            await _cacheWriteLock.WaitAsync();
            try
            {
                WriteCache(cache, file);
            }
            finally
            {
                _cacheWriteLock.Release();
            }
        }, CancellationToken.None);
    }

    private void WriteCache(LoadCacheStore cache, LoadCacheFile file)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var inputs = LoadCacheInputs.Compute(file.Snapshot, file.Header.SolutionPaths);

            // An input written after the load started may not be the version MSBuild read; an
            // entry keyed on it would later match a file the snapshot never saw. The change
            // itself triggers a reload, whose own write records the right state.
            if (LoadCacheInputs.LatestWriteUtc(inputs) >= file.LoadStartedUtc.UtcDateTime)
            {
                logger.LogInformation("Load cache not written: a build input changed while the solution was loading.");
                return;
            }

            cache.Write(file with { Inputs = inputs });
            logger.LogInformation(
                "Load cache written in {Elapsed} ms: {ProjectCount} projects, {InputCount} inputs.",
                (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds, file.Snapshot.Projects.Count, inputs.Count);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Read-only home, full disk, locked folder: the server works the same without it.
            logger.LogWarning("Load cache not written to {Directory} ({Message}); continuing without it.", cache.RootDirectory, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Load cache not written; continuing without it.");
        }
    }

    /// <summary>
    /// Which of <paramref name="paths"/> exist, with one listing per directory (read in
    /// parallel) rather than a stat per file. A directory that cannot be read counts all of
    /// its files as present, so a transient failure never drops documents.
    /// </summary>
    private static HashSet<string> ExistingFiles(IEnumerable<string> paths)
    {
        var byDirectory = paths
            .GroupBy(path => Path.GetDirectoryName(path) ?? string.Empty, PathComparison.Comparer)
            .ToArray();
        var present = new List<string>?[byDirectory.Length];

        Parallel.For(0, byDirectory.Length, new ParallelOptions { MaxDegreeOfParallelism = ScanParallelism }, i =>
        {
            var listing = ReadDirectoryTimestamps(byDirectory[i].Key);
            present[i] = listing is null ? [.. byDirectory[i]] : [.. byDirectory[i].Where(listing.ContainsKey)];
        });

        var result = new HashSet<string>(PathComparison.Comparer);
        foreach (var files in present)
            result.UnionWith(files!);
        return result;
    }

    /// <summary>Awaits the background verification of a restored snapshot. Test seam.</summary>
    internal Task WaitForCacheVerificationAsync() => _cacheVerificationTask;

    /// <summary>Awaits the in-flight cache write. Test seam.</summary>
    internal Task WaitForCacheWriteAsync() => _cacheWrite;
}
