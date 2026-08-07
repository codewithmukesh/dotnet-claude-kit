using System.ComponentModel;
using System.Text.Json;
using System.Xml.Linq;
using CWM.RoslynNavigator.Responses;
using ModelContextProtocol.Server;

namespace CWM.RoslynNavigator.Tools;

[McpServerToolType]
public static class GetNugetPackagesTool
{
    private const int MaxPropsSearchDepth = 12;

    [McpServerTool(Name = "get_nuget_packages"), Description("List the NuGet PackageReference inventory per project: package id, version (resolved from the csproj or Directory.Packages.props when central package management is used), and target framework. Reports what is referenced on disk — no network calls, no latest-version lookups.")]
    public static async Task<string> ExecuteAsync(
        WorkspaceManager workspace,
        [Description("Optional: project name filter")] string? projectFilter = null,
        [Description("Maximum package entries to return across all projects. TotalFound in the response reports the full count; re-query with a higher value if it exceeds Count.")] int maxResults = 100,
        CancellationToken ct = default)
    {
        var notReady = await workspace.EnsureReadyOrStatusAsync(ct);
        if (notReady is not null) return notReady;

        var solution = workspace.GetSolution();
        if (solution is null)
            return JsonSerializer.Serialize(new NugetPackagesResult([], 0, 0, false, Math.Max(1, maxResults)));

        maxResults = Math.Max(1, maxResults);

        var results = new List<ProjectPackages>();
        var seenProjectFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var returned = 0;
        var total = 0;

        foreach (var project in solution.Projects)
        {
            ct.ThrowIfCancellationRequested();

            if (project.FilePath is null || !File.Exists(project.FilePath))
                continue;

            // Multi-target flavors share one csproj — report it once
            if (!seenProjectFiles.Add(project.FilePath))
                continue;

            if (projectFilter is not null
                && !project.Name.Equals(projectFilter, StringComparison.OrdinalIgnoreCase)
                && !project.Name.StartsWith(projectFilter + "(", StringComparison.OrdinalIgnoreCase))
                continue;

            var (packages, cpm) = ParseProjectPackages(project.FilePath);
            total += packages.Count;

            var kept = packages.Take(Math.Max(0, maxResults - returned)).ToList();
            returned += kept.Count;

            results.Add(new ProjectPackages(
                Name: project.Name,
                TargetFramework: GetProjectGraphTool.DetectTargetFramework(project),
                Cpm: cpm,
                Packages: kept));
        }

        // The cap applies to packages across projects, so truncation is measured on the
        // package count rather than the project list this tool returns.
        return JsonSerializer.Serialize(new NugetPackagesResult(
            results, returned, total, total > returned, maxResults));
    }

    /// <summary>
    /// Parses PackageReference items from a csproj, resolving versions from
    /// Directory.Packages.props when the project uses central package management.
    /// </summary>
    internal static (List<PackageRef> Packages, bool Cpm) ParseProjectPackages(string csprojPath)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Load(csprojPath);
        }
        catch
        {
            return ([], false);
        }

        var (cpmVersions, cpmEnabled) = LoadCentralPackageVersions(Path.GetDirectoryName(csprojPath));

        var packages = new List<PackageRef>();
        foreach (var reference in doc.Descendants("PackageReference"))
        {
            var id = reference.Attribute("Include")?.Value ?? reference.Attribute("Update")?.Value;
            if (string.IsNullOrEmpty(id))
                continue;

            var version = reference.Attribute("Version")?.Value
                ?? reference.Element("Version")?.Value
                ?? reference.Attribute("VersionOverride")?.Value;

            if (version is null && cpmEnabled && cpmVersions.TryGetValue(id, out var central))
                version = central;

            packages.Add(new PackageRef(id, version));
        }

        return (packages, cpmEnabled);
    }

    /// <summary>
    /// Walks up from the project directory looking for Directory.Packages.props.
    /// CPM counts as enabled when the file sets ManagePackageVersionsCentrally=true, or —
    /// as a best-effort fallback when the property lives elsewhere — when the file
    /// declares PackageVersion items.
    /// </summary>
    private static (Dictionary<string, string> Versions, bool Enabled) LoadCentralPackageVersions(string? startDirectory)
    {
        var directory = startDirectory;

        for (var i = 0; i < MaxPropsSearchDepth && directory is not null; i++)
        {
            var propsPath = Path.Combine(directory, "Directory.Packages.props");
            if (File.Exists(propsPath))
            {
                try
                {
                    var doc = XDocument.Load(propsPath);

                    var versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var pv in doc.Descendants("PackageVersion"))
                    {
                        var id = pv.Attribute("Include")?.Value;
                        var version = pv.Attribute("Version")?.Value ?? pv.Element("Version")?.Value;
                        if (!string.IsNullOrEmpty(id) && version is not null)
                            versions.TryAdd(id, version);
                    }

                    var managed = doc.Descendants("ManagePackageVersionsCentrally").FirstOrDefault()?.Value.Trim();
                    var enabled = managed is not null
                        ? managed.Equals("true", StringComparison.OrdinalIgnoreCase)
                        : versions.Count > 0;

                    return (versions, enabled);
                }
                catch
                {
                    return ([], false);
                }
            }

            directory = Path.GetDirectoryName(directory);
        }

        return ([], false);
    }
}
