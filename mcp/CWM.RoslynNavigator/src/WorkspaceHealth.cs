using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CWM.RoslynNavigator;

/// <summary>A workspace load diagnostic, attributed to a project when its message names one.</summary>
internal sealed record LoadDiagnostic(string Kind, string Message, string? ProjectPath);

/// <summary>A project that loaded or compiled incompletely, and why.</summary>
internal sealed record ProjectProblem(string Name, string? Path, int Documents, IReadOnlyList<string> Issues);

/// <summary>Progress of the background compilation warm-up for the current load.</summary>
internal sealed record WarmUpProgress(string State, int Compiled, int Total, long? ElapsedMs);

/// <summary>
/// Records what went wrong while loading and compiling the workspace, so a partially loaded
/// solution can say so instead of silently answering from the projects that did load.
///
/// <para>MSBuildWorkspace keeps going when a project fails to evaluate, a project reference
/// points nowhere, or restore never ran: the solution opens, the broken projects are simply
/// missing documents or references, and every tool returns a confident but incomplete answer.
/// The signals collected here are the ones observable without extra compilation work:
/// workspace failure diagnostics, projects with no documents, projects whose compilation
/// threw or could not resolve the core library (System.Object is an error type), and analyzer
/// references whose file does not exist. Generator exceptions are not observable through the
/// public Workspaces API without running the generator driver again, so they are not
/// reported.</para>
///
/// <para>One instance per load, swapped in with the snapshot it describes: a warm-up still
/// winding down after a reload writes into the record of the load it belongs to, never the next.</para>
/// </summary>
internal sealed class WorkspaceHealth(TimeProvider clock)
{
    private readonly ConcurrentQueue<LoadDiagnostic> _diagnostics = new();
    private readonly ConcurrentDictionary<ProjectId, string> _compileProblems = new();
    private readonly Lock _gate = new();
    private int _compiled;
    private int _total;
    private string _warmUpState = "not-started";
    private long _warmUpStarted;
    private long? _warmUpElapsedMs;
    private int _revision;
    private (int SnapshotVersion, int Revision, IReadOnlyList<ProjectProblem> Problems)? _cache;

    /// <summary>Bumped on every recorded fact; part of the problem-list cache key.</summary>
    public int Revision => Volatile.Read(ref _revision);

    public void RecordDiagnostic(WorkspaceDiagnosticKind kind, string message)
    {
        _diagnostics.Enqueue(new LoadDiagnostic(
            kind == WorkspaceDiagnosticKind.Failure ? "failure" : "warning", message, null));
        Interlocked.Increment(ref _revision);
    }

    public void WarmUpStarted(int total)
    {
        lock (_gate)
        {
            _compiled = 0;
            _total = total;
            _warmUpState = "running";
            _warmUpStarted = clock.GetTimestamp();
            _warmUpElapsedMs = null;
        }
    }

    /// <summary>
    /// Records one project's warm-up outcome. A compilation that cannot resolve System.Object
    /// has no usable references — every type from the BCL or a package is an error type and
    /// every navigation answer for that project is suspect.
    /// </summary>
    public void ProjectCompiled(ProjectId projectId, Compilation? compilation, Exception? error)
    {
        if (error is not null)
        {
            _compileProblems[projectId] = $"compilation failed: {error.Message}";
            Interlocked.Increment(ref _revision);
        }
        else if (compilation is not null
            && compilation.GetSpecialType(SpecialType.System_Object).TypeKind == TypeKind.Error)
        {
            _compileProblems[projectId] = "core library not resolved — references are missing (run dotnet restore / build)";
            Interlocked.Increment(ref _revision);
        }

        lock (_gate) _compiled++;
    }

    public void WarmUpFinished(string state)
    {
        lock (_gate)
        {
            _warmUpState = state;
            _warmUpElapsedMs = (long)clock.GetElapsedTime(_warmUpStarted).TotalMilliseconds;
        }
    }

