using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CWM.RoslynNavigator.Responses;
using ModelContextProtocol.Server;

namespace CWM.RoslynNavigator.Tools;

[McpServerToolType]
public static class GetProjectGraphTool
{
    [McpServerTool(Name = "get_project_graph"), Description("Get the solution project dependency tree with names, paths, target frameworks, and project references. When several solutions are loaded, also lists them (Solutions) and which solution(s) each project belongs to.")]
    public static async Task<string> ExecuteAsync(
        WorkspaceManager workspace,
        CancellationToken ct = default)
    {
        var notReady = await workspace.EnsureReadyOrStatusAsync(ct);
        if (notReady is not null) return notReady;

        var solution = workspace.GetSolution();
        if (solution is null)
            return JsonSerializer.Serialize(new ProjectGraphResult("unknown", []));

        // Membership is noise with one solution, so single-solution output is unchanged.
        var multiSolution = workspace.LoadedSolutions.Count > 1;

        var projects = solution.Projects.Select(project =>
        {
            var references = project.ProjectReferences
                .Select(r => solution.GetProject(r.ProjectId)?.Name ?? "unknown")
                .ToList();

            var targetFramework = DetectTargetFramework(project);

            return new ProjectInfo(
                Name: project.Name,
                Path: project.FilePath ?? "unknown",
                TargetFramework: targetFramework,
                References: references,
                Solutions: multiSolution ? workspace.GetSolutionsForProject(project.FilePath) : null);
        }).ToList();

        var solutionName = workspace.SolutionFilePath is { } primary
            ? Path.GetFileName(primary)
            : solution.FilePath is not null ? Path.GetFileName(solution.FilePath) : "unknown";

        var solutions = multiSolution
            ? workspace.LoadedSolutions
                .Select(s => new SolutionSummary(s.Name, workspace.ToRelativePath(s.Path), s.ProjectPaths.Count, s.Error))
                .ToList()
            : null;

        return JsonSerializer.Serialize(new ProjectGraphResult(solutionName, projects, solutions));
    }

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
