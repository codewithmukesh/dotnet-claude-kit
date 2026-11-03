using System.Text;

namespace CWM.RoslynNavigator.Tests.Scale;

/// <summary>
/// Writes a synthetic SDK-style solution of <c>projectCount</c> projects plus one shared
/// project into a temp folder. Shape, chosen to stress the heavy tools the way a large real
/// repo does:
/// <list type="bullet">
/// <item>Shared declares <c>IWidgetHandler</c>; every project implements it once.</item>
/// <item>Every project references Shared, and projects form reference chains ten deep
/// (P002 → P001, P003 → P002 … P010 → P009, P012 → P011 …), so references cross projects
/// without the O(N²) metadata fan-out of one 500-deep chain.</item>
/// <item>Each project has <see cref="FilesPerProject"/> files: the handler, and services
/// that call each other, call into the previous project in the chain, leave one method
/// unused (dead code), and use DateTime.Now in one file (an anti-pattern finding).</item>
/// <item>Guard Directory.* files so the generated tree never inherits a parent's build
/// configuration.</item>
/// </list>
/// </summary>
public static class LargeSolutionGenerator
{
    public const int FilesPerProject = 25;
    public const int ChainLength = 10;

    /// <summary>Generates the solution and returns the .slnx path.</summary>
    public static string Generate(string root, int projectCount)
    {
        Fixtures.TestDataCopy.WriteGuardFiles(root);

        WriteProject(root, "Shared", references: [], files: new Dictionary<string, string>
        {
            ["IWidgetHandler.cs"] = """
                namespace Shared;

                public interface IWidgetHandler
                {
                    int Handle(int value);
                }

                public static class WidgetRegistry
                {
                    public static int Register(IWidgetHandler handler) => handler.Handle(1);
                }
                """,
        });

        var solution = new StringBuilder("<Solution>\n  <Project Path=\"Shared/Shared.csproj\" />\n");

        for (var p = 1; p <= projectCount; p++)
        {
            var name = ProjectName(p);
            var previous = p % ChainLength == 1 ? null : ProjectName(p - 1);
            List<string> references = previous is null ? ["Shared"] : ["Shared", previous];

            var files = new Dictionary<string, string>
            {
                [$"Handler{p:D3}.cs"] = $$"""
                    using Shared;

                    namespace {{name}};

                    public sealed class Handler{{p:D3}} : IWidgetHandler
                    {
                        public int Handle(int value) => new Service{{p:D3}}_01().ComputeWidgetTotal(value);
                    }
                    """,
            };

            for (var f = 1; f < FilesPerProject; f++)
                files[$"Service{p:D3}_{f:D2}.cs"] = ServiceSource(name, p, f, previous);

            WriteProject(root, name, references, files);
            solution.Append($"  <Project Path=\"{name}/{name}.csproj\" />\n");
        }

        solution.Append("</Solution>\n");
        var solutionPath = Path.Combine(root, $"Large{projectCount}.slnx");
        File.WriteAllText(solutionPath, solution.ToString());
        return solutionPath;
    }

    public static string ProjectName(int index) => $"P{index:D3}";

    private static string ServiceSource(string ns, int p, int f, string? previous)
    {
        var next = f + 1 < FilesPerProject ? $"new Service{p:D3}_{f + 1:D2}().ComputeWidgetTotal(value)" : "value";
        var upstream = previous is not null && f == 1
            ? $" + new {previous}.Service{p - 1:D3}_02().ComputeWidgetTotal(1)"
            : "";
        var clock = f == 3 ? "\n    public DateTime Stamp() => DateTime.Now;\n" : "";

        return $$"""
            using Shared;

            namespace {{ns}};

            public sealed class Service{{p:D3}}_{{f:D2}}
            {
                public int ComputeWidgetTotal(int value) => {{next}}{{upstream}} + Helper(value);

                public int RegisterWith(IWidgetHandler handler) => WidgetRegistry.Register(handler);

                private static int Helper(int value) => value * {{f}};

                private int NeverCalled{{p:D3}}_{{f:D2}}() => {{f}};
            {{clock}}}
            """;
    }

    private static void WriteProject(string root, string name, IReadOnlyList<string> references, Dictionary<string, string> files)
    {
        var directory = Directory.CreateDirectory(Path.Combine(root, name)).FullName;
        var items = string.Concat(references.Select(r => $"    <ProjectReference Include=\"../{r}/{r}.csproj\" />\n"));

        File.WriteAllText(Path.Combine(directory, $"{name}.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
              <ItemGroup>
            {items}  </ItemGroup>
            </Project>
            """);

        foreach (var (file, source) in files)
            File.WriteAllText(Path.Combine(directory, file), source);
    }
}
