namespace CWM.RoslynNavigator.Tests.Fixtures;

/// <summary>
/// One known-good and one known-bad argument set per tool against SampleSolution. Shared by
/// the cancellation, snapshot, and end-to-end tests. A tool with no entry here fails those
/// tests loudly, so a new tool cannot slip through with placeholder arguments.
/// </summary>
public static class ToolArguments
{
    private const string StackTrace = """
        System.InvalidOperationException: Order 42 not found
           at SampleApi.OrderService.CancelOrderAsync(Guid id, CancellationToken ct)
        """;

    private static readonly Dictionary<string, Dictionary<string, object?>> ValidArgs = new(StringComparer.Ordinal)
    {
        ["analyze_change_impact"] = new() { ["symbolName"] = "IOrderRepository.GetByIdAsync" },
        ["detect_antipatterns"] = new() { ["file"] = "AntiPatternExamples.cs" },
        ["detect_circular_dependencies"] = new() { ["scope"] = "projects" },
        ["find_callers"] = new() { ["methodName"] = "GetByIdAsync", ["className"] = "IOrderRepository" },
        ["find_dead_code"] = new() { ["scope"] = "project", ["path"] = "SampleDomain" },
        ["find_implementations"] = new() { ["interfaceName"] = "IOrderRepository" },
        ["find_overrides"] = new() { ["methodName"] = "GetByIdAsync", ["className"] = "IOrderRepository" },
        ["find_references"] = new() { ["symbolName"] = "IOrderRepository" },
        ["find_symbol"] = new() { ["name"] = "OrderService" },
        ["get_dependency_graph"] = new() { ["symbolName"] = "OrderService.CancelOrderAsync", ["depth"] = 2 },
        ["get_di_registrations"] = new(),
        ["get_diagnostics"] = new() { ["scope"] = "project", ["path"] = "SampleDomain" },
        ["get_endpoint_map"] = new(),
        ["get_file_outline"] = new() { ["filePath"] = "OrderService.cs" },
        ["get_nuget_packages"] = new(),
        ["get_project_graph"] = new(),
        ["get_public_api"] = new() { ["typeName"] = "Order" },
        ["get_symbol_at"] = new() { ["file"] = "SampleApi/OrderService.cs", ["line"] = 8, ["text"] = "OrderService" },
        ["get_symbol_detail"] = new() { ["symbolName"] = "CreateOrderAsync", ["containingType"] = "OrderService" },
        ["get_symbol_source"] = new() { ["symbolName"] = "Order.Cancel" },
        ["get_test_coverage_map"] = new() { ["projectFilter"] = "SampleDomain" },
        ["get_token_savings"] = new(),
        ["get_type_hierarchy"] = new() { ["typeName"] = "BaseEntity" },
        ["get_workspace_status"] = new(),
        ["preview_rename"] = new() { ["symbolName"] = "Order.Ship", ["newName"] = "Dispatch" },
        ["resolve_stack_trace"] = new() { ["stackTrace"] = StackTrace },
    };

    /// <summary>
    /// Arguments that must produce a structured <c>{"Error": ...}</c> payload. Tools that take
    /// nothing that can be wrong (pure listings) map to null.
    /// </summary>
    private static readonly Dictionary<string, Dictionary<string, object?>?> InvalidArgs = new(StringComparer.Ordinal)
    {
        ["analyze_change_impact"] = new() { ["symbolName"] = "NoSuchSymbol12345" },
        ["detect_antipatterns"] = new() { ["severity"] = "catastrophic" },
        ["detect_circular_dependencies"] = new() { ["scope"] = "galaxies" },
        ["find_callers"] = new() { ["methodName"] = "NoSuchMethod12345" },
        ["find_dead_code"] = new() { ["scope"] = "galaxy" },
        ["find_implementations"] = new() { ["interfaceName"] = "INoSuchInterface12345" },
        ["find_overrides"] = new() { ["methodName"] = "NoSuchMethod12345" },
        ["find_references"] = new() { ["symbolName"] = "NoSuchSymbol12345" },
        ["find_symbol"] = new() { ["name"] = "Order", ["match"] = "telepathic" },
        ["get_dependency_graph"] = new() { ["symbolName"] = "NoSuchMethod12345" },
        ["get_di_registrations"] = new() { ["file"] = "NoSuchFile12345.cs" },
        ["get_diagnostics"] = new() { ["scope"] = "galaxy" },
        ["get_endpoint_map"] = new() { ["file"] = "NoSuchFile12345.cs" },
        ["get_file_outline"] = new() { ["filePath"] = "NoSuchFile12345.cs" },
        ["get_nuget_packages"] = null,
        ["get_project_graph"] = null,
        ["get_public_api"] = new() { ["typeName"] = "NoSuchType12345" },
        ["get_symbol_at"] = new() { ["file"] = "NoSuchFile12345.cs", ["line"] = 1 },
        ["get_symbol_detail"] = new() { ["symbolName"] = "NoSuchSymbol12345" },
        ["get_symbol_source"] = new() { ["symbolName"] = "NoSuchSymbol12345" },
        ["get_test_coverage_map"] = new() { ["projectFilter"] = "NoSuchProject12345" },
        ["get_token_savings"] = null,
        ["get_type_hierarchy"] = new() { ["typeName"] = "NoSuchType12345" },
        ["get_workspace_status"] = null,
        ["preview_rename"] = new() { ["symbolName"] = "Order.Ship", ["newName"] = "not a valid identifier" },
        ["resolve_stack_trace"] = new() { ["stackTrace"] = "something went wrong" },
    };

    public static IReadOnlyDictionary<string, object?> Valid(string tool) =>
        ValidArgs.TryGetValue(tool, out var args)
            ? args
            : throw new KeyNotFoundException($"No valid arguments for '{tool}'. Add it to ToolArguments.");

    public static IReadOnlyDictionary<string, object?>? Invalid(string tool) =>
        InvalidArgs.TryGetValue(tool, out var args)
            ? args
            : throw new KeyNotFoundException($"No invalid arguments for '{tool}'. Add it to ToolArguments.");

    public static IEnumerable<string> Tools => ValidArgs.Keys;
}
