using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CWM.RoslynNavigator;

/// <summary>
/// Manages the MSBuildWorkspace lifecycle: loading, on-demand refresh, and compilation caching.
/// File watching is intentionally avoided — on Linux, recursive FileSystemWatcher creates
/// one inotify watch per subdirectory, quickly exhausting the kernel limit for large solutions.
/// Instead, documents are refreshed on demand when tools are invoked.
/// </summary>
public sealed class WorkspaceManager(ILogger<WorkspaceManager> logger, TimeProvider clock) : IDisposable
{
    private const int LazyLoadThreshold = 50;
    private const int MaxCachedCompilations = 30;

    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<ProjectId, Compilation> _compilationCache = new();
    private readonly ConcurrentDictionary<ProjectId, long> _cacheAccessOrder = new();
    private readonly ConcurrentDictionary<DocumentId, DocumentInfo> _knownDocuments = new();
    private readonly ConcurrentDictionary<string, BuildFileInfo> _buildFiles = new();
    private readonly ConcurrentDictionary<string, byte> _knownDocumentPaths = new(StringComparer.OrdinalIgnoreCase);
    private long _accessCounter;
    private int _loadCount;
    private readonly CancellationTokenSource _backgroundCts = new();
    private int _structuralScanRunning;
    private int _newSourceFilesPending;
    private Task _structuralScan = Task.CompletedTask;
    private int _rootsAttempted; // 0 = not tried, 1 = tried
    private long _lastRefreshTicks;
    private long _lastStructuralScanTicks;
    private long _lastErrorRetryTicks;
    private static readonly long RefreshCooldownTicks = TimeSpan.FromSeconds(5).Ticks;
    private static readonly long StructuralScanCooldownTicks = TimeSpan.FromSeconds(60).Ticks;
    private static readonly long ErrorRetryCooldownTicks = TimeSpan.FromSeconds(30).Ticks;

    private MSBuildWorkspace? _workspace;
    private Solution? _solution;
    private IReadOnlyList<string> _solutionPaths = [];
    private IReadOnlyList<LoadedSolution> _loadedSolutions = [];
    private Dictionary<string, List<string>> _projectSolutions = new(StringComparer.OrdinalIgnoreCase);
    private string? _rootDirectory;
    private string? _errorMessage;

    public WorkspaceState State { get; private set; } = WorkspaceState.NotStarted;
    public string? ErrorMessage => _errorMessage;
    public int ProjectCount => _solution?.ProjectIds.Count ?? 0;
    public bool IsLazyLoading => ProjectCount > LazyLoadThreshold;

    /// <summary>
    /// Set by Program.cs after host build to allow lazy McpServer resolution.
    /// </summary>
    internal IServiceProvider? Services { get; set; }

    /// <summary>
    /// When set (<c>--all-solutions</c>), MCP roots discovery loads every solution under each
    /// root instead of the first. Set by Program.cs.
    /// </summary>
    internal bool DiscoverAllSolutions { get; set; }

    /// <summary>
    /// Number of compilations currently cached. Test seam for verifying reload eviction.
    /// </summary>
    internal int CachedCompilationCount => _compilationCache.Count;

    /// <summary>
    /// How many times the solution has been loaded. Test seam for verifying that a
    /// no-op build-file rewrite does not trigger a reload.
    /// </summary>
    internal int LoadCount => _loadCount;

    /// <summary>
    /// Loads the solution at the specified path. Call this once on startup.
    /// </summary>
    public Task LoadSolutionAsync(string solutionPath, CancellationToken ct = default) =>
        LoadSolutionsAsync([solutionPath], ct);

