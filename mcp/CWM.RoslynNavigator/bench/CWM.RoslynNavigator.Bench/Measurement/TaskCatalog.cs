using System.Text.RegularExpressions;
using CWM.RoslynNavigator.Bench.Corpus;

namespace CWM.RoslynNavigator.Bench.Measurement;

/// <summary>
/// The twelve tasks, each mirroring something an agent does daily. Search patterns are what
/// a competent agent would type into Grep — specific enough to be useful, not tuned to make
/// the manual route look bad.
/// </summary>
internal static class TaskCatalog
{
    private const string Cs = "*.cs";

    public static List<TaskCase> For(CorpusTargets t)
    {
        var type = Regex.Escape(t.TypeName);
        var iface = Regex.Escape(t.InterfaceName);
        var method = Regex.Escape(t.MethodName);
        var baseClass = Regex.Escape(t.BaseClassName);
        var stackMethods = string.Join('|', t.StackMethods.Distinct().Select(Regex.Escape));

        return
        [
            new("locate-type", $"Locate the definition of {t.TypeName}", "find_symbol",
                $@"\b(class|record|struct|interface|enum)\s+{type}\b", Cs, BaselineSource.ResponseFiles, []),
            new("interface-usages", $"Find all usages of {t.InterfaceName}", "find_references",
                $@"\b{iface}\b", Cs, BaselineSource.ResponseFiles, []),
            new("implementations", $"List implementations of {t.InterfaceName}", "find_implementations",
                $@"[:,]\s*{iface}\b", Cs, BaselineSource.ResponseFiles, []),
            new("outline-file", $"Outline {Path.GetFileName(t.LargeFile)} (largest file)", "get_file_outline",
                null, Cs, BaselineSource.FixedFiles, [t.LargeFile]),
            new("read-method", $"Read the body of {t.MethodType}.{t.MethodName}", "get_symbol_source",
                $@"\b{method}\s*\(", Cs, BaselineSource.FixedFiles, [t.MethodFile]),
            new("find-callers", $"Find callers of {t.MethodType}.{t.MethodName}", "find_callers",
                $@"\b{method}\s*\(", Cs, BaselineSource.ResponseFiles, []),
            new("type-hierarchy", $"Type hierarchy of {t.BaseClassName}", "get_type_hierarchy",
                $@":\s*[^;{{]*\b{baseClass}\b", Cs, BaselineSource.ResponseFiles, []),
            new("project-graph", "Project dependency graph", "get_project_graph",
                "<ProjectReference", "*.csproj", BaselineSource.ResponseProjectPaths, []),
            new("di-registrations", "DI registrations", "get_di_registrations",
                @"\.(TryAdd|Add)(Keyed)?(Scoped|Transient|Singleton|Enumerable)\b", Cs, BaselineSource.ResponseFiles, []),
            new("endpoint-map", "HTTP endpoint map", "get_endpoint_map",
                @"\.Map(Get|Post|Put|Delete|Patch|Methods|Group)\s*\(|\[(Http(Get|Post|Put|Delete|Patch)|Route)\b",
                Cs, BaselineSource.ResponseFiles, []),
            new("stack-trace", "Resolve a stack trace to source", "resolve_stack_trace",
                $@"\b({stackMethods})\s*\(", Cs, BaselineSource.ResponseFiles, []),
            new("file-diagnostics", $"Diagnostics for {Path.GetFileName(t.DiagnosticsFile)}", "get_diagnostics",
                null, Cs, BaselineSource.FixedFiles, [t.DiagnosticsFile]),
        ];
    }
}
