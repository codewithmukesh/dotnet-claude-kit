using System.Collections.Concurrent;
using System.IO.Enumeration;
using System.Security.Cryptography;
using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CWM.RoslynNavigator;

/// <summary>
/// Manages the MSBuildWorkspace lifecycle: loading, compilation warm-up, and on-demand refresh.
/// File watching is intentionally avoided — on Linux, recursive FileSystemWatcher creates
/// one inotify watch per subdirectory, quickly exhausting the kernel limit for large solutions.
/// Instead, documents are refreshed on demand when tools are invoked.
/// </summary>
public sealed class WorkspaceManager(ILogger<WorkspaceManager> logger, TimeProvider clock) : IDisposable
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<DocumentId, TrackedDocument> _knownDocuments = new();
    private readonly ConcurrentDictionary<string, BuildFileInfo> _buildFiles = new();
    private readonly ConcurrentDictionary<string, byte> _knownDocumentPaths = new(StringComparer.OrdinalIgnoreCase);
    private int _loadCount;
    private readonly CancellationTokenSource _backgroundCts = new();
    private int _structuralScanRunning;
    // New-file tracking: candidates found by the background walk, files known not to be
    // compiled (kept across reloads so they are never re-classified), and per-project answers
    // to "does the SDK default glob alone decide what this project compiles?".
    private readonly ConcurrentDictionary<string, byte> _pendingNewFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _excludedSourceFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _defaultGlobByProject = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, ProjectId[]> _projectsByDirectory = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _lastLoadStartedUtc;
    private Task _structuralScan = Task.CompletedTask;
    private CancellationTokenSource? _warmUpCts;
    private Task _warmUp = Task.CompletedTask;
    private int _rootsAttempted; // 0 = not tried, 1 = tried
    private long _lastRefreshTicks;
    private long _lastStructuralScanTicks;
    private long _lastErrorRetryTicks;
    private static readonly long RefreshCooldownTicks = TimeSpan.FromSeconds(5).Ticks;
    private static readonly long StructuralScanCooldownTicks = TimeSpan.FromSeconds(60).Ticks;
    private static readonly long ErrorRetryCooldownTicks = TimeSpan.FromSeconds(30).Ticks;

    private MSBuildWorkspace? _workspace;
    private volatile Solution? _solution;
    private IReadOnlyList<string> _solutionPaths = [];
    private IReadOnlyList<LoadedSolution> _loadedSolutions = [];
    private Dictionary<string, List<string>> _projectSolutions = new(StringComparer.OrdinalIgnoreCase);
    private string? _rootDirectory;
    private string? _errorMessage;
    private int _snapshotVersion;
    private readonly WorkspaceHealth _health = new(clock);

    public WorkspaceState State { get; private set; } = WorkspaceState.NotStarted;
    public string? ErrorMessage => _errorMessage;
    public int ProjectCount => _solution?.ProjectIds.Count ?? 0;

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
    /// How many times the solution has been loaded. Test seam for verifying that a
    /// no-op build-file rewrite does not trigger a reload.
    /// </summary>
    internal int LoadCount => _loadCount;

    /// <summary>
    /// Bumped whenever the solution snapshot changes — a load, or an in-place document
    /// refresh. Lets a caller tell whether two answers came from the same snapshot.
    /// </summary>
    public int SnapshotVersion => Volatile.Read(ref _snapshotVersion);

    /// <summary>Load and warm-up problems recorded for the current load.</summary>
    internal WorkspaceHealth Health => _health;

    /// <summary>Projects that loaded or compiled incompletely in the current snapshot.</summary>
    internal IReadOnlyList<ProjectProblem> GetProjectProblems() =>
        _health.GetProblems(_solution, _loadedSolutions, SnapshotVersion);

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
    ///
    /// <para>Returns — and the workspace reports Ready — as soon as the solutions are open.
    /// Compilations warm up in the background; a tool call that needs one before warm-up
    /// reaches it awaits the same in-flight build, since Roslyn builds one compilation per
    /// project per snapshot no matter how many callers ask.</para>
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
            // The previous snapshot is about to be discarded; compiling it further is waste.
            CancelWarmUp();
            _solutionPaths = [.. solutionPaths];
            _rootDirectory = ComputeRootDirectory(solutionPaths);
            _errorMessage = null;

            logger.LogInformation("Loading solution: {SolutionPath}", string.Join(", ", solutionPaths));

            // Anything on disk before MSBuild evaluates and still not a document afterwards was
            // excluded by the project; files created from here on are genuinely new.
            _lastLoadStartedUtc = clock.GetUtcNow();
            _pendingNewFiles.Clear();

            // Dispose previous workspace to avoid leaking Roslyn Solution snapshots
            _workspace?.Dispose();

            // Shadow-copying host: analyzers and generators load from temp copies, never from
            // ~/.nuget/packages, so this long-lived process cannot block `dotnet restore` (#25).
            _workspace = MSBuildWorkspace.Create(ShadowCopyAnalyzerService.HostServices);
            _health.Reset();
            _workspace.RegisterWorkspaceFailedHandler(args =>
            {
                // Logged and kept: a failure here means a project loaded partially or not at
                // all, which get_workspace_status and empty results must be able to report.
                _health.RecordDiagnostic(args.Diagnostic.Kind, args.Diagnostic.Message);
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
            Interlocked.Increment(ref _snapshotVersion);

            if (solutionPaths.Count == 1)
                logger.LogInformation("Solution loaded: {ProjectCount} projects", _solution.ProjectIds.Count);
            else
                logger.LogInformation("{SolutionCount} solutions loaded: {ProjectCount} distinct projects",
                    solutionPaths.Count, _solution.ProjectIds.Count);

            SnapshotFileTimestamps();
            State = WorkspaceState.Ready;
            StartWarmUp(_solution);
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
    /// Gets the Compilation for the specified project in the current solution snapshot.
    /// Deliberately uncached here: the Solution already holds every compilation it has built
    /// (Roslyn's compilation tracker keeps them alive), and after a document edit it forks
    /// them incrementally — including for projects that reference the edited one. A second
    /// cache keyed on ProjectId cannot tell snapshots apart, so it served dependents a
    /// compilation built against the previous version of their dependency.
    /// </summary>
    public Task<Compilation?> GetCompilationAsync(ProjectId projectId, CancellationToken ct = default) =>
        _solution?.GetProject(projectId)?.GetCompilationAsync(ct) ?? Task.FromResult<Compilation?>(null);

    /// <summary>
    /// Gets compilations for all projects of the current snapshot, in project order. Projects
    /// still uncompiled are built concurrently.
    /// </summary>
    public async Task<IReadOnlyList<Compilation>> GetAllCompilationsAsync(CancellationToken ct = default)
    {
        var solution = _solution;
        if (solution is null)
            return [];

        var compilations = await CompileAllAsync(solution, ct);
        return [.. compilations.OfType<Compilation>()];
    }

    /// <summary>
    /// Compiles every project of <paramref name="solution"/> with up to one build per core.
    /// Measured on a 39-project solution: 12.2s serial, 3.6s at 24-way. Projects are
    /// scheduled leaves-first so a worker rarely blocks on a dependency nobody has started,
    /// and results are returned in <see cref="Solution.ProjectIds"/> order regardless.
    /// </summary>
    private static async Task<Compilation?[]> CompileAllAsync(
        Solution solution, CancellationToken ct, WorkspaceHealth? health = null, int generation = 0)
    {
        var projectIds = solution.ProjectIds;
        var results = new Compilation?[projectIds.Count];
        var index = new Dictionary<ProjectId, int>(projectIds.Count);
        for (var i = 0; i < projectIds.Count; i++)
            index[projectIds[i]] = i;

        var schedule = solution.GetProjectDependencyGraph().GetTopologicallySortedProjects(ct);

        await Parallel.ForEachAsync(
            schedule,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ct },
            async (projectId, token) =>
            {
                if (!index.TryGetValue(projectId, out var i) || solution.GetProject(projectId) is not { } project)
                    return;

                if (health is null)
                {
                    results[i] = await project.GetCompilationAsync(token);
                    return;
                }

                // Warm-up: one project failing must not stop the rest, and the failure is
                // recorded so a later empty answer can be explained.
                try
                {
                    results[i] = await project.GetCompilationAsync(token);
                    health.ProjectCompiled(generation, projectId, results[i], null);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    health.ProjectCompiled(generation, projectId, null, ex);
                }
            });

        return results;
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

    /// <summary>
    /// Compiles every project of the freshly loaded snapshot in the background, for every
    /// solution size. The workspace is already Ready: tool calls racing the warm-up share its
    /// in-flight builds rather than duplicating them. A reload or dispose cancels it.
    /// </summary>
    private void StartWarmUp(Solution solution)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_backgroundCts.Token);
        _warmUpCts = cts;
        var token = cts.Token;

        var generation = _health.Generation;
        _health.WarmUpStarted(generation, solution.ProjectIds.Count);

        _warmUp = Task.Run(async () =>
        {
            var started = clock.GetTimestamp();
            try
            {
                logger.LogInformation("Warming compilations for {Count} projects in the background...",
                    solution.ProjectIds.Count);
                await CompileAllAsync(solution, token, _health, generation);
                _health.WarmUpFinished(generation, "completed");
                logger.LogInformation("All compilations warmed in {Elapsed} ms.",
                    (long)clock.GetElapsedTime(started).TotalMilliseconds);
            }
            catch (OperationCanceledException)
            {
                // Superseded by a reload, or shutting down.
                _health.WarmUpFinished(generation, "cancelled");
            }
            catch (Exception ex)
            {
                // Warm-up is an optimization: a project that fails to compile here fails the
                // same way, with a proper error, when a tool asks for it.
                _health.WarmUpFinished(generation, "failed");
                logger.LogWarning(ex, "Background compilation warm-up failed.");
            }
        }, CancellationToken.None);
    }

    private void CancelWarmUp()
    {
        var cts = _warmUpCts;
        if (cts is null) return;

        _warmUpCts = null;
        cts.Cancel();
        // Dispose only once the warm-up has observed the cancellation.
        _warmUp.ContinueWith(_ => cts.Dispose(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>
    /// Awaits the in-flight background warm-up. Test seam — nothing in the request path
    /// should ever wait on this.
    /// </summary>
    internal Task WaitForWarmUpAsync() => _warmUp;

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
        _defaultGlobByProject.Clear();
        var projectsByDirectory = new Dictionary<string, List<ProjectId>>(StringComparer.OrdinalIgnoreCase);

        foreach (var projectId in _solution.ProjectIds)
        {
            var project = _solution.GetProject(projectId);
            if (project is null) continue;

            if (project.FilePath is not null)
            {
                TrackBuildFile(project.FilePath);

                if (Path.GetDirectoryName(project.FilePath) is { } projectDir)
                {
                    if (!projectsByDirectory.TryGetValue(projectDir, out var ids))
                        projectsByDirectory[projectDir] = ids = [];
                    ids.Add(projectId);
                }
            }

            foreach (var document in project.Documents)
            {
                if (document.FilePath is null) continue;

                var writeTime = File.GetLastWriteTimeUtc(document.FilePath);
                // GetLastWriteTimeUtc returns year 1601 for non-existent files
                if (writeTime.Year < 1900) continue;

                _knownDocuments[document.Id] = new TrackedDocument(
                    document.FilePath, Path.GetDirectoryName(document.FilePath) ?? string.Empty, writeTime);
                _knownDocumentPaths[document.FilePath] = 0;
            }
        }

        foreach (var sharedFile in EnumerateSharedBuildFiles())
            TrackBuildFile(sharedFile);

        _projectsByDirectory = projectsByDirectory.ToDictionary(
            pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.OrdinalIgnoreCase);

        // A file a csproj edit has since pulled in is a document now, not an exclusion.
        foreach (var path in _excludedSourceFiles.Keys)
        {
            if (_knownDocumentPaths.ContainsKey(path))
                _excludedSourceFiles.TryRemove(path, out _);
        }

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

    /// <summary>A tracked document: its path, the directory holding it, and its last-seen write time.</summary>
    private readonly record struct TrackedDocument(string FilePath, string Directory, DateTime LastWriteUtc);

    /// <summary>A csproj or Directory.* file, tracked by both timestamp and content.</summary>
    private readonly record struct BuildFileInfo(DateTime LastWriteUtc, string? Hash);

    /// <summary>
    /// Refreshes the workspace to reflect on-disk changes. Uses tiered cooldowns to
    /// keep per-call overhead low: build files plus one listing per document directory
    /// (edits, deletions, and new files beside existing ones) every 5s, and a full recursive
    /// walk for new files in new folders every 60s, in the background.
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

        // Phase 2: the recursive walk for files in folders no document lives in yet never
        // runs in-band; it reports into _pendingNewFiles for a later call to act on.
        var lastStructural = Interlocked.Read(ref _lastStructuralScanTicks);
        if (now - lastStructural >= StructuralScanCooldownTicks)
        {
            Interlocked.Exchange(ref _lastStructuralScanTicks, now);
            StartStructuralScan();
        }

        // Phase 3: collect changed, deleted, and new documents without holding the lock (the
        // directory reads are the expensive part — keeping them lock-free avoids blocking
        // concurrent tool calls).
        var scan = ScanKnownDocuments(ct);
        foreach (var path in _pendingNewFiles.Keys)
        {
            _pendingNewFiles.TryRemove(path, out _);
            scan.NewFiles.Add(path);
        }

        var additions = ClassifyNewFiles(scan.NewFiles, out var needReload);
        if (needReload.Count > 0)
        {
            logger.LogInformation(
                "New source files in projects with custom compile items ({Files}). Reloading solution.",
                string.Join(", ", needReload.Select(Path.GetFileName)));

            await LoadSolutionsAsync(_solutionPaths, ct);
            return;
        }

        if (scan.Changed.Count == 0 && scan.Deleted.Count == 0 && additions.Count == 0) return;

        // Apply mutations under lock.
        await _writeLock.WaitAsync(ct);
        try
        {
            await ApplyDocumentChangesAsync(scan, additions, ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private sealed record DocumentScan(
        List<(DocumentId Id, TrackedDocument Info)> Changed,
        List<(DocumentId Id, TrackedDocument Info)> Deleted,
        HashSet<string> NewFiles);

    /// <summary>A new source file and every project instance (one per target framework) that compiles it.</summary>
    private sealed record NewDocument(string FilePath, string ProjectDirectory, ProjectId[] Projects);

    /// <summary>
    /// Compares every tracked document against its directory listing. One enumeration per
    /// directory returns the timestamps of all its files, which is far cheaper than a stat
    /// per document (measured on 3,750 documents: 118 ms of stats per call, 57 ms of
    /// sequential enumeration), and the directories are read in parallel. A document whose
    /// directory no longer lists it has been deleted; a .cs file the listing has that no
    /// document or exclusion accounts for is a candidate new document.
    /// </summary>
    private DocumentScan ScanKnownDocuments(CancellationToken ct)
    {
        var byDirectory = new Dictionary<string, List<KeyValuePair<DocumentId, TrackedDocument>>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _knownDocuments)
        {
            if (!byDirectory.TryGetValue(entry.Value.Directory, out var documents))
                byDirectory[entry.Value.Directory] = documents = [];
            documents.Add(entry);
        }

        var scan = new DocumentScan([], [], new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var gate = new Lock();

        Parallel.ForEach(
            byDirectory,
            new ParallelOptions { MaxDegreeOfParallelism = ScanParallelism, CancellationToken = ct },
            pair =>
            {
                var onDisk = ReadDirectoryTimestamps(pair.Key);
                if (onDisk is null) return; // Unreadable right now: infer nothing, retry next call.

                foreach (var (id, info) in pair.Value)
                {
                    if (!onDisk.TryGetValue(info.FilePath, out var writeTime))
                    {
                        lock (gate) scan.Deleted.Add((id, info));
                    }
                    else if (writeTime > info.LastWriteUtc)
                    {
                        lock (gate) scan.Changed.Add((id, info with { LastWriteUtc = writeTime }));
                    }
                }

                foreach (var path in onDisk.Keys)
                {
                    if (IsUntrackedSourceFile(path))
                    {
                        lock (gate) scan.NewFiles.Add(path);
                    }
                }
            });

        return scan;
    }

    private bool IsUntrackedSourceFile(string path) =>
        path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
        && !_knownDocumentPaths.ContainsKey(path)
        && !_excludedSourceFiles.ContainsKey(path);

    /// <summary>Directory reads are IO-bound; a handful in flight saturates a local disk.</summary>
    private static int ScanParallelism => Math.Clamp(Environment.ProcessorCount, 1, 8);

    private static readonly EnumerationOptions DirectoryListing = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = 0
    };

    /// <summary>
    /// Last-write times of the files directly in <paramref name="directory"/>, keyed by full
    /// path. Empty when the directory itself is gone (every document in it was deleted);
    /// null when it exists but cannot be read, so a transient failure never reads as a
    /// deletion.
    /// </summary>
    private static Dictionary<string, DateTime>? ReadDirectoryTimestamps(string directory)
    {
        var result = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var entries = new FileSystemEnumerable<(string Path, DateTime LastWriteUtc)>(
                directory,
                (ref FileSystemEntry entry) => (entry.ToFullPath(), entry.LastWriteTimeUtc.UtcDateTime),
                DirectoryListing)
            {
                ShouldIncludePredicate = (ref FileSystemEntry entry) => !entry.IsDirectory
            };

            foreach (var (path, lastWriteUtc) in entries)
                result[path] = lastWriteUtc;

            return result;
        }
        catch (DirectoryNotFoundException)
        {
            return result;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Sorts candidate new files into those that can be added to the workspace directly —
    /// owned by a project that compiles them through the SDK default glob alone — and those
    /// only an MSBuild evaluation can place (<paramref name="needReload"/>). Files no project
    /// compiles (bin/obj, dot-folders, outside every project, or already on disk when MSBuild
    /// last evaluated and not included then) are remembered as excluded so they are never
    /// classified twice.
    /// </summary>
    private List<NewDocument> ClassifyNewFiles(IEnumerable<string> candidates, out List<string> needReload)
    {
        var additions = new List<NewDocument>();
        needReload = [];

        foreach (var path in candidates)
        {
            if (!IsUntrackedSourceFile(path) || !File.Exists(path)) continue;

            if (FindOwningProjects(path) is not { } owner
                || DefaultCompileGlob.IsDefaultExcluded(owner.Directory, path)
                || ExistedAtLastLoad(path))
            {
                _excludedSourceFiles[path] = 0;
                continue;
            }

            var solution = _solution;
            var allDefaultGlob = owner.Projects
                .Select(id => solution?.GetProject(id)?.FilePath)
                .All(projectPath => projectPath is not null
                    && _defaultGlobByProject.GetOrAdd(projectPath, DefaultCompileGlob.AppliesTo));

            if (allDefaultGlob)
                additions.Add(new NewDocument(path, owner.Directory, owner.Projects));
            else
                needReload.Add(path);
        }

        return additions;
    }

    /// <summary>
    /// The projects whose directory is the deepest one containing <paramref name="path"/>,
    /// across every loaded solution. A multi-targeted project contributes one instance per
    /// target framework; all of them compile the file.
    /// </summary>
    private (string Directory, ProjectId[] Projects)? FindOwningProjects(string path)
    {
        var projectsByDirectory = _projectsByDirectory;
        for (var dir = Path.GetDirectoryName(path); dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (projectsByDirectory.TryGetValue(dir, out var projects))
                return (dir, projects);
        }

        return null;
    }

    /// <summary>
    /// A .cs file that was already on disk when MSBuild last evaluated the projects, yet is
    /// not a document, was excluded by the project itself (a Compile Remove, a nested
    /// project's folder, ...). Re-evaluating would only exclude it again. Creation time is
    /// part of the check because a copy keeps the source's write time.
    /// </summary>
    private bool ExistedAtLastLoad(string path)
    {
        var loadStarted = _lastLoadStartedUtc.UtcDateTime;
        return File.GetLastWriteTimeUtc(path) < loadStarted && File.GetCreationTimeUtc(path) < loadStarted;
    }

    /// <summary>
    /// Applies a scan to the current snapshot. Must hold <see cref="_writeLock"/>. Every id is
    /// re-checked against the snapshot: a reload between the lock-free scan and here mints new
    /// ids, and the stale ones must not be applied.
    /// </summary>
    private async Task ApplyDocumentChangesAsync(DocumentScan scan, List<NewDocument> additions, CancellationToken ct)
    {
        var solution = _solution!;

        foreach (var (docId, info) in scan.Changed)
        {
            if (!solution.ContainsDocument(docId)) continue;

            string text;
            try
            {
                text = await File.ReadAllTextAsync(info.FilePath, ct);
            }
            catch (FileNotFoundException)
            {
                continue; // Deleted since the scan; the next scan reports it as such.
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue; // Locked mid-save; the timestamp is not advanced, so it is retried.
            }

            solution = solution.WithDocumentText(docId, SourceText.From(text));
            _knownDocuments[docId] = info;
            logger.LogDebug("Refreshed changed document: {Path}", info.FilePath);
        }

        foreach (var (docId, info) in scan.Deleted)
        {
            // Re-checked under the lock: an editor that saves by delete-and-rename leaves the
            // file briefly missing, and dropping it then would hide its symbols until the next
            // scan noticed the "new" file.
            if (!solution.ContainsDocument(docId) || File.Exists(info.FilePath)) continue;

            solution = solution.RemoveDocument(docId);
            _knownDocuments.TryRemove(docId, out _);
            logger.LogInformation("Removed deleted document: {Path}", info.FilePath);
        }

        if (scan.Deleted.Count > 0)
            RebuildKnownDocumentPaths();

        foreach (var addition in additions)
            solution = AddNewDocument(solution, addition);

        if (!ReferenceEquals(solution, _solution))
            Interlocked.Increment(ref _snapshotVersion);
        _solution = solution;
    }

    /// <summary>
    /// Adds a new file to each project instance that compiles it — the incremental
    /// alternative to a full reload, which costs as much as the initial load. The text loads
    /// lazily from disk, exactly as MSBuildWorkspace loads every other document.
    /// </summary>
    private Solution AddNewDocument(Solution solution, NewDocument addition)
    {
        var path = addition.FilePath;
        if (_knownDocumentPaths.ContainsKey(path) || !File.Exists(path)) return solution;

        var writeTime = File.GetLastWriteTimeUtc(path);
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        string[] folders = [.. Path.GetRelativePath(addition.ProjectDirectory, directory)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(segment => segment is not ("" or "."))];

        var added = false;
        foreach (var projectId in addition.Projects)
        {
            if (!solution.ContainsProject(projectId)) continue;

            var documentId = DocumentId.CreateNewId(projectId, path);
            solution = solution.AddDocument(DocumentInfo.Create(
                documentId,
                Path.GetFileName(path),
                folders,
                SourceCodeKind.Regular,
                new FileTextLoader(path, defaultEncoding: null),
                path));

            _knownDocuments[documentId] = new TrackedDocument(path, directory, writeTime);
            added = true;
        }

        if (added)
        {
            _knownDocumentPaths[path] = 0;
            logger.LogInformation("Added new document without a reload: {Path}", path);
        }

        return solution;
    }

    /// <summary>
    /// A path can back several documents (a multi-targeted project loads once per target
    /// framework), so it is dropped only when no tracked document still uses it.
    /// </summary>
    private void RebuildKnownDocumentPaths()
    {
        var live = _knownDocuments.Values.Select(d => d.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in _knownDocumentPaths.Keys)
        {
            if (!live.Contains(path))
                _knownDocumentPaths.TryRemove(path, out _);
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
                foreach (var path in FindUntrackedSourceFiles(_backgroundCts.Token))
                    _pendingNewFiles[path] = 0;
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

    private List<string> FindUntrackedSourceFiles(CancellationToken ct)
    {
        var found = new List<string>();

        // Captured once: a concurrent reload can swap the field mid-walk.
        var solution = _solution;
        if (solution is null) return found;

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
        };

        var walked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in solution.Projects)
        {
            ct.ThrowIfCancellationRequested();

            var projectDir = Path.GetDirectoryName(project.FilePath);
            // One walk per directory, however many target frameworks load the project.
            if (projectDir is null || !walked.Add(projectDir) || !Directory.Exists(projectDir)) continue;

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

                if (IsUntrackedSourceFile(file))
                    found.Add(file);
            }
        }

        return found;
    }

    public void Dispose()
    {
        _backgroundCts.Cancel();

        try
        {
            // Bounded so shutdown cannot block on a large directory walk or a compilation
            // that is slow to observe cancellation.
            Task.WaitAll([_structuralScan, _warmUp], TimeSpan.FromSeconds(2));
        }
        catch (Exception)
        {
            // Cancellation or a failed scan; nothing left to salvage during teardown.
        }

        _warmUpCts?.Dispose();
        _backgroundCts.Dispose();
        _workspace?.Dispose();
        _writeLock.Dispose();
    }
}