    /// <summary>
    /// Loads one or more solutions into a single workspace. The first opens via
    /// <c>OpenSolutionAsync</c> exactly as a single-solution load does; each further solution
    /// contributes only the projects not already loaded, so a library shared by several
    /// solutions is loaded — and compiled — once, and project references across solution
    /// boundaries resolve to that one instance. Every tool then sees all solutions at once.
    /// A failure in an additional solution is recorded against it and does not fail the load.
    /// </summary>
    public async Task LoadSolutionsAsync(IReadOnlyList<string> solutionPaths, CancellationToken ct = default)
    {
        if (solutionPaths.Count == 0)
            throw new ArgumentException("At least one solution path is required.", nameof(solutionPaths));

        await _writeLock.WaitAsync(ct);
        var solutionPath = solutionPaths[0];
        try
        {
            State = WorkspaceState.Loading;
            _solutionPaths = [.. solutionPaths];
            _rootDirectory = ComputeRootDirectory(solutionPaths);
            _errorMessage = null;

            // A reload mints fresh ProjectIds, so every cached Compilation is keyed on a
            // dead id. Clearing here (rather than at each call site) keeps the roots-discovery
            // and error-retry paths from resurrecting stale entries.
            _compilationCache.Clear();
            _cacheAccessOrder.Clear();

            logger.LogInformation("Loading solution: {SolutionPath}", string.Join(", ", solutionPaths));

            // Dispose previous workspace to avoid leaking Roslyn Solution snapshots
            _workspace?.Dispose();

            // Shadow-copying host: analyzers and generators load from temp copies, never from
            // ~/.nuget/packages, so this long-lived process cannot block `dotnet restore` (#25).
            _workspace = MSBuildWorkspace.Create(ShadowCopyAnalyzerService.HostServices);
            _workspace.RegisterWorkspaceFailedHandler(args =>
            {
                if (args.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
                    logger.LogError("Workspace failure: {Message}", args.Diagnostic.Message);
                else
                    logger.LogWarning("Workspace warning: {Message}", args.Diagnostic.Message);
            });

            _solution = await _workspace.OpenSolutionAsync(solutionPath, cancellationToken: ct);
            List<LoadedSolution> loaded = [await DescribePrimaryAsync(solutionPath, _solution, ct)];

            foreach (var additional in solutionPaths.Skip(1))
                loaded.Add(await AddSolutionProjectsAsync(_workspace, additional, ct));

            _solution = _workspace.CurrentSolution;
            _loadedSolutions = loaded;
            _projectSolutions = BuildMembership(loaded);
            Interlocked.Increment(ref _loadCount);

            if (solutionPaths.Count == 1)
                logger.LogInformation("Solution loaded: {ProjectCount} projects", _solution.ProjectIds.Count);
            else
                logger.LogInformation("{SolutionCount} solutions loaded: {ProjectCount} distinct projects",
                    solutionPaths.Count, _solution.ProjectIds.Count);

            if (!IsLazyLoading)
            {
                await WarmCompilationsAsync(ct);
            }
            else
            {
                logger.LogInformation("Large solution detected ({Count} projects). Using lazy loading.",
                    _solution.ProjectIds.Count);
            }

            SnapshotFileTimestamps();
            State = WorkspaceState.Ready;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load solution: {SolutionPath}", solutionPath);
            _errorMessage = ex.Message;
            State = WorkspaceState.Error;
            throw;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Gets the current solution snapshot. Returns null if workspace is not ready.
    /// </summary>
    public Solution? GetSolution() => _solution;

    /// <summary>
    /// Root for relative paths: the directory containing the loaded solution file, or — with
    /// several solutions — the deepest directory containing all of them, so every path a tool
    /// returns stays relative (no "../") and round-trips through the file-addressed tools.
    /// </summary>
    public string? SolutionDirectory => _rootDirectory;

    /// <summary>Path of the primary (first) solution file, or null before one is discovered.</summary>
    public string? SolutionFilePath => _solutionPaths.Count > 0 ? _solutionPaths[0] : null;

    /// <summary>Every configured solution file, primary first.</summary>
    public IReadOnlyList<string> SolutionFilePaths => _solutionPaths;

    /// <summary>The solutions in the current workspace, with their declared projects and any load error.</summary>
    public IReadOnlyList<LoadedSolution> LoadedSolutions => _loadedSolutions;

    /// <summary>
    /// File names of the solutions that declare the project at <paramref name="projectFilePath"/>.
    /// Empty for a project loaded only as a reference of a declared one.
    /// </summary>
    public IReadOnlyList<string> GetSolutionsForProject(string? projectFilePath) =>
        projectFilePath is not null
        && _projectSolutions.TryGetValue(SolutionFileReader.NormalizePath(projectFilePath), out var names)
            ? names
            : [];

    /// <summary>
    /// Membership for the primary solution. Parsing the file (rather than reading the loaded
    /// projects) keeps referenced-but-undeclared projects out, matching how additional
    /// solutions are described; the loaded projects are the fallback if parsing fails.
    /// </summary>
    private async Task<LoadedSolution> DescribePrimaryAsync(string solutionPath, Solution solution, CancellationToken ct)
    {
        try
        {
            return new LoadedSolution(solutionPath, await SolutionFileReader.GetProjectPathsAsync(solutionPath, ct), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Could not parse {SolutionPath}; using its loaded projects.", solutionPath);
            var loaded = solution.Projects
                .Select(p => p.FilePath)
                .OfType<string>()
                .Select(SolutionFileReader.NormalizePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return new LoadedSolution(solutionPath, loaded, null);
        }
    }

    /// <summary>
    /// Opens every project of <paramref name="solutionPath"/> that the workspace does not
    /// already hold. <c>OpenProjectAsync</c> also loads project references and resolves the
    /// ones already in the workspace to the existing project, so a shared library is never
    /// duplicated. One project failing to load is logged and skipped — the same tolerance
    /// <c>OpenSolutionAsync</c> applies — and only an unreadable solution file is an error.
    /// </summary>
    private async Task<LoadedSolution> AddSolutionProjectsAsync(
        MSBuildWorkspace workspace, string solutionPath, CancellationToken ct)
    {
        IReadOnlyList<string> declared;
        try
        {
            declared = await SolutionFileReader.GetProjectPathsAsync(solutionPath, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to read solution: {SolutionPath}", solutionPath);
            return new LoadedSolution(solutionPath, [], ex.Message);
        }

        var added = 0;
        var loaded = LoadedProjectPaths(workspace.CurrentSolution);
        foreach (var projectPath in declared)
        {
            if (loaded.Contains(projectPath))
                continue;

            if (!File.Exists(projectPath))
            {
                logger.LogWarning("Project in {SolutionPath} not found: {ProjectPath}", solutionPath, projectPath);
                continue;
            }

            try
            {
                await workspace.OpenProjectAsync(projectPath, cancellationToken: ct);
                added++;
                // The open may also have pulled in this solution's other projects as references.
                loaded = LoadedProjectPaths(workspace.CurrentSolution);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to load project {ProjectPath} from {SolutionPath}", projectPath, solutionPath);
            }
        }

        logger.LogInformation("Added {SolutionPath}: {Declared} projects declared, {Added} newly loaded",
            solutionPath, declared.Count, added);
        return new LoadedSolution(solutionPath, declared, null);
    }

    private static HashSet<string> LoadedProjectPaths(Solution solution) =>
        solution.Projects
            .Select(p => p.FilePath)
            .OfType<string>()
            .Select(SolutionFileReader.NormalizePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, List<string>> BuildMembership(IEnumerable<LoadedSolution> solutions)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var solution in solutions)
        {
            foreach (var projectPath in solution.ProjectPaths)
            {
                if (!map.TryGetValue(projectPath, out var names))
                    map[projectPath] = names = [];
                if (!names.Contains(solution.Name, StringComparer.OrdinalIgnoreCase))
                    names.Add(solution.Name);
            }
        }
        return map;
    }

    /// <summary>
    /// Deepest directory containing every solution. Falls back to the primary solution's
    /// directory when they share no root (different drives).
    /// </summary>
    internal static string? ComputeRootDirectory(IReadOnlyList<string> solutionPaths)
    {
        var directories = solutionPaths
            .Select(p => Path.GetDirectoryName(Path.GetFullPath(p)))
            .OfType<string>()
            .ToList();
        if (directories.Count == 0) return null;

        var common = directories[0];
        foreach (var directory in directories.Skip(1))
        {
            while (common is not null && !IsSameOrUnder(directory, common))
                common = Path.GetDirectoryName(common);
        }

        return common ?? directories[0];
    }

    private static bool IsSameOrUnder(string path, string root)
    {
        var trimmedRoot = Path.TrimEndingDirectorySeparator(root);
        return path.Equals(trimmedRoot, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(trimmedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || (Path.EndsInDirectorySeparator(root) && path.StartsWith(root, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Absolute paths of every document in the solution, captured at load. Used to size the
    /// repository without re-walking the tree.
    /// </summary>
    internal IReadOnlyCollection<string> KnownDocumentPaths => _knownDocumentPaths.Keys.ToList();

    /// <summary>
    /// Trims an absolute file path to one relative to the solution directory, forward-slashed,
    /// for token-efficient MCP responses. Falls back to the forward-slashed input when the path
    /// is empty, the root is unknown, or it lies outside the solution tree.
    /// </summary>
    public string ToRelativePath(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return filePath;

        var root = SolutionDirectory;
        if (root is null) return filePath.Replace('\\', '/');

        try
        {
            var relative = Path.GetRelativePath(root, filePath);
            // GetRelativePath returns the input unchanged if it can't relativize (e.g. different drive).
            return relative.Replace('\\', '/');
        }
        catch (ArgumentException)
        {
            return filePath.Replace('\\', '/');
        }
    }

    /// <summary>
    /// Gets or creates a Compilation for the specified project. Thread-safe and cached.
    /// </summary>
    public async Task<Compilation?> GetCompilationAsync(ProjectId projectId, CancellationToken ct = default)
    {
        if (_compilationCache.TryGetValue(projectId, out var cached))
        {
            _cacheAccessOrder[projectId] = Interlocked.Increment(ref _accessCounter);
            return cached;
        }

        var project = _solution?.GetProject(projectId);
        if (project is null)
            return null;

        var compilation = await project.GetCompilationAsync(ct);
        if (compilation is not null)
        {
            EvictIfNeeded();
            _compilationCache[projectId] = compilation;
            _cacheAccessOrder[projectId] = Interlocked.Increment(ref _accessCounter);
        }

        return compilation;
    }

    private void EvictIfNeeded()
    {
        if (!IsLazyLoading || _compilationCache.Count < MaxCachedCompilations)
            return;

        // Evict least-recently-used entries until under limit
        var toEvict = _cacheAccessOrder
            .OrderBy(kvp => kvp.Value)
            .Take(_compilationCache.Count - MaxCachedCompilations + 1)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var projectId in toEvict)
        {
            _compilationCache.TryRemove(projectId, out _);
            _cacheAccessOrder.TryRemove(projectId, out _);
            logger.LogDebug("Evicted compilation cache for project {ProjectId}", projectId);
        }
    }

    /// <summary>
    /// Gets compilations for all projects. Lazy-loaded on demand.
    /// </summary>
    public async Task<IReadOnlyList<Compilation>> GetAllCompilationsAsync(CancellationToken ct = default)
    {
        if (_solution is null)
            return [];

        var compilations = new List<Compilation>();
        foreach (var projectId in _solution.ProjectIds)
        {
            var compilation = await GetCompilationAsync(projectId, ct);
            if (compilation is not null)
                compilations.Add(compilation);
        }

        return compilations;
    }

    /// <summary>
    /// Returns a status message suitable for MCP tool responses when the workspace is not ready.
    /// </summary>
    public string GetStatusMessage() => State switch
    {
        WorkspaceState.NotStarted => "Workspace has not been initialized. Waiting for solution path.",
        WorkspaceState.Loading when _solutionPaths.Count > 1 =>
            $"Workspace is loading {_solutionPaths.Count} solutions ({string.Join(", ", _solutionPaths.Select(Path.GetFileName))}). Please try again in a moment.",
        WorkspaceState.Loading => "Workspace is loading the solution. Please try again in a moment.",
        WorkspaceState.Error =>
            $"Workspace failed to load: {_errorMessage}. The server retries automatically on later calls.",
        WorkspaceState.Ready => "Workspace is ready.",
        _ => "Unknown workspace state."
    };

    /// <summary>
    /// Returns null when the workspace is ready; otherwise attempts one-shot discovery
    /// from MCP roots and returns a JSON status response if still not ready.
    /// When the workspace is ready, refreshes any documents that have changed on disk.
    /// Refresh/reload failures never escape as exceptions — tools always get either a
    /// ready workspace or a graceful status response, and a failed load of a known
    /// solution path is retried automatically (with a cooldown) on later calls.
    /// </summary>
    public async Task<string?> EnsureReadyOrStatusAsync(CancellationToken ct)
    {
        if (State == WorkspaceState.Ready)
        {
            try
            {
                await RefreshChangedDocumentsAsync(ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A failed refresh (e.g. a .csproj saved mid-write) must not surface as a
                // raw MCP error. LoadSolutionAsync already recorded the Error state; fall
                // through to the status response and let the retry path below recover.
                logger.LogWarning(ex, "Workspace refresh failed; will retry on a later call.");
            }

            if (State == WorkspaceState.Ready) return null;
        }

        // Retry a previously failed load of a known solution path (transient failures
        // such as mid-write project files). Cooldown prevents hammering a broken solution.
        if (State == WorkspaceState.Error && _solutionPaths.Count > 0 && TryClaimErrorRetry())
        {
            try
            {
                await LoadSolutionsAsync(_solutionPaths, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Workspace reload retry failed.");
            }

            if (State == WorkspaceState.Ready) return null;
        }

        // One-shot attempt to discover workspace from MCP roots
        if (Interlocked.CompareExchange(ref _rootsAttempted, 1, 0) == 0)
        {
            await TryInitializeFromRootsAsync(ct);
            if (State == WorkspaceState.Ready) return null;
        }

        return JsonSerializer.Serialize(new StatusResponse(State.ToString(), GetStatusMessage()));
    }

    private bool TryClaimErrorRetry()
    {
        var now = clock.GetUtcNow().UtcTicks;
        var last = Interlocked.Read(ref _lastErrorRetryTicks);
        if (now - last < ErrorRetryCooldownTicks) return false;
        return Interlocked.CompareExchange(ref _lastErrorRetryTicks, now, last) == last;
    }

    private async Task TryInitializeFromRootsAsync(CancellationToken ct)
    {
        try
        {
            var server = Services?.GetService(typeof(McpServer)) as McpServer;
            if (server is null) return;

            var rootsResult = await server.RequestRootsAsync(new ListRootsRequestParams(), ct);

            // A solution from EACH root (every solution per root with --all-solutions), so a
            // multi-root workspace spanning several services loads them all into one workspace.
            var solutionPaths = new List<string>();
            foreach (var root in rootsResult.Roots)
            {
                if (!Uri.TryCreate(root.Uri, UriKind.Absolute, out var uri)) continue;
                var localPath = uri.LocalPath;
                if (!Directory.Exists(localPath)) continue;

                foreach (var solutionPath in SolutionDiscovery.ScanDirectory(localPath, DiscoverAllSolutions))
                {
                    if (solutionPaths.Contains(solutionPath, StringComparer.OrdinalIgnoreCase)) continue;
                    logger.LogInformation("Discovered solution from MCP roots: {SolutionPath}", solutionPath);
                    solutionPaths.Add(solutionPath);
                }
            }

            if (solutionPaths.Count > 0)
            {
                await LoadSolutionsAsync(solutionPaths, ct);
                return;
            }

            logger.LogWarning("MCP roots available but no .sln/.slnx found in any root directory.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to discover workspace from MCP roots.");
        }
    }

    private async Task WarmCompilationsAsync(CancellationToken ct)
    {
        if (_solution is null) return;

        logger.LogInformation("Warming compilations for {Count} projects...", _solution.ProjectIds.Count);

        await Parallel.ForEachAsync(
            _solution.ProjectIds,
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
            async (projectId, token) => await GetCompilationAsync(projectId, token));

        logger.LogInformation("All compilations warmed.");
    }

    /// <summary>
    /// Records the last-write time of every document and build file in the solution.
    /// Called once after solution load to establish a baseline for staleness detection.
    /// Stores file paths and project IDs alongside timestamps to avoid Roslyn lookups
    /// during the per-call refresh hot path.
    ///
    /// Build files (.csproj plus the Directory.* files that feed into them) additionally
    /// get a content hash. A reload is the most expensive thing this server does, and a
    /// timestamp alone cannot tell a real edit from a rewrite with identical content —
    /// which is what a branch switch or a formatter produces across many files at once.
    /// </summary>
    private void SnapshotFileTimestamps()
    {
        if (_solution is null) return;

        _knownDocuments.Clear();
        _buildFiles.Clear();
        _knownDocumentPaths.Clear();

        foreach (var projectId in _solution.ProjectIds)
        {
            var project = _solution.GetProject(projectId);
            if (project is null) continue;

            if (project.FilePath is not null)
                TrackBuildFile(project.FilePath);

            foreach (var document in project.Documents)
            {
                if (document.FilePath is null) continue;

                var writeTime = File.GetLastWriteTimeUtc(document.FilePath);
                // GetLastWriteTimeUtc returns year 1601 for non-existent files
                if (writeTime.Year < 1900) continue;

                _knownDocuments[document.Id] = new DocumentInfo(document.FilePath, projectId, writeTime);
                _knownDocumentPaths[document.FilePath] = 0;
            }
        }

        foreach (var sharedFile in EnumerateSharedBuildFiles())
            TrackBuildFile(sharedFile);

        logger.LogInformation("Captured timestamps for {Count} documents across {BuildFileCount} build files",
            _knownDocuments.Count, _buildFiles.Count);
    }

    /// <summary>
    /// Directory-scoped MSBuild files change the compilation of every project beneath
    /// them — a bumped version in Directory.Packages.props is as significant as a csproj
    /// edit — but they are not documents, so nothing else in the workspace notices them.
    /// </summary>
    private IEnumerable<string> EnumerateSharedBuildFiles()
    {
        string[] names =
        [
            "Directory.Build.props",
            "Directory.Build.targets",
            "Directory.Packages.props",
            "global.json",
            "nuget.config"
        ];

        // Each solution's own directory: with several solutions, each service typically
        // carries its own Directory.Packages.props.
        var roots = _solutionPaths
            .Select(Path.GetDirectoryName)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;

            foreach (var name in names)
            {
                var path = Path.Combine(root, name);
                if (File.Exists(path)) yield return path;
            }
        }
    }

    private void TrackBuildFile(string path)
    {
        var writeTime = File.GetLastWriteTimeUtc(path);
        if (writeTime.Year < 1900) return;

        _buildFiles[path] = new BuildFileInfo(writeTime, ComputeHash(path));
    }

    /// <summary>
    /// Content hash of a build file, or null when it cannot be read. A null hash compares
    /// unequal to every other value, so an unreadable file is treated as changed rather
    /// than silently assumed stable.
    /// </summary>
    private static string? ComputeHash(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private readonly record struct DocumentInfo(string FilePath, ProjectId ProjectId, DateTime LastWriteUtc);

    /// <summary>A csproj or Directory.* file, tracked by both timestamp and content.</summary>
    private readonly record struct BuildFileInfo(DateTime LastWriteUtc, string? Hash);

    /// <summary>
    /// Refreshes the workspace to reflect on-disk changes. Uses tiered cooldowns to
    /// keep per-call overhead low: .csproj + document timestamps every 5s, full
    /// directory scan for new files every 60s.
    /// </summary>
    public async Task RefreshChangedDocumentsAsync(CancellationToken ct = default)
    {
        if (_solution is null || _solutionPaths.Count == 0) return;

        var now = clock.GetUtcNow().UtcTicks;

        var lastRefresh = Interlocked.Read(ref _lastRefreshTicks);
        if (now - lastRefresh < RefreshCooldownTicks) return;
        Interlocked.Exchange(ref _lastRefreshTicks, now);

        // Phase 1: check build files (one stat each, hashed only when the stat moved)
        var changedBuildFiles = GetChangedBuildFiles();
        if (changedBuildFiles.Count > 0)
        {
            logger.LogInformation(
                "Build files changed ({Files}). Reloading solution.",
                string.Join(", ", changedBuildFiles.Select(Path.GetFileName)));

            await LoadSolutionsAsync(_solutionPaths, ct);
            return;
        }

        // Phase 2: act on whatever the last background scan found. The scan itself walks
        // every project directory, so it never runs in-band — a tool call pays an
        // interlocked read here, and the reload only once the scan has already found
        // something. The finding is at most one cooldown stale, which is the same
        // staleness the in-band version had.
        if (Interlocked.Exchange(ref _newSourceFilesPending, 0) == 1)
        {
            logger.LogInformation("New source files detected. Reloading solution.");
            await LoadSolutionsAsync(_solutionPaths, ct);
            return;
        }

        var lastStructural = Interlocked.Read(ref _lastStructuralScanTicks);
        if (now - lastStructural >= StructuralScanCooldownTicks)
        {
            Interlocked.Exchange(ref _lastStructuralScanTicks, now);
            StartStructuralScan();
        }

        // Phase 3: collect changed documents without holding the lock (stat calls are
        // the expensive part — keeping them lock-free avoids blocking concurrent tool calls).
        var changed = new List<(DocumentId Id, DocumentInfo Info)>();
        foreach (var (docId, info) in _knownDocuments)
        {
            var currentWriteTime = File.GetLastWriteTimeUtc(info.FilePath);
            if (currentWriteTime > info.LastWriteUtc)
                changed.Add((docId, info with { LastWriteUtc = currentWriteTime }));
        }

        if (changed.Count == 0) return;

        // Apply mutations under lock.
        await _writeLock.WaitAsync(ct);
        try
        {
            foreach (var (docId, info) in changed)
            {
                var text = await File.ReadAllTextAsync(info.FilePath, ct);
                var sourceText = Microsoft.CodeAnalysis.Text.SourceText.From(text);
                _solution = _solution.WithDocumentText(docId, sourceText);
                _knownDocuments[docId] = info;

                _compilationCache.TryRemove(info.ProjectId, out _);
                _cacheAccessOrder.TryRemove(info.ProjectId, out _);

                logger.LogDebug("Refreshed changed document: {Path}", info.FilePath);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Build files whose content actually differs from the last load. The timestamp is a
    /// cheap pre-filter — one stat per file — and only files that moved are hashed, so the
    /// steady-state cost is unchanged while a no-op rewrite costs one hash instead of a
    /// full solution reload.
    /// </summary>
    private List<string> GetChangedBuildFiles()
    {
        var changed = new List<string>();

        foreach (var (path, known) in _buildFiles)
        {
            // GetLastWriteTimeUtc returns year 1601 for missing files — always < lastKnown
            if (File.GetLastWriteTimeUtc(path) <= known.LastWriteUtc)
                continue;

            var hash = ComputeHash(path);
            if (hash is not null && hash == known.Hash)
            {
                // Rewritten with identical content: refresh the timestamp so the next poll
                // short-circuits at the stat, and skip the reload.
                _buildFiles[path] = known with { LastWriteUtc = File.GetLastWriteTimeUtc(path) };
                continue;
            }

            changed.Add(path);
        }

        return changed;
    }

    /// <summary>
    /// Kicks off the directory walk on a background thread, at most one at a time. Fire and
    /// forget by design: the caller is servicing a tool request and must not wait on it.
    /// </summary>
    private void StartStructuralScan()
    {
        if (Interlocked.CompareExchange(ref _structuralScanRunning, 1, 0) != 0)
            return;

        _structuralScan = Task.Run(() =>
        {
            try
            {
                if (HasNewSourceFiles(_backgroundCts.Token))
                    Interlocked.Exchange(ref _newSourceFilesPending, 1);
            }
            catch (OperationCanceledException)
            {
                // Shutting down.
            }
            catch (Exception ex)
            {
                // A transient IO error during the walk costs one missed scan, not a failed
                // tool call — the next cooldown schedules another.
                logger.LogWarning(ex, "Structural scan failed; will retry on the next cooldown.");
            }
            finally
            {
                Interlocked.Exchange(ref _structuralScanRunning, 0);
            }
        }, _backgroundCts.Token);
    }

    /// <summary>
    /// Awaits the in-flight structural scan. Test seam — nothing in the request path
    /// should ever wait on this.
    /// </summary>
    internal Task WaitForStructuralScanAsync() => _structuralScan;

    private bool HasNewSourceFiles(CancellationToken ct)
    {
        // Captured once: a concurrent reload can swap the field mid-walk.
        var solution = _solution;
        if (solution is null) return false;

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
        };

        foreach (var projectId in solution.ProjectIds)
        {
            ct.ThrowIfCancellationRequested();

            var project = solution.GetProject(projectId);
            if (project?.FilePath is null) continue;

            var projectDir = Path.GetDirectoryName(project.FilePath);
            if (projectDir is null || !Directory.Exists(projectDir)) continue;

            // Pre-compute bin/obj prefixes to avoid per-file Path.GetRelativePath allocation
            var sep = Path.DirectorySeparatorChar;
            var binPrefix = $"{projectDir}{sep}bin{sep}";
            var objPrefix = $"{projectDir}{sep}obj{sep}";

            foreach (var file in Directory.EnumerateFiles(projectDir, "*.cs", options))
            {
                ct.ThrowIfCancellationRequested();

                if (file.StartsWith(binPrefix, StringComparison.OrdinalIgnoreCase) ||
                    file.StartsWith(objPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!_knownDocumentPaths.ContainsKey(file))
                    return true;
            }
        }

        return false;
    }

    public void Dispose()
    {
        _backgroundCts.Cancel();

        try
        {
            // Bounded so shutdown cannot block on a large directory walk.
            _structuralScan.Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception)
        {
            // Cancellation or a failed scan; nothing left to salvage during teardown.
        }

        _backgroundCts.Dispose();
        _workspace?.Dispose();
        _writeLock.Dispose();
    }
}
