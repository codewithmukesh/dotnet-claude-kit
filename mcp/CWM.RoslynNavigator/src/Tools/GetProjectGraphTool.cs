using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CWM.RoslynNavigator.Analyzers;
using CWM.RoslynNavigator.Responses;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;
using ProjectInfo = CWM.RoslynNavigator.Responses.ProjectInfo;

namespace CWM.RoslynNavigator.Tools;

[McpServerToolType]
public static class GetProjectGraphTool
{
    private const int SummaryExamples = 5;
    private const int SummaryTop = 10;

    [McpServerTool(Name = "get_project_graph"), Description("Get the solution's project dependency graph: one entry per csproj with its name, solution-relative path, target frameworks (a multi-targeted csproj is one entry listing all of them), and the projects its csproj references directly (omitted when none). Large solutions are paged: Count/TotalFound/Truncated report it, and summary=true or a project filter is cheaper than raising maxResults. summary=true returns counts, frameworks, top-level folders, dependency layers, and the most directly referenced projects instead of the list. project='Name' returns the matching projects with ReferencedBy (who depends on them). When several solutions are loaded, also lists them (Solutions) and which solution(s) each project belongs to.")]
    public static async Task<string> ExecuteAsync(
        WorkspaceManager workspace,
        [Description("Optional: only projects whose name or path contains this value (case-insensitive). Each match also lists ReferencedBy.")] string? project = null,
        [Description("True: return the solution's shape (counts, frameworks, folders, dependency layers, most-referenced projects) instead of the project list.")] bool summary = false,
        [Description("Maximum projects to return. TotalFound in the response reports the full count; re-query with a higher value, a project filter, or summary=true if it exceeds Count.")] int maxResults = 100,
        CancellationToken ct = default)
    {
        var notReady = await workspace.EnsureReadyOrStatusAsync(ct);
        if (notReady is not null) return notReady;

        var solution = workspace.GetSolution();
        if (solution is null)
            return JsonSerializer.Serialize(new ProjectGraphResult("unknown", [], 0, 0, false, Math.Max(1, maxResults)));

        // Membership is noise with one solution, so single-solution output is unchanged.
        var multiSolution = workspace.LoadedSolutions.Count > 1;

        var solutionName = workspace.SolutionFilePath is { } primary
            ? Path.GetFileName(primary)
            : solution.FilePath is not null ? Path.GetFileName(solution.FilePath) : "unknown";

        var solutions = multiSolution
            ? workspace.LoadedSolutions
                .Select(s => new SolutionSummary(s.Name, workspace.ToRelativePath(s.Path), s.ProjectPaths.Count, s.Error))
                .ToList()
            : null;

        var nodes = BuildNodes(solution, workspace, multiSolution, ct);

        if (summary)
            return JsonSerializer.Serialize(Summarize(solutionName, nodes, solutions));

        var selected = nodes;
        if (project is not null)
        {
            selected = [.. nodes.Where(n =>
                n.Info.Name.Contains(project, StringComparison.OrdinalIgnoreCase)
                || n.Info.Path.Contains(project.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))];

            if (selected.Count == 0)
                return JsonSerializer.Serialize(new ErrorResponse(ErrorCodes.InvalidArgument,
                    $"No project name or path contains '{project}'. Call get_project_graph with summary=true for an overview."));

            // Who depends on each match — the question a filtered query is usually asking.
            selected = [.. selected.Select(n => n with
            {
                Info = n.Info with
                {
                    ReferencedBy = nodes
                        .Where(other => other.Info.References?.Contains(n.Info.Name, StringComparer.Ordinal) == true)
                        .Select(other => other.Info.Name)
                        .ToList() is { Count: > 0 } dependents ? dependents : null,
                },
            })];
        }

        var page = Paging.Apply(selected.Select(n => n.Info).ToList(), maxResults);

        return JsonSerializer.Serialize(new ProjectGraphResult(
            solutionName, page.Items, page.Count, page.TotalFound, page.Truncated, page.Limit, solutions));
    }

