using CWM.RoslynNavigator.Bench.Corpus;

namespace CWM.RoslynNavigator.Bench.Measurement;

/// <summary>
/// Every tool, called once per corpus with arguments built from the committed targets.
/// Ordered cheap to expensive so a slow whole-solution scan cannot delay the rest.
/// <c>get_token_savings</c> is left out: it reports on telemetry, which the bench disables.
/// Methods are named type-qualified where a tool accepts it, as the tool descriptions advise;
/// file/line hints are passed too, exactly as the server reported them.
/// </summary>
internal static class ToolCatalog
{
    public static List<ToolCase> For(CorpusTargets t)
    {
        List<ToolCase> cases =
        [
            new("find_symbol", new() { ["name"] = t.TypeName, ["kind"] = "class", ["maxResults"] = 50 }),
            new("find_references", new() { ["symbolName"] = t.InterfaceName, ["maxResults"] = 100 }),
            new("find_implementations", new() { ["interfaceName"] = t.InterfaceName, ["maxResults"] = 100 }),
            new("find_callers", new() { ["methodName"] = t.MethodName, ["className"] = t.MethodType, ["maxResults"] = 100 }),
        ];

        if (t.VirtualMethodName is not null)
        {
            cases.Add(new("find_overrides", new()
            {
                ["methodName"] = t.VirtualMethodName,
                ["className"] = t.VirtualMethodClass,
                ["maxResults"] = 100,
            }));
        }

        cases.AddRange(
        [
            new("get_type_hierarchy", new() { ["typeName"] = t.BaseClassName, ["maxResults"] = 100 }),
            new("get_public_api", new() { ["typeName"] = t.TypeName, ["maxResults"] = 100 }),
            new("get_symbol_detail", new() { ["symbolName"] = t.MethodName, ["containingType"] = t.MethodType }),
            new("get_symbol_source", new()
            {
                ["symbolName"] = t.MethodName,
                ["containingType"] = t.MethodType,
                ["file"] = t.MethodFile,
                ["line"] = t.MethodLine,
            }),
            new("get_file_outline", new() { ["filePath"] = t.LargeFile, ["maxResults"] = 500 }),
            new("get_project_graph", []),
            new("get_dependency_graph", new()
            {
                ["symbolName"] = $"{t.MethodType}.{t.MethodName}",
                ["file"] = t.MethodFile,
                ["line"] = t.MethodLine,
                ["depth"] = 2,
            }),
            new("detect_circular_dependencies", new() { ["scope"] = "projects" }),
            new("get_diagnostics", new() { ["scope"] = "file", ["path"] = t.DiagnosticsFile, ["maxResults"] = 100 }),
            new("resolve_stack_trace", new() { ["stackTrace"] = t.StackTrace, ["maxResults"] = 50 }),
            new("get_nuget_packages", new() { ["maxResults"] = 100 }),
            new("analyze_change_impact", new()
            {
                ["symbolName"] = $"{t.MethodType}.{t.MethodName}",
                ["file"] = t.MethodFile,
                ["line"] = t.MethodLine,
                ["depth"] = 2,
            }),
            new("get_di_registrations", new() { ["maxResults"] = 100 }),
            new("get_endpoint_map", new() { ["maxResults"] = 100 }),
            new("get_test_coverage_map", new() { ["maxResults"] = 50 }),
            new("detect_antipatterns", new() { ["maxResults"] = 100 }),
            new("find_dead_code", new() { ["scope"] = "solution", ["maxResults"] = 50 }),
        ]);

        return cases;
    }
}