    public WarmUpProgress GetWarmUp()
    {
        lock (_gate) return new WarmUpProgress(_warmUpState, _compiled, _total, _warmUpElapsedMs);
    }

    /// <summary>Load diagnostics, attributed to a loaded project where the message names its file.</summary>
    public IReadOnlyList<LoadDiagnostic> GetDiagnostics(Solution? solution)
    {
        var projectPaths = solution?.Projects.Select(p => p.FilePath).OfType<string>().Distinct().ToList() ?? [];
        return _diagnostics
            .Select(d => d with { ProjectPath = projectPaths.FirstOrDefault(p => d.Message.Contains(p, PathComparison.Comparison)) })
            .ToList();
    }

    /// <summary>
    /// Every project with at least one problem. Cached per snapshot and revision: the empty-
    /// result check consults this on calls that found nothing, and the answer only changes
    /// when the snapshot or the recorded facts do.
    /// </summary>
    public IReadOnlyList<ProjectProblem> GetProblems(
        Solution? solution, IReadOnlyList<LoadedSolution> solutions, int snapshotVersion)
    {
        if (solution is null) return [];

        var revision = Revision;
        if (_cache is { } cached && cached.SnapshotVersion == snapshotVersion && cached.Revision == revision)
            return cached.Problems;

        var diagnostics = GetDiagnostics(solution);
        var problems = new List<ProjectProblem>();

        // A project the solution declares but the workspace never loaded contributes nothing
        // to any answer — the most complete form of "incomplete".
        var loadedPaths = solution.Projects
            .Select(p => p.FilePath)
            .OfType<string>()
            .Select(SolutionFileReader.NormalizePath)
            .ToHashSet(PathComparison.Comparer);

        foreach (var declaring in solutions)
        {
            foreach (var declared in declaring.ProjectPaths)
            {
                if (loadedPaths.Contains(declared) || problems.Any(p => string.Equals(p.Path, declared, PathComparison.Comparison)))
                    continue;

                problems.Add(new ProjectProblem(
                    Path.GetFileNameWithoutExtension(declared), declared, 0,
                    [$"declared in {declaring.Name} but not loaded{(File.Exists(declared) ? "" : " (file not found)")}"]));
            }
        }

        foreach (var project in solution.Projects)
        {
            var issues = new List<string>();
            var documents = CountSourceDocuments(project);

            if (documents == 0)
                issues.Add("no source documents loaded");

            if (project.FilePath is { } path)
            {
                foreach (var diagnostic in diagnostics)
                {
                    if (diagnostic.Kind == "failure" && string.Equals(diagnostic.ProjectPath, path, PathComparison.Comparison))
                        issues.Add($"load failure: {diagnostic.Message}");
                }
            }

            if (_compileProblems.TryGetValue(project.Id, out var compileProblem))
                issues.Add(compileProblem);

            foreach (var reference in project.AnalyzerReferences)
            {
                var missing = reference switch
                {
                    UnresolvedAnalyzerReference unresolved => unresolved.Display,
                    AnalyzerFileReference file when !File.Exists(file.FullPath) => file.FullPath,
                    _ => null
                };
                if (missing is not null)
                    issues.Add($"analyzer/generator not found: {Path.GetFileName(missing)}");
            }

            if (issues.Count > 0)
                problems.Add(new ProjectProblem(project.Name, project.FilePath, documents, issues));
        }

        _cache = (snapshotVersion, revision, problems);
        return problems;
    }

    /// <summary>
    /// Documents outside the project's obj/ folder. The SDK writes AssemblyInfo and global
    /// usings there for every project, so a project whose sources never loaded still has them.
    /// </summary>
    private static int CountSourceDocuments(Project project)
    {
        var projectDirectory = Path.GetDirectoryName(project.FilePath);
        if (projectDirectory is null) return project.DocumentIds.Count;

        var objPrefix = Path.Combine(projectDirectory, "obj") + Path.DirectorySeparatorChar;
        return project.Documents.Count(d =>
            d.FilePath is not { } path || !path.StartsWith(objPrefix, PathComparison.Comparison));
    }
}