    private sealed record ProjectNode(ProjectInfo Info, bool IsTest);

    /// <summary>
    /// One node per csproj, in solution order. MSBuildWorkspace loads a multi-targeted csproj
    /// as one project per framework ("Lib(net8.0)", "Lib(net10.0)"); those flavors are merged
    /// into one entry, and references to any flavor name the csproj.
    /// </summary>
    private static List<ProjectNode> BuildNodes(
        Solution solution, WorkspaceManager workspace, bool multiSolution, CancellationToken ct)
    {
        var groups = solution.Projects
            .GroupBy(p => p.FilePath ?? p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var nodes = new List<ProjectNode>(groups.Count);
        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested();
            var first = group.First();

            List<string> frameworks = group.Count() > 1
                    && group.Select(p => ExtractTfmFromProjectName(p.Name)).ToList() is var named
                    && named.All(t => t is not null)
                ? [.. named.Distinct(StringComparer.OrdinalIgnoreCase)!]
                : [.. group.Select(DetectTargetFramework).Distinct(StringComparer.OrdinalIgnoreCase)];

            var references = DirectReferences(solution, group.ToList(), first.FilePath);

            nodes.Add(new ProjectNode(
                new ProjectInfo(
                    Name: BaseName(first.Name),
                    Path: first.FilePath is { } path ? workspace.ToRelativePath(path) : "unknown",
                    TargetFrameworks: frameworks,
                    References: references.Count > 0 ? references : null,
                    Solutions: multiSolution ? workspace.GetSolutionsForProject(first.FilePath) : null),
                SourceClassifier.IsTestProject(first)));
        }

        return nodes;
    }

    /// <summary>
    /// The projects a csproj references itself. Roslyn's ProjectReferences on a restored SDK
    /// project are the transitive closure (OrchardCore.Cms.Web declares 2 and Roslyn lists
    /// 150), which is not the dependency graph and costs most of the response on a large
    /// solution. The csproj's own &lt;ProjectReference&gt; items pick the direct ones out of
    /// Roslyn's list; when the csproj cannot be read, or names none of them (references
    /// imported from a props file), Roslyn's list is kept.
    /// </summary>
    private static List<string> DirectReferences(Solution solution, List<Project> flavors, string? projectFile)
    {
        var targets = flavors
            .SelectMany(p => p.ProjectReferences)
            .Select(r => solution.GetProject(r.ProjectId))
            .ToList();

        var all = targets.Select(t => t is null ? "unknown" : BaseName(t.Name)).Distinct(StringComparer.Ordinal).ToList();
        if (projectFile is null || all.Count == 0) return all;

        var (paths, fileNames) = DeclaredProjectReferences(projectFile);
        var direct = targets
            .Where(t => t?.FilePath is { } path
                && (paths.Contains(Path.GetFullPath(path)) || fileNames.Contains(Path.GetFileName(path))))
            .Select(t => BaseName(t!.Name))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return direct.Count > 0 ? direct : all;
    }

    /// <summary>
    /// &lt;ProjectReference Include&gt; values of a csproj, whatever their condition: resolved
    /// full paths, plus bare file names for includes built from MSBuild properties.
    /// </summary>
    private static (HashSet<string> Paths, HashSet<string> FileNames) DeclaredProjectReferences(string projectFile)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var directory = Path.GetDirectoryName(projectFile)!;
            foreach (var item in XDocument.Load(projectFile).Descendants().Where(e => e.Name.LocalName == "ProjectReference"))
            {
                if (item.Attribute("Include")?.Value is not { Length: > 0 } include) continue;

                foreach (var entry in include.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var normalized = entry.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                    if (normalized.Contains("$(", StringComparison.Ordinal))
                        fileNames.Add(Path.GetFileName(normalized));
                    else
                        paths.Add(Path.GetFullPath(Path.Combine(directory, normalized)));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException or ArgumentException)
        {
            // Unreadable csproj: the caller keeps Roslyn's list.
        }

        return (paths, fileNames);
    }

