using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace CWM.RoslynNavigator;

/// <summary>
/// Loads one solution through several MSBuild build hosts at once.
///
/// <para><c>MSBuildWorkspace.OpenSolutionAsync</c> evaluates and design-time-builds every
/// project one after another in a single build host process. On a large solution that is
/// nearly all of the load: OrchardCore's 257 projects spend about 40 of 45 seconds in
/// evaluation and design-time builds. Here the projects are split into groups
/// (<see cref="ProjectPartitioner"/>), each group is written as a solution filter (.slnf) over
/// the real solution — so <c>$(SolutionDir)</c> and every other property evaluates exactly as
/// it does for the whole solution — and each filter is loaded by its own
/// <see cref="MSBuildProjectLoader"/>, which owns its own build host. The groups' results are
/// then merged into one <see cref="SolutionInfo"/>.</para>
///
/// <para>A group loads its projects' references too, so a library several groups depend on
/// is loaded by each of them. The duplicates are identified by project file and output path,
/// one is kept, and every <see cref="ProjectReference"/> to the others is pointed at it. The
/// result has the same projects, documents, and references as a single load.</para>
/// </summary>
internal static class ParallelSolutionLoader
{
    /// <summary>
    /// Loads <paramref name="solutionPath"/> as <paramref name="groups"/> parallel slices into
    /// <paramref name="workspace"/>, which must have no solution yet, and returns the merged
    /// solution.
    /// </summary>
    public static async Task<Solution> LoadAsync(
        AdhocWorkspace workspace,
        string solutionPath,
        IReadOnlyList<IReadOnlyList<string>> groups,
        IProgress<ProjectLoadProgress>? progress,
        CancellationToken ct)
    {
        var tempDirectory = Directory.CreateTempSubdirectory("cwm-rn-load-");
        try
        {
            var loads = groups.Select((projects, index) => Task.Run(async () =>
            {
                var filter = WriteFilter(tempDirectory.FullName, index, solutionPath, projects);
                var loader = new MSBuildProjectLoader(workspace) { SkipUnrecognizedProjects = true };
                return await loader.LoadSolutionInfoAsync(filter, progress, cancellationToken: ct);
            }, ct)).ToList();

            var infos = await Task.WhenAll(loads);
            var merged = Merge(infos.Select(i => (IReadOnlyList<ProjectInfo>)i.Projects).ToList());

            var solution = workspace.AddSolution(SolutionInfo.Create(
                SolutionId.CreateNewId(solutionPath), VersionStamp.Create(), solutionPath, merged));
            return ConvertOutputReferencesToProjectReferences(solution);
        }
        finally
        {
            TryDelete(tempDirectory);
        }
    }

    /// <summary>
    /// A solution filter selecting <paramref name="projects"/> from <paramref name="solutionPath"/>.
    /// Roslyn resolves the filter to the real solution, so the solution directory is unchanged.
    /// </summary>
    internal static string WriteFilter(string directory, int index, string solutionPath, IReadOnlyList<string> projects)
    {
        var solutionDirectory = Path.GetDirectoryName(Path.GetFullPath(solutionPath))!;
        var filter = new
        {
            solution = new
            {
                path = Path.GetFullPath(solutionPath),
                projects = projects.Select(p => Path.GetRelativePath(solutionDirectory, p)).ToArray()
            }
        };

        var path = Path.Combine(directory, $"group-{index}.slnf");
        File.WriteAllText(path, JsonSerializer.Serialize(filter));
        return path;
    }

    /// <summary>
    /// Merges the groups' projects: one instance per (project file, output path, name), the
    /// first group's, with project references to every dropped duplicate redirected to it.
    /// </summary>
    internal static List<ProjectInfo> Merge(IReadOnlyList<IReadOnlyList<ProjectInfo>> groups)
    {
        var canonical = new Dictionary<(string, string, string), ProjectId>();
        var redirect = new Dictionary<ProjectId, ProjectId>();
        var kept = new List<ProjectInfo>();

        foreach (var group in groups)
        {
            foreach (var project in group)
            {
                var key = (
                    project.FilePath is { } file ? SolutionFileReader.NormalizePath(file) : project.Id.Id.ToString(),
                    project.OutputFilePath ?? "",
                    project.Name);

                if (canonical.TryGetValue(key, out var existing))
                {
                    redirect[project.Id] = existing;
                    continue;
                }

                canonical[key] = project.Id;
                kept.Add(project);
            }
        }

        if (redirect.Count == 0)
            return kept;

        for (var i = 0; i < kept.Count; i++)
        {
            var project = kept[i];
            if (!project.ProjectReferences.Any(r => redirect.ContainsKey(r.ProjectId)))
                continue;

            var references = project.ProjectReferences
                .Select(r => redirect.TryGetValue(r.ProjectId, out var target)
                    ? new ProjectReference(target, r.Aliases, r.EmbedInteropTypes)
                    : r)
                .Distinct()
                .ToList();
            kept[i] = project.WithProjectReferences(references);
        }

        return kept;
    }

    /// <summary>
    /// What <c>MSBuildWorkspace</c> does after adding projects (Workspace.UpdateReferencesAfterAdd):
    /// a metadata reference to the output of a project in the solution becomes a project
    /// reference, so navigation goes to source instead of a built DLL.
    /// </summary>
    internal static Solution ConvertOutputReferencesToProjectReferences(Solution solution)
    {
        var byOutput = new Dictionary<string, ProjectId>();
        foreach (var project in solution.Projects)
        {
            if (!string.IsNullOrEmpty(project.OutputFilePath)) byOutput[project.OutputFilePath] = project.Id;
            if (!string.IsNullOrEmpty(project.OutputRefFilePath)) byOutput[project.OutputRefFilePath] = project.Id;
        }

        foreach (var projectId in solution.ProjectIds)
        {
            var project = solution.GetProject(projectId)!;
            var changed = false;
            foreach (var reference in project.MetadataReferences)
            {
                if (reference is not PortableExecutableReference pe) continue;
                if (!((pe.Display is { Length: > 0 } display && byOutput.TryGetValue(display, out var target))
                      || (pe.FilePath is { Length: > 0 } file && byOutput.TryGetValue(file, out target))))
                {
                    continue;
                }

                var projectReference = new ProjectReference(target, pe.Properties.Aliases, pe.Properties.EmbedInteropTypes);
                if (!project.ProjectReferences.Contains(projectReference))
                    project = project.AddProjectReference(projectReference);
                project = project.RemoveMetadataReference(reference);
                changed = true;
            }

            if (changed)
                solution = project.Solution;
        }

        return solution;
    }

    private static void TryDelete(DirectoryInfo directory)
    {
        try
        {
            directory.Delete(recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Temp folder; leaving two small files behind is harmless.
        }
    }
}
