using System.Reflection;
using System.Runtime.ExceptionServices;
using ModelContextProtocol.Server;

namespace CWM.RoslynNavigator.Tests.Fixtures;

/// <summary>
/// Every <see cref="McpServerToolAttribute"/> method in the server assembly, discovered by
/// reflection so a newly added tool is covered by the contract, snapshot, and end-to-end
/// tests without anyone remembering to list it.
/// </summary>
public static class ToolCatalog
{
    public static IReadOnlyList<ToolMethod> All { get; } =
    [
        .. typeof(WorkspaceManager).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Select(method => (method, attribute: method.GetCustomAttribute<McpServerToolAttribute>()))
            .Where(x => x.attribute is not null)
            .Select(x => new ToolMethod(x.attribute!.Name ?? x.method.Name, x.method))
            .OrderBy(tool => tool.Name, StringComparer.Ordinal)
    ];

    public static IEnumerable<string> Names => All.Select(tool => tool.Name);

    public static ToolMethod Get(string name) => All.Single(tool => tool.Name == name);

    /// <summary>xUnit data source: one row per tool name.</summary>
    public static TheoryData<string> NameData => [.. Names];
}

/// <summary>One discovered tool and a way to call it the way the MCP SDK binds parameters.</summary>
public sealed record ToolMethod(string Name, MethodInfo Method)
{
    /// <summary>
    /// Invokes the tool. Injected services (WorkspaceManager, IServiceProvider,
    /// CancellationToken) are supplied; named <paramref name="args"/> win; optional parameters
    /// keep their declared default; any other required parameter gets a type-derived
    /// placeholder ("X" for strings, 1 for numbers, false for booleans).
    /// </summary>
    public async Task<string> InvokeAsync(
        WorkspaceManager workspace,
        IReadOnlyDictionary<string, object?>? args = null,
        CancellationToken ct = default,
        IServiceProvider? services = null)
    {
        var parameters = Method.GetParameters();
        var values = new object?[parameters.Length];

        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            values[i] = parameter.ParameterType switch
            {
                var t when t == typeof(WorkspaceManager) => workspace,
                var t when t == typeof(CancellationToken) => ct,
                var t when t == typeof(IServiceProvider) => services ?? EmptyServiceProvider.Instance,
                _ when args is not null && args.TryGetValue(parameter.Name!, out var value) => value,
                _ when parameter.HasDefaultValue => parameter.DefaultValue,
                var t => Placeholder(t),
            };
        }

        Task<string> task;
        try
        {
            task = (Task<string>)Method.Invoke(null, values)!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }

        return await task;
    }

    /// <summary>The parameters an MCP client supplies (everything the server does not inject).</summary>
    public IEnumerable<ParameterInfo> ClientParameters => Method.GetParameters()
        .Where(p => p.ParameterType != typeof(WorkspaceManager)
                    && p.ParameterType != typeof(CancellationToken)
                    && p.ParameterType != typeof(IServiceProvider));

    public static object? Placeholder(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is not null)
            return null;

        return type switch
        {
            _ when type == typeof(string) => "X",
            _ when type == typeof(int) => 1,
            _ when type == typeof(bool) => false,
            _ => throw new NotSupportedException(
                $"No placeholder for parameter type {type}. Extend ToolMethod.Placeholder for the new tool."),
        };
    }

    public override string ToString() => Name;

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new();

        public object? GetService(Type serviceType) => null;
    }
}
