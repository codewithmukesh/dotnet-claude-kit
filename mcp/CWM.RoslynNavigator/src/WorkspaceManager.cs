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
    // _writeLock guards snapshot mutations (document refreshes, the swap after a load) and is
    // only ever held briefly. _loadLock serializes loads, which take as long as MSBuild needs;
    // holding it never blocks a tool call, because calls read the current snapshot lock-free.
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private readonly ConcurrentDictionary<DocumentId, TrackedDocument> _knownDocuments = new();
    private readonly ConcurrentDictionary<string, BuildFileInfo> _buildFiles = new();
    private readonly ConcurrentDictionary<string, byte> _knownDocumentPaths = new(PathComparison.Comparer);
    private int _loadCount;
    private readonly CancellationTokenSource _backgroundCts = new();
    private int _structuralScanRunning;
    // New-file tracking: candidates found by the background walk, files known not to be
    // compiled (kept across reloads so they are never re-classified), and per-project answers
    // to "does the SDK default glob alone decide what this project compiles?".
    private readonly ConcurrentDictionary<string, byte> _pendingNewFiles = new(PathComparison.Comparer);
    private readonly ConcurrentDictionary<string, byte> _excludedSourceFiles = new(PathComparison.Comparer);
    private readonly ConcurrentDictionary<string, bool> _defaultGlobByProject = new(PathComparison.Comparer);
    private Dictionary<string, ProjectId[]> _projectsByDirectory = new(PathComparison.Comparer);
    private DateTimeOffset _lastLoadStartedUtc;
    private Task _structuralScan = Task.CompletedTask;
    private CancellationTokenSource? _warmUpCts;
    private Task _warmUp = Task.CompletedTask;
    // MCP roots discovery: pending until tried, and again whenever the client reports that
    // its roots changed; retried after a cooldown while no root holds a solution.
    private int _rootsDirty = 1;
    private long _rootsRetryNotBeforeTicks = long.MaxValue;
    private volatile bool _solutionsFromRoots;
    private long _lastRefreshTicks;
    private long _lastStructuralScanTicks;
    private long _lastErrorRetryTicks;
    private static readonly long RefreshCooldownTicks = TimeSpan.FromSeconds(5).Ticks;
    private static readonly long StructuralScanCooldownTicks = TimeSpan.FromSeconds(60).Ticks;
    private static readonly long ErrorRetryCooldownTicks = TimeSpan.FromSeconds(30).Ticks;

    private WorkspaceGeneration? _generation;
    private volatile Solution? _solution;
    private IReadOnlyList<string> _solutionPaths = [];
    private volatile IReadOnlyList<LoadedSolution> _loadedSolutions = [];
    private Dictionary<string, List<string>> _projectSolutions = new(PathComparison.Comparer);
    private string? _rootDirectory;
    private string? _errorMessage;
    private int _snapshotVersion;
    private volatile WorkspaceHealth _health = new(clock);

    // Background reload: at most one in flight (_reloadRunning), why it started, and what
    // the last failed one said. A failed reload keeps the previous snapshot.
    private int _reloadRunning;
    private int _reloading;
    private volatile string? _reloadReason;
    private volatile string? _lastReloadError;
    private long _reloadRetryNotBeforeTicks;
    private Task _reload = Task.CompletedTask;
    private int _liveWorkspaces;
    private volatile bool _disposed;

    public WorkspaceState State { get; private set; } = WorkspaceState.NotStarted;
    public string? ErrorMessage => _errorMessage;
    public int ProjectCount => _solution?.ProjectIds.Count ?? 0;

    /// <summary>
    /// True while a reload is building the next snapshot in the background. Tool calls keep
    /// answering from the current one meanwhile; <see cref="StalenessWarning"/> says so.
    /// </summary>
    public bool IsReloading => Volatile.Read(ref _reloadRunning) == 1 || Volatile.Read(ref _reloading) == 1;

    /// <summary>Why the in-flight reload started, or null when none is running.</summary>
    public string? ReloadReason => IsReloading ? _reloadReason ?? "reload requested" : null;

    /// <summary>
    /// The error of the last background reload, cleared by the next successful load. While set,
    /// answers come from the snapshot before the failed reload.
    /// </summary>
    public string? LastReloadError => _lastReloadError;

    /// <summary>
    /// Why answers may lag the files on disk right now — a reload in flight, or a failed one —
    /// or null when the snapshot is current.
    /// </summary>
    public string? StalenessWarning
    {
        get
        {
            if (State != WorkspaceState.Ready) return null;
            if (ReloadReason is { } reason)
                return $"The workspace is reloading in the background ({reason}); this answer comes from the previous snapshot and may not reflect that change yet.";
            if (_lastReloadError is { } error)
                return $"The last workspace reload failed ({error}); this answer comes from the snapshot before it. Call get_workspace_status for details.";
            return null;
        }
    }

    /// <summary>
    /// Awaited after a load has opened its solutions and before it replaces the snapshot. Test
    /// seam: holds a reload open so calls made during it can be observed.
    /// </summary>
    internal Func<CancellationToken, Task>? BeforeSwap { get; set; }

    /// <summary>MSBuildWorkspace instances not yet disposed. Test seam for the swap's cleanup.</summary>
    internal int LiveWorkspaceCount => Volatile.Read(ref _liveWorkspaces);

    /// <summary>
    /// Keeps the current workspace alive for the duration of a tool call, so a reload that
    /// swaps in the next snapshot meanwhile disposes the old workspace only after the call
    /// finishes with it. Null before the first load.
    /// </summary>
    internal IDisposable? AcquireSnapshotLease()
    {
        while (true)
        {
            var generation = Volatile.Read(ref _generation);
            if (generation is null) return null;
            if (generation.TryAcquire() is { } lease) return lease;
            // Retired between the read and the acquire: the swap has already published the
            // next generation, so the loop terminates.
        }
    }

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
    /// How many MSBuild build hosts load the primary solution side by side
    /// (<c>--load-parallelism</c>). 0 picks automatically (<see cref="EffectiveLoadParallelism"/>);
    /// 1 is a plain <c>OpenSolutionAsync</c>. Set by Program.cs.
    /// </summary>
    internal int LoadParallelism { get; set; }

    /// <summary>
    /// Build hosts for a solution declaring <paramref name="projectCount"/> projects. Each host
    /// costs a process start and loads its group's shared references again, which only pays off
    /// once there are enough projects to keep every host busy.
    /// </summary>
    internal int EffectiveLoadParallelism(int projectCount)
    {
        if (LoadParallelism > 0)
            return Math.Min(LoadParallelism, Math.Max(projectCount, 1));

        return projectCount < AutoParallelLoadMinProjects
            ? 1
            : Math.Clamp(Environment.ProcessorCount / 4, 1, AutoParallelLoadMaxHosts);
    }

    /// <summary>
    /// How to split the primary solution's load, or a single group for a plain
    /// <c>OpenSolutionAsync</c>. Automatic mode only splits when the reference graph lets the
    /// busiest build host do clearly less than one host would alone.
    /// </summary>
    private ProjectPartitioner.Plan PlanParallelLoad(IReadOnlyList<string> declared)
    {
        var groups = EffectiveLoadParallelism(declared.Count);
        var plan = ProjectPartitioner.CreatePlan(declared, groups);
        if (plan.Groups.Count > 1)
        {
            logger.LogInformation(
                "Parallel load plan: {Groups} build hosts, busiest loads {Largest} of {Single} projects (estimated speed-up {Speedup:0.00}x).",
                plan.Groups.Count, plan.LargestGroupLoad, plan.SingleLoad, plan.EstimatedSpeedup);
        }

        if (LoadParallelism == 0 && plan.EstimatedSpeedup < AutoParallelLoadMinSpeedup)
            return new ProjectPartitioner.Plan([declared], declared.Count, declared.Count);

        return plan;
    }

    private const int AutoParallelLoadMinProjects = 12;
    private const int AutoParallelLoadMaxHosts = 4;
    private const double AutoParallelLoadMinSpeedup = 1.8;

    /// <summary>
    /// True when solutions were configured at startup (<c>--solution</c> or found in the working
    /// directory). MCP roots are then never consulted, even before that load has started or
    /// after it failed: roots discovery is the fallback for a server started without one.
    /// </summary>
    internal bool HasConfiguredSolutions { get; set; }

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
    internal IReadOnlyList<ProjectProblem> GetProjectProblems()
    {
        // Read in the reverse of the swap's write order (solution, solutions, health, version):
        // seeing the new health implies seeing the new solution, so the problem cache never
        // pairs one load's health with another load's projects.
        var health = _health;
        var solution = _solution;
        var solutions = _loadedSolutions;
        return health.GetProblems(solution, solutions, SnapshotVersion);
    }

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
    ///
    /// <para>Only the first load leaves the workspace without an answer (<c>Loading</c>). Every
    /// later load — a reload after a build-file change — opens the next workspace beside the
    /// current one while tool calls keep answering from the current snapshot, then swaps it in
    /// atomically. A reload that fails keeps the current snapshot and reports the error in
    /// <see cref="LastReloadError"/>; cancellation is never reported as a failure.</para>
    /// </summary>
    public async Task LoadSolutionsAsync(IReadOnlyList<string> solutionPaths, CancellationToken ct = default)
    {
        if (solutionPaths.Count == 0)
            throw new ArgumentException("At least one solution path is required.", nameof(solutionPaths));

        await _loadLock.WaitAsync(ct);
        try
        {
            await LoadAndSwapAsync([.. solutionPaths], ct);
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <summary>A workspace opened by a load and not yet swapped in.</summary>
    private sealed record PendingLoad(
        string[] SolutionPaths,
        Workspace Workspace,
        Solution Solution,
        IReadOnlyList<LoadedSolution> Loaded,
        WorkspaceHealth Health,
        DateTimeOffset StartedUtc,
        IReadOnlyDictionary<string, DateTime> BuildFileStampsAtStart,
        LoadProfile Profile);

    /// <summary>Must hold <see cref="_loadLock"/>.</summary>
    private async Task LoadAndSwapAsync(string[] solutionPaths, CancellationToken ct)
    {
        var reloading = _solution is not null;
        var previousState = State;
        var previousError = _errorMessage;

        if (reloading)
        {
            Volatile.Write(ref _reloading, 1);
            logger.LogInformation(
                "Reloading {SolutionPath} in the background; serving snapshot {Version} meanwhile.",
                string.Join(", ", solutionPaths), SnapshotVersion);
        }
        else
        {
            State = WorkspaceState.Loading;
            _solutionPaths = solutionPaths;
            _rootDirectory = ComputeRootDirectory(solutionPaths);
            _errorMessage = null;
            logger.LogInformation("Loading solution: {SolutionPath}", string.Join(", ", solutionPaths));
        }

        PendingLoad? pending = null;
        try
        {
            pending = await OpenSolutionsAsync(solutionPaths, ct);
            if (BeforeSwap is { } beforeSwap)
                await beforeSwap(ct);

            await SwapAsync(pending);
            pending = null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Superseded or shutting down: nothing failed, so nothing is reported as failed.
            logger.LogInformation("Workspace load cancelled.");
            if (!reloading)
            {
                State = previousState == WorkspaceState.Loading ? WorkspaceState.NotStarted : previousState;
                _errorMessage = previousError;
            }

            throw;
        }
        catch (Exception ex)
        {
            if (reloading)
            {
                logger.LogError(ex, "Background reload failed; keeping snapshot {Version}.", SnapshotVersion);
                _lastReloadError = ex.Message;
                Interlocked.Exchange(ref _reloadRetryNotBeforeTicks, clock.GetUtcNow().UtcTicks + ErrorRetryCooldownTicks);
            }
            else
            {
                logger.LogError(ex, "Failed to load solution: {SolutionPath}", solutionPaths[0]);
                _errorMessage = ex.Message;
                State = WorkspaceState.Error;
            }

            throw;
        }
        finally
        {
            if (pending is not null)
                DisposeWorkspace(pending.Workspace);
            if (reloading)
                Volatile.Write(ref _reloading, 0);
        }
    }

    /// <summary>
    /// Opens the solutions into a new workspace without touching the current snapshot. The
    /// first solution opens via <c>OpenSolutionAsync</c>; each further one contributes only
    /// the projects not already loaded.
    /// </summary>
    private async Task<PendingLoad> OpenSolutionsAsync(string[] solutionPaths, CancellationToken ct)
    {
        // Anything on disk before MSBuild evaluates and still not a document afterwards was
        // excluded by the project; files created from here on are genuinely new.
        var startedUtc = clock.GetUtcNow();
        var profile = new LoadProfile();
        // A build file whose stamp moves between here and the swap changed after MSBuild may
        // have read it; the swap marks it changed so the next refresh reloads again.
        var stampsAtStart = _buildFiles.Keys.ToDictionary(path => path, File.GetLastWriteTimeUtc);

        var health = new WorkspaceHealth(clock);
        // Shadow-copying host: analyzers and generators load from temp copies, never from
        // ~/.nuget/packages, so this long-lived process cannot block `dotnet restore` (#25).
        var declared = await TryReadDeclaredProjectsAsync(solutionPaths[0], ct);
        var plan = declared is null ? null : PlanParallelLoad(declared);
        profile.Step("plan");
        Workspace workspace = plan is { Groups.Count: > 1 }
            ? new AdhocWorkspace(ShadowCopyAnalyzerService.HostServices, WorkspaceKind.MSBuild)
            : MSBuildWorkspace.Create(ShadowCopyAnalyzerService.HostServices);
        Interlocked.Increment(ref _liveWorkspaces);
        try
        {
            workspace.RegisterWorkspaceFailedHandler(args =>
            {
                // Logged and kept: a failure here means a project loaded partially or not at
                // all, which get_workspace_status and empty results must be able to report.
                health.RecordDiagnostic(args.Diagnostic.Kind, args.Diagnostic.Message);
                if (args.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
                    logger.LogError("Workspace failure: {Message}", args.Diagnostic.Message);
                else
                    logger.LogWarning("Workspace warning: {Message}", args.Diagnostic.Message);
            });

            Solution solution;
            if (workspace is AdhocWorkspace adhoc)
            {
                solution = await ParallelSolutionLoader.LoadAsync(adhoc, solutionPaths[0], plan!.Groups, profile, ct);
                profile.Step($"open ({plan.Groups.Count} build hosts)");
            }
            else
            {
                solution = await ((MSBuildWorkspace)workspace).OpenSolutionAsync(solutionPaths[0], progress: profile, cancellationToken: ct);
                profile.Step("open");
            }

            List<LoadedSolution> loaded = [DescribePrimary(solutionPaths[0], declared, solution)];

            foreach (var additional in solutionPaths.Skip(1))
            {
                var (withAdditional, described) = await AddSolutionProjectsAsync(workspace, solution, additional, profile, ct);
                solution = withAdditional;
                loaded.Add(described);
            }
            if (solutionPaths.Length > 1)
                profile.Step("additional solutions");

            // A fresh clone's unbuilt in-solution analyzers would make every derived-type search throw.
            solution = UnresolvedAnalyzerReferences.Strip(solution, health);
            profile.Step("strip analyzers");

            return new PendingLoad(
                solutionPaths, workspace, solution, loaded, health, startedUtc, stampsAtStart, profile);
        }
        catch
        {
            DisposeWorkspace(workspace);
            throw;
        }
    }

    /// <summary>
    /// Publishes a loaded workspace as the current snapshot. Brief: everything slow happened in
    /// <see cref="OpenSolutionsAsync"/>. The replaced workspace is retired, and disposed as
    /// soon as no tool call still holds a lease on it.
    /// </summary>
    private async Task SwapAsync(PendingLoad load)
    {
        WorkspaceGeneration? retired;
        await _writeLock.WaitAsync(CancellationToken.None);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // The previous snapshot is being replaced; compiling it further is waste.
            CancelWarmUp();

            retired = _generation;
            Volatile.Write(ref _generation,
                new WorkspaceGeneration(load.Workspace, () => Interlocked.Decrement(ref _liveWorkspaces)));

            _solutionPaths = load.SolutionPaths;
            _rootDirectory = ComputeRootDirectory(load.SolutionPaths);
            _projectSolutions = BuildMembership(load.Loaded);
            _lastLoadStartedUtc = load.StartedUtc;
            // Write order matters to GetProjectProblems: solution, solutions, health, version.
            _solution = load.Solution;
            _loadedSolutions = load.Loaded;
            _health = load.Health;
            Interlocked.Increment(ref _loadCount);
            Interlocked.Increment(ref _snapshotVersion);
            _errorMessage = null;
            _lastReloadError = null;

            if (load.SolutionPaths.Length == 1)
                logger.LogInformation("Solution loaded: {ProjectCount} projects", load.Solution.ProjectIds.Count);
            else
                logger.LogInformation("{SolutionCount} solutions loaded: {ProjectCount} distinct projects",
                    load.SolutionPaths.Length, load.Solution.ProjectIds.Count);

            load.Profile.Step("swap");
            SnapshotFileTimestamps(load.BuildFileStampsAtStart);
            load.Profile.Step("file snapshot");
            State = WorkspaceState.Ready;
            logger.LogInformation("Load profile: {Profile}", load.Profile.Summarize());
            StartWarmUp(load.Solution);
        }
        finally
        {
            _writeLock.Release();
        }

        retired?.Retire();
    }

    private void DisposeWorkspace(Workspace workspace)
    {
        workspace.Dispose();
        Interlocked.Decrement(ref _liveWorkspaces);
    }

    /// <summary>
    /// Starts a reload of the configured solutions in the background, unless one is already in
    /// flight — racing callers that notice the same change coalesce into one reload. Returns
    /// whether this call started it. A change made while a reload runs is picked up by the
    /// first refresh after it completes.
    /// </summary>
    private bool StartBackgroundReload(string reason, IReadOnlyList<string>? solutionPaths = null)
    {
        if (_solution is null || _disposed) return false;
        if (Interlocked.CompareExchange(ref _reloadRunning, 1, 0) != 0) return false;

        _reloadReason = reason;
        logger.LogInformation("{Reason}: reloading in the background.", reason);
        _reload = Task.Run(async () =>
        {
            try
            {
                await LoadSolutionsAsync(solutionPaths ?? _solutionPaths, _backgroundCts.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                // Shutting down.
            }
            catch (Exception)
            {
                // Recorded in LastReloadError by the load; the previous snapshot stays current.
            }
            finally
            {
                _reloadReason = null;
                Interlocked.Exchange(ref _reloadRunning, 0);
            }
        }, CancellationToken.None);
        return true;
    }

    /// <summary>
    /// Awaits the in-flight background reload. Test seam — nothing in the request path
    /// should ever wait on this.
    /// </summary>
    internal Task WaitForReloadAsync() => _reload;

    private bool ReloadRetryAllowed() =>
        clock.GetUtcNow().UtcTicks >= Interlocked.Read(ref _reloadRetryNotBeforeTicks);

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
    /// The loadable projects a solution file declares, or null when it cannot be parsed (the
    /// load then falls back to describing the projects it loaded).
    /// </summary>
    private async Task<IReadOnlyList<string>?> TryReadDeclaredProjectsAsync(string solutionPath, CancellationToken ct)
    {
        try
        {
            return await SolutionFileReader.GetProjectPathsAsync(solutionPath, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Could not parse {SolutionPath}; using its loaded projects.", solutionPath);
            return null;
        }
    }

    /// <summary>
    /// Membership for the primary solution. Parsing the file (rather than reading the loaded
    /// projects) keeps referenced-but-undeclared projects out, matching how additional
    /// solutions are described; the loaded projects are the fallback if parsing failed.
    /// </summary>
    internal static LoadedSolution DescribePrimary(string solutionPath, IReadOnlyList<string>? declared, Solution solution) =>
        new(solutionPath, declared ?? [.. LoadedProjectPaths(solution)], null);

    /// <summary>Parses <paramref name="solutionPath"/> and describes it, as a load does.</summary>
    internal static async Task<LoadedSolution> DescribePrimaryAsync(
        string solutionPath, Solution solution, ILogger log, CancellationToken ct)
    {
        try
        {
            return DescribePrimary(solutionPath, await SolutionFileReader.GetProjectPathsAsync(solutionPath, ct), solution);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogDebug(ex, "Could not parse {SolutionPath}; using its loaded projects.", solutionPath);
            return DescribePrimary(solutionPath, null, solution);
        }
    }

    /// <summary>
    /// Opens every project of <paramref name="solutionPath"/> that the workspace does not
    /// already hold. <c>OpenProjectAsync</c> also loads project references and resolves the
    /// ones already in the workspace to the existing project, so a shared library is never
    /// duplicated. One project failing to load is logged and skipped — the same tolerance
    /// <c>OpenSolutionAsync</c> applies — and only an unreadable solution file is an error.
    /// </summary>
    private async Task<(Solution Solution, LoadedSolution Described)> AddSolutionProjectsAsync(
        Workspace workspace, Solution solution, string solutionPath, LoadProfile profile, CancellationToken ct)
    {
        IReadOnlyList<string> declared;
        try
        {
            declared = await SolutionFileReader.GetProjectPathsAsync(solutionPath, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to read solution: {SolutionPath}", solutionPath);
            return (solution, new LoadedSolution(solutionPath, [], ex.Message));
        }

        var added = 0;
        var loader = new MSBuildProjectLoader(workspace) { SkipUnrecognizedProjects = true };
        var loaded = LoadedProjectPaths(solution);
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
                // What MSBuildWorkspace.OpenProjectAsync does, against this load's solution:
                // references to projects already present resolve to them through the map.
                var infos = await loader.LoadProjectInfoAsync(
                    projectPath, ProjectMap.Create(solution), profile, cancellationToken: ct);
                foreach (var info in infos)
                    solution = solution.AddProject(info);
                solution = ParallelSolutionLoader.ConvertOutputReferencesToProjectReferences(solution);
                added++;
                // The open may also have pulled in this solution's other projects as references.
                loaded = LoadedProjectPaths(solution);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to load project {ProjectPath} from {SolutionPath}", projectPath, solutionPath);
            }
        }

        logger.LogInformation("Added {SolutionPath}: {Declared} projects declared, {Added} newly loaded",
            solutionPath, declared.Count, added);
        return (solution, new LoadedSolution(solutionPath, declared, null));
    }

    private static HashSet<string> LoadedProjectPaths(Solution solution) =>
        solution.Projects
            .Select(p => p.FilePath)
            .OfType<string>()
            .Select(SolutionFileReader.NormalizePath)
            .ToHashSet(PathComparison.Comparer);

    private static Dictionary<string, List<string>> BuildMembership(IEnumerable<LoadedSolution> solutions)
    {
        var map = new Dictionary<string, List<string>>(PathComparison.Comparer);
        foreach (var solution in solutions)
        {
            foreach (var projectPath in solution.ProjectPaths)
            {
                if (!map.TryGetValue(projectPath, out var names))
                    map[projectPath] = names = [];
                if (!names.Contains(solution.Name, PathComparison.Comparer))
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

    internal static bool IsSameOrUnder(string path, string root) =>
        IsSameOrUnder(path, root, PathComparison.Comparison);

    /// <summary>
    /// Whether <paramref name="path"/> is <paramref name="root"/> or lies beneath it, on a
    /// directory boundary (<c>/repo/src2</c> is not under <c>/repo/src</c>). Case-sensitive
    /// on Linux, where <c>/repo/Src</c> is a different directory.
    /// </summary>
    internal static bool IsSameOrUnder(string path, string root, StringComparison comparison)
    {
        var trimmedRoot = Path.TrimEndingDirectorySeparator(root);
        return path.Equals(trimmedRoot, comparison)
            || path.StartsWith(trimmedRoot + Path.DirectorySeparatorChar, comparison)
            || (Path.EndsInDirectorySeparator(root) && path.StartsWith(root, comparison));
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
        Solution solution, CancellationToken ct, WorkspaceHealth? health = null)
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
                    health.ProjectCompiled(projectId, results[i], null);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    health.ProjectCompiled(projectId, null, ex);
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
        WorkspaceState.Ready when ReloadReason is { } reason =>
            $"Workspace is ready and reloading in the background ({reason}). Answers come from the previous snapshot until the reload completes.",
        WorkspaceState.Ready when _lastReloadError is { } error =>
            $"Workspace is ready, but the last reload failed ({error}); answers come from the snapshot before it. The reload is retried on a later call.",
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
                // A failed refresh must not surface as a raw MCP error. Reloads run in the
                // background and keep the current snapshot, so the answer below still stands.
                logger.LogWarning(ex, "Workspace refresh failed; will retry on a later call.");
            }

            // Roots changed under a workspace that came from them: rediscover, and reload in
            // the background if the solutions differ. This call still answers right away.
            if (_solutionsFromRoots && TryClaimRootsDiscovery())
                await DiscoverFromRootsAsync(ct);

            if (State == WorkspaceState.Ready) return null;
        }

        // Retry a previously failed load of a known solution path (transient failures
        // such as mid-write project files). Cooldown prevents hammering a broken solution.
        if (State == WorkspaceState.Error && _solutionPaths.Count > 0 && TryClaimErrorRetry())
        {
            // On the server's own token, not the caller's: a cancelled request must not
            // abandon a load every later call is waiting for.
            await RunLoadAsync(_solutionPaths).WaitAsync(ct);
            if (State == WorkspaceState.Ready) return null;
        }

        // No solution configured (or the configured ones came from roots): discover from MCP roots.
        if (State != WorkspaceState.Loading
            && !HasConfiguredSolutions
            && (_solutionPaths.Count == 0 || _solutionsFromRoots)
            && TryClaimRootsDiscovery())
        {
            await DiscoverFromRootsAsync(ct);
            if (State == WorkspaceState.Ready) return null;
        }

        return JsonSerializer.Serialize(new StatusResponse(State.ToString(), GetStatusMessage()));
    }

    /// <summary>
    /// A load on the server's lifetime token whose failure is recorded (State, ErrorMessage,
    /// LastReloadError) rather than thrown, so callers can stop waiting without leaving an
    /// unobserved exception behind.
    /// </summary>
    private async Task RunLoadAsync(IReadOnlyList<string> solutionPaths)
    {
        try
        {
            await LoadSolutionsAsync(solutionPaths, _backgroundCts.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Workspace load failed; will retry on a later call.");
        }
    }

    private bool TryClaimErrorRetry()
    {
        var now = clock.GetUtcNow().UtcTicks;
        var last = Interlocked.Read(ref _lastErrorRetryTicks);
        if (now - last < ErrorRetryCooldownTicks) return false;
        return Interlocked.CompareExchange(ref _lastErrorRetryTicks, now, last) == last;
    }

    /// <summary>
    /// The directories of the client's MCP roots. Test seam: replaces the roots request to the
    /// connected client.
    /// </summary>
    internal Func<CancellationToken, Task<IReadOnlyList<string>>>? RootsProvider { get; set; }

    /// <summary>
    /// Called when the client sends <c>notifications/roots/list_changed</c>. The next tool call
    /// rediscovers solutions from the new roots — unless the solutions were configured
    /// explicitly (<c>--solution</c> or the working directory), which roots never override.
    /// </summary>
    public void OnRootsChanged()
    {
        Volatile.Write(ref _rootsDirty, 1);
        logger.LogInformation("MCP roots changed; solutions are rediscovered on the next call.");
    }

    /// <summary>
    /// One caller at a time claims discovery: when it is pending (never tried, or roots
    /// changed), or when an earlier attempt found no solution and its cooldown has passed.
    /// </summary>
    private bool TryClaimRootsDiscovery()
    {
        if (Interlocked.Exchange(ref _rootsDirty, 0) == 1) return true;

        var retryAt = Interlocked.Read(ref _rootsRetryNotBeforeTicks);
        return _solution is null
            && clock.GetUtcNow().UtcTicks >= retryAt
            && Interlocked.CompareExchange(ref _rootsRetryNotBeforeTicks, long.MaxValue, retryAt) == retryAt;
    }

    /// <summary>
    /// Scans every MCP root for solutions — one per root, or all with <c>--all-solutions</c> —
    /// so a multi-root workspace spanning several services loads them all into one workspace.
    /// With no snapshot yet the call waits for the load; with one, a changed set of solutions
    /// reloads in the background like any other reload.
    /// </summary>
    private async Task DiscoverFromRootsAsync(CancellationToken ct)
    {
        IReadOnlyList<string>? roots;
        try
        {
            roots = await RequestRootDirectoriesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Typically a client without roots support; only a roots change asks again.
            logger.LogWarning(ex, "Failed to discover workspace from MCP roots.");
            return;
        }

        if (roots is null) return;

        var solutionPaths = new List<string>();
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;

            foreach (var solutionPath in SolutionDiscovery.ScanDirectory(root, DiscoverAllSolutions))
            {
                if (solutionPaths.Contains(solutionPath, PathComparison.Comparer)) continue;
                logger.LogInformation("Discovered solution from MCP roots: {SolutionPath}", solutionPath);
                solutionPaths.Add(solutionPath);
            }
        }

        if (solutionPaths.Count == 0)
        {
            // A solution may be created later (dotnet new sln); look again after a cooldown.
            logger.LogWarning("MCP roots available but no .sln/.slnx found in any root directory.");
            if (_solution is null)
                Interlocked.Exchange(ref _rootsRetryNotBeforeTicks, clock.GetUtcNow().UtcTicks + ErrorRetryCooldownTicks);
            return;
        }

        _solutionsFromRoots = true;
        if (_solution is null)
        {
            await RunLoadAsync(solutionPaths).WaitAsync(ct);
        }
        else if (!solutionPaths.SequenceEqual(_solutionPaths, PathComparison.Comparer))
        {
            StartBackgroundReload("MCP roots changed", solutionPaths);
        }
    }

    private async Task<IReadOnlyList<string>?> RequestRootDirectoriesAsync(CancellationToken ct)
    {
        if (RootsProvider is { } provider)
            return await provider(ct);

        if (Services?.GetService(typeof(McpServer)) is not McpServer server)
            return null;

        var result = await server.RequestRootsAsync(new ListRootsRequestParams(), ct);
        return
        [
            .. result.Roots
                .Select(root => Uri.TryCreate(root.Uri, UriKind.Absolute, out var uri) && uri.IsFile ? uri.LocalPath : null)
                .OfType<string>()
        ];
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

        // Captured: a reload swaps _health, and this warm-up reports into its own load's record.
        var health = _health;
        health.WarmUpStarted(solution.ProjectIds.Count);

        _warmUp = Task.Run(async () =>
        {
            var started = clock.GetTimestamp();
            try
            {
                logger.LogInformation("Warming compilations for {Count} projects in the background...",
                    solution.ProjectIds.Count);
                await CompileAllAsync(solution, token, health);
                health.WarmUpFinished("completed");
                using var process = System.Diagnostics.Process.GetCurrentProcess();
                logger.LogInformation("All compilations warmed in {Elapsed} ms (peak working set {PeakMb} MB).",
                    (long)clock.GetElapsedTime(started).TotalMilliseconds, process.PeakWorkingSet64 / (1024 * 1024));
            }
            catch (OperationCanceledException)
            {
                // Superseded by a reload, or shutting down.
                health.WarmUpFinished("cancelled");
            }
            catch (Exception ex)
            {
                // Warm-up is an optimization: a project that fails to compile here fails the
                // same way, with a proper error, when a tool asks for it.
                health.WarmUpFinished("failed");
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
    private void SnapshotFileTimestamps(IReadOnlyDictionary<string, DateTime> stampsAtLoadStart)
    {
        if (_solution is null) return;

        _knownDocuments.Clear();
        _buildFiles.Clear();
        _knownDocumentPaths.Clear();
        _defaultGlobByProject.Clear();
        var projectsByDirectory = new Dictionary<string, List<ProjectId>>(PathComparison.Comparer);
        var projectFiles = new HashSet<string>(PathComparison.Comparer);
        var documents = new List<(DocumentId Id, string Path, string Directory)>();

        foreach (var projectId in _solution.ProjectIds)
        {
            var project = _solution.GetProject(projectId);
            if (project is null) continue;

            if (project.FilePath is not null)
            {
                // Once per file: a multi-targeted project appears once per target framework.
                if (projectFiles.Add(project.FilePath))
                    TrackBuildFile(project.FilePath, stampsAtLoadStart);

                if (Path.GetDirectoryName(project.FilePath) is { } projectDir)
                {
                    if (!projectsByDirectory.TryGetValue(projectDir, out var ids))
                        projectsByDirectory[projectDir] = ids = [];
                    ids.Add(projectId);
                }
            }

            foreach (var document in project.Documents)
            {
                if (document.FilePath is { } path)
                    documents.Add((document.Id, path, Path.GetDirectoryName(path) ?? string.Empty));
            }
        }

        // One listing per directory, read in parallel, instead of a stat per document: on a
        // 15,000-document solution this baseline was 0.7 s of the time before Ready.
        var directories = documents.Select(d => d.Directory).Distinct(PathComparison.Comparer).ToList();
        var listings = new Dictionary<string, DateTime>?[directories.Count];
        Parallel.For(0, directories.Count, new ParallelOptions { MaxDegreeOfParallelism = ScanParallelism },
            i => listings[i] = ReadDirectoryTimestamps(directories[i]));
        var byDirectory = new Dictionary<string, Dictionary<string, DateTime>?>(PathComparison.Comparer);
        for (var i = 0; i < directories.Count; i++)
            byDirectory[directories[i]] = listings[i];

        foreach (var (id, path, directory) in documents)
        {
            // A path the listing spells differently (or an unreadable directory) falls back to a stat.
            var writeTime = byDirectory[directory] is { } listing && listing.TryGetValue(path, out var listed)
                ? listed
                : File.GetLastWriteTimeUtc(path);
            // GetLastWriteTimeUtc returns year 1601 for non-existent files
            if (writeTime.Year < 1900) continue;

            _knownDocuments[id] = new TrackedDocument(path, directory, writeTime);
            _knownDocumentPaths[path] = 0;
        }

        // The solution files themselves: adding or removing a project changes what loads.
        foreach (var solutionPath in _solutionPaths)
            TrackBuildFile(solutionPath, stampsAtLoadStart);

        var projectPaths = _solution.Projects.Select(p => p.FilePath).OfType<string>();
        foreach (var sharedFile in EnumerateSharedBuildFiles(projectPaths, _solutionPaths))
            TrackBuildFile(sharedFile, stampsAtLoadStart);

        _projectsByDirectory = projectsByDirectory.ToDictionary(
            pair => pair.Key, pair => pair.Value.ToArray(), PathComparison.Comparer);

        // A file a csproj edit has since pulled in is a document now, not an exclusion.
        foreach (var path in _excludedSourceFiles.Keys)
        {
            if (_knownDocumentPaths.ContainsKey(path))
                _excludedSourceFiles.TryRemove(path, out _);
        }

        logger.LogInformation("Captured timestamps for {Count} documents across {BuildFileCount} build files",
            _knownDocuments.Count, _buildFiles.Count);
    }

    /// <summary>File names MSBuild, the SDK resolver, and NuGet pick up from a project's directory or any above it.</summary>
    private static readonly string[] SharedBuildFileNames =
    [
        "Directory.Build.props",
        "Directory.Build.targets",
        "Directory.Packages.props",
        "global.json",
        // NuGet accepts all three spellings; on a case-sensitive file system they are distinct.
        "NuGet.config",
        "nuget.config",
        "NuGet.Config"
    ];

    /// <summary>
    /// Directory-scoped files change the compilation of every project beneath them — a bumped
    /// version in Directory.Packages.props is as significant as a csproj edit — but they are
    /// not documents, so nothing else in the workspace notices them.
    ///
    /// <para>MSBuild imports the nearest Directory.Build.props/.targets walking up from each
    /// <em>project</em>, not from the solution, and those commonly import the next one up; large
    /// repos keep them in src/ and tests/ or above the solution folder. So every directory from
    /// each project (and each solution, for global.json and NuGet.config) up to the file-system
    /// root is checked. Directories shared by several projects are visited once, which keeps
    /// this to a handful of stats per distinct folder.</para>
    /// </summary>
    internal static IReadOnlyList<string> EnumerateSharedBuildFiles(
        IEnumerable<string> projectPaths, IEnumerable<string> solutionPaths)
    {
        var visited = new HashSet<string>(PathComparison.Comparer);
        var found = new HashSet<string>(PathComparison.Comparer);
        var result = new List<string>();

        foreach (var start in projectPaths.Concat(solutionPaths).Select(Path.GetDirectoryName).OfType<string>())
        {
            // Stops at the first directory already walked: everything above it was walked too.
            for (var dir = start; dir is not null && visited.Add(dir); dir = Path.GetDirectoryName(dir))
            {
                foreach (var name in SharedBuildFileNames)
                {
                    var path = Path.Combine(dir, name);
                    if (File.Exists(path) && found.Add(path))
                        result.Add(path);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Records a build file's baseline. One whose stamp moved since the load started may have
    /// changed after MSBuild read it, so it is recorded as unknown — the next refresh sees it
    /// as changed and reloads again, instead of the edit being silently absorbed.
    /// </summary>
    private void TrackBuildFile(string path, IReadOnlyDictionary<string, DateTime> stampsAtLoadStart)
    {
        var writeTime = File.GetLastWriteTimeUtc(path);
        if (writeTime.Year < 1900) return;

        _buildFiles[path] = stampsAtLoadStart.TryGetValue(path, out var atStart) && atStart != writeTime
            ? new BuildFileInfo(DateTime.MinValue, null)
            : new BuildFileInfo(writeTime, ComputeHash(path));
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

        // Claimed atomically: racing callers past the cooldown must not all run the scan (and,
        // before reloads were coalesced, each start its own reload of the same change).
        var lastRefresh = Interlocked.Read(ref _lastRefreshTicks);
        if (now - lastRefresh < RefreshCooldownTicks) return;
        if (Interlocked.CompareExchange(ref _lastRefreshTicks, now, lastRefresh) != lastRefresh) return;

        // Phase 1: check build files (one stat each, hashed only when the stat moved). While a
        // reload is in flight its swap re-baselines them, and anything that changed after it
        // started is flagged then — checking now would only find the change it is handling.
        if (!IsReloading && ReloadRetryAllowed())
        {
            var changedBuildFiles = GetChangedBuildFiles();
            if (changedBuildFiles.Count > 0)
            {
                StartBackgroundReload(
                    $"build files changed: {string.Join(", ", changedBuildFiles.Select(Path.GetFileName).Distinct())}");
            }
        }

        // Phase 2: the recursive walk for files in folders no document lives in yet never
        // runs in-band; it reports into _pendingNewFiles for a later call to act on.
        var lastStructural = Interlocked.Read(ref _lastStructuralScanTicks);
        if (now - lastStructural >= StructuralScanCooldownTicks
            && Interlocked.CompareExchange(ref _lastStructuralScanTicks, now, lastStructural) == lastStructural)
        {
            StartStructuralScan();
        }

        // Phase 3: collect changed, deleted, and new documents without holding the lock (the
        // directory reads are the expensive part — keeping them lock-free avoids blocking
        // concurrent tool calls). Document edits still apply to the current snapshot while a
        // reload is in flight, so answers stay fresh for the files that changed.
        var scan = ScanKnownDocuments(ct);
        foreach (var path in _pendingNewFiles.Keys)
        {
            _pendingNewFiles.TryRemove(path, out _);
            scan.NewFiles.Add(path);
        }

        if (scan.Changed.Count == 0 && scan.Deleted.Count == 0 && scan.NewFiles.Count == 0) return;

        // Classify and apply under the lock: a swap rebuilds the tracked-document maps under
        // the same lock, and classifying against half-rebuilt maps would mark real documents
        // as excluded for good.
        await _writeLock.WaitAsync(ct);
        try
        {
            var additions = ClassifyNewFiles(scan.NewFiles, out var needReload);
            if (needReload.Count > 0
                && !StartBackgroundReload(
                    $"new source files in projects with custom compile items: {string.Join(", ", needReload.Select(Path.GetFileName))}"))
            {
                // A reload already in flight may have evaluated the projects before these
                // files existed; ask again once it has swapped in.
                foreach (var path in needReload)
                    _pendingNewFiles[path] = 0;
            }

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
            PathComparison.Comparer);
        foreach (var entry in _knownDocuments)
        {
            if (!byDirectory.TryGetValue(entry.Value.Directory, out var documents))
                byDirectory[entry.Value.Directory] = documents = [];
            documents.Add(entry);
        }

        var scan = new DocumentScan([], [], new HashSet<string>(PathComparison.Comparer));
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
        var result = new Dictionary<string, DateTime>(PathComparison.Comparer);
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
        var live = _knownDocuments.Values.Select(d => d.FilePath).ToHashSet(PathComparison.Comparer);
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

        var projectDirectories = solution.Projects
            .Select(p => Path.GetDirectoryName(p.FilePath))
            .OfType<string>()
            .ToHashSet(PathComparison.Comparer);

        // One walk per directory, however many target frameworks load the project.
        foreach (var projectDir in projectDirectories)
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(projectDir)) continue;

            foreach (var file in EnumerateProjectSourceFiles(projectDir, projectDirectories))
            {
                ct.ThrowIfCancellationRequested();
                if (IsUntrackedSourceFile(file))
                    found.Add(file);
            }
        }

        return found;
    }

    /// <summary>
    /// The .cs files the SDK default glob could compile for the project in
    /// <paramref name="projectDir"/>, found without descending where that glob never looks or
    /// where descending is unsafe on a large repo:
    /// <list type="bullet">
    /// <item>the project's own bin/ and obj/ (often the largest folders in the tree);</item>
    /// <item>dot-folders (<c>**/.*/**</c> is a default exclude) and node_modules;</item>
    /// <item>another project's folder — that project gets its own walk, so a project at the
    /// repo root no longer re-walks every project beneath it;</item>
    /// <item>symbolic links and junctions, which can point back up the tree and turn the
    /// walk into an endless loop.</item>
    /// </list>
    /// </summary>
    internal static IEnumerable<string> EnumerateProjectSourceFiles(string projectDir, IReadOnlySet<string> projectDirectories)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
        };

        return new FileSystemEnumerable<string>(projectDir, (ref FileSystemEntry entry) => entry.ToFullPath(), options)
        {
            ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                !entry.IsDirectory && entry.FileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase),
            ShouldRecursePredicate = (ref FileSystemEntry entry) =>
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) return false;

                var name = entry.FileName;
                if (name.StartsWith('.') || name.Equals("node_modules", StringComparison.OrdinalIgnoreCase))
                    return false;

                var atProjectRoot = Path.TrimEndingDirectorySeparator(entry.Directory)
                    .Equals(Path.TrimEndingDirectorySeparator(projectDir.AsSpan()), PathComparison.Comparison);
                if (atProjectRoot && (name.Equals("bin", PathComparison.Comparison) || name.Equals("obj", PathComparison.Comparison)))
                    return false;

                return !projectDirectories.Contains(entry.ToFullPath());
            }
        };
    }

    public void Dispose()
    {
        _disposed = true;
        _backgroundCts.Cancel();

        try
        {
            // Bounded so shutdown cannot block on a large directory walk, a compilation, or a
            // reload that is slow to observe cancellation. A reload still running afterwards
            // finds the manager disposed at its swap and disposes its own workspace.
            Task.WaitAll([_structuralScan, _warmUp, _reload], TimeSpan.FromSeconds(2));
        }
        catch (Exception)
        {
            // Cancellation or a failed scan; nothing left to salvage during teardown.
        }

        _warmUpCts?.Dispose();
        _generation?.DisposeNow();
        // The semaphores are deliberately not disposed: a background load that outlived the
        // bounded wait above still releases them on its way out.
    }
}