    private static ProjectGraphSummaryResult Summarize(
        string solutionName, List<ProjectNode> nodes, List<SolutionSummary>? solutions)
    {
        var byName = nodes
            .GroupBy(n => n.Info.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        // Depth = the longest chain of project references below a project (0 = references none).
        var depths = new Dictionary<string, int>(StringComparer.Ordinal);
        int Depth(string name, HashSet<string> path)
        {
            if (depths.TryGetValue(name, out var known)) return known;
            if (!byName.TryGetValue(name, out var node) || !path.Add(name)) return 0; // unknown or cycle

            var depth = 0;
            foreach (var reference in node.Info.References ?? [])
                depth = Math.Max(depth, Depth(reference, path) + 1);

            path.Remove(name);
            return depths[name] = depth;
        }

        foreach (var name in byName.Keys)
            Depth(name, new HashSet<string>(StringComparer.Ordinal));

        var layers = nodes
            .GroupBy(n => depths.GetValueOrDefault(n.Info.Name))
            .OrderBy(g => g.Key)
            .Select(g => new ProjectLayer(g.Key, g.Count(), [.. g.Select(n => n.Info.Name).Take(SummaryExamples)]))
            .ToList();

        var incoming = nodes
            .SelectMany(n => n.Info.References ?? [])
            .GroupBy(r => r, StringComparer.Ordinal)
            .Select(g => new NamedCount(g.Key, g.Count()))
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .Take(SummaryTop)
            .ToList();

        return new ProjectGraphSummaryResult(
            Solution: solutionName,
            Projects: nodes.Count,
            TestProjects: nodes.Count(n => n.IsTest),
            MultiTargeted: nodes.Count(n => n.Info.TargetFrameworks.Count > 1),
            TargetFrameworks: CountBy(nodes.SelectMany(n => n.Info.TargetFrameworks)),
            Folders: CountBy(nodes.Select(n => TopFolder(n.Info.Path))),
            Layers: layers,
            MostReferenced: incoming,
            Solutions: solutions);
    }

    private static List<NamedCount> CountBy(IEnumerable<string> values) =>
        [.. values
            .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
            .Select(g => new NamedCount(g.Key, g.Count()))
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.Name, StringComparer.Ordinal)];

    /// <summary>"src/Orders/Orders.csproj" → "src"; a project beside the solution → ".".</summary>
    private static string TopFolder(string relativePath)
    {
        var slash = relativePath.IndexOf('/');
        return slash > 0 && relativePath.IndexOf('/', slash + 1) > 0 ? relativePath[..slash] : ".";
    }

    /// <summary>"Lib(net8.0)" → "Lib".</summary>
    internal static string BaseName(string projectName) =>
        ExtractTfmFromProjectName(projectName) is not null ? projectName[..projectName.LastIndexOf('(')] : projectName;

    internal static string DetectTargetFramework(Microsoft.CodeAnalysis.Project project)
    {
        // Strategy 1: Parse from .csproj file (most reliable)
        if (project.FilePath is not null && File.Exists(project.FilePath))
        {
            try
            {
                var doc = XDocument.Load(project.FilePath);
                var tfm = doc.Root?.Descendants("TargetFramework").FirstOrDefault()?.Value;
                if (!string.IsNullOrEmpty(tfm))
                    return tfm;

                // Multi-target: Roslyn loads one project flavor per TFM, so the csproj alone
                // cannot tell us which flavor THIS Project instance is — picking the first
                // entry would report e.g. net10.0 for the net8.0 flavor.
                var tfms = doc.Root?.Descendants("TargetFrameworks").FirstOrDefault()?.Value
                    ?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (tfms is { Length: > 0 })
                    return SelectMultiTargetTfm(project.Name, tfms, project.ParseOptions?.PreprocessorSymbolNames);
            }
            catch
            {
                // Fall through to next strategy
            }
        }

        // Strategy 2: Check preprocessor symbols (e.g., NET10_0, NET8_0)
        if (project.ParseOptions?.PreprocessorSymbolNames is { } symbols)
        {
            var detected = DetectFromPreprocessorSymbols(symbols);
            if (detected is not null)
                return detected;
        }

        return "unknown";
    }

    /// <summary>
    /// Picks the flavor-correct TFM for a multi-targeted project.
    /// Preference order: the TFM embedded in Roslyn's flavor project name
    /// ("MyProject(net8.0)" — exact, including OS-specific suffixes), then the
    /// per-flavor preprocessor symbols, then the first list entry as a last resort.
    /// </summary>
    internal static string SelectMultiTargetTfm(
        string projectName,
        string[] tfms,
        IEnumerable<string>? preprocessorSymbols)
    {
        var fromName = ExtractTfmFromProjectName(projectName);
        if (fromName is not null && tfms.Contains(fromName, StringComparer.OrdinalIgnoreCase))
            return fromName;

        if (preprocessorSymbols is not null
            && DetectFromPreprocessorSymbols(preprocessorSymbols) is { } fromSymbols)
        {
            // Symbols only carry the base TFM (net10.0), so match OS-specific list
            // entries (net10.0-windows) by prefix.
            var match = tfms.FirstOrDefault(t =>
                t.Equals(fromSymbols, StringComparison.OrdinalIgnoreCase) ||
                t.StartsWith(fromSymbols + "-", StringComparison.OrdinalIgnoreCase));
            return match ?? fromSymbols;
        }

        return tfms[0];
    }

    /// <summary>
    /// Extracts the TFM from a Roslyn multi-target flavor project name like "MyProject(net8.0)".
    /// Returns null for regular project names.
    /// </summary>
    internal static string? ExtractTfmFromProjectName(string projectName)
    {
        var open = projectName.LastIndexOf('(');
        if (open < 0 || !projectName.EndsWith(')'))
            return null;

        var tfm = projectName[(open + 1)..^1].Trim();
        return tfm.Length > 0 ? tfm : null;
    }

    // Exact TFM symbols: NET10_0, NETSTANDARD2_0, NETCOREAPP3_1. Anchored so compat
    // symbols like NETCOREAPP1_0_OR_GREATER (issue #19) never match.
    private static readonly Regex ExactTfmSymbol =
        new(@"^(NETSTANDARD|NETCOREAPP|NET)(\d+)_(\d+)$", RegexOptions.Compiled);

    // .NET Framework symbols have no underscore: NET48, NET472
    private static readonly Regex FrameworkTfmSymbol = new(@"^NET(\d{2,3})$", RegexOptions.Compiled);

    internal static string? DetectFromPreprocessorSymbols(IEnumerable<string> symbols)
    {
        var symbolList = symbols.ToList();

        // A compilation defines exactly one exact TFM symbol alongside many *_OR_GREATER
        // compat symbols. Only the exact one names the real target framework.
        var exact = symbolList
            .Select(s => ExactTfmSymbol.Match(s))
            .Where(m => m.Success)
            .Select(m => (Prefix: m.Groups[1].Value, Major: int.Parse(m.Groups[2].Value), Minor: int.Parse(m.Groups[3].Value)))
            .OrderByDescending(t => t.Major)
            .ThenByDescending(t => t.Minor)
            .Select(t => (string?)$"{t.Prefix.ToLowerInvariant()}{t.Major}.{t.Minor}")
            .FirstOrDefault();

        if (exact is not null)
            return exact;

        return symbolList
            .Select(s => FrameworkTfmSymbol.Match(s))
            .Where(m => m.Success)
            .Select(m => m.Groups[1].Value)
            .OrderByDescending(v => int.Parse(v.PadRight(3, '0')))
            .Select(v => (string?)$"net{v}")
            .FirstOrDefault();
    }
}
