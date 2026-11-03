using System.Text;
using System.Text.Json;
using CWM.RoslynNavigator.Bench.Server;

namespace CWM.RoslynNavigator.Bench.Corpus;

/// <summary>
/// Picks the symbols a corpus is benchmarked on, deterministically: text nominates a
/// shortlist, the server scores it, the highest score wins and ties break by name. Run once
/// per corpus; the result is committed so every later run asks the same questions.
/// </summary>
internal sealed class TargetSelector(ServerSession session, SolutionScope scope, TextWriter log)
{
    private const int Shortlist = 12;
    private static readonly TimeSpan CallTimeout = TimeSpan.FromMinutes(5);

    private readonly List<string> _log = [];

    public async Task<CorpusTargets> SelectAsync(string selectedWith, CancellationToken ct)
    {
        log.WriteLine("  indexing source...");
        var index = SourceIndex.Build(scope);

        var iface = await PickMaxAsync("interface", index.TopUnique(index.Interfaces, Shortlist),
            name => ("find_implementations", new() { ["interfaceName"] = name, ["maxResults"] = 1 }), "TotalFound", ct)
            ?? throw new InvalidOperationException("No interface candidate resolved.");

        var classes = await ScoreAllAsync("class", index.TopUnique(index.Classes, Shortlist),
            name => ("find_references", new() { ["symbolName"] = name, ["maxResults"] = 1 }), "TotalFound", ct);
        if (classes.Count == 0) throw new InvalidOperationException("No class candidate resolved.");
        var type = classes[0].Name;
        var typeFile = await FirstFileAsync(type, "class", ct)
            ?? throw new InvalidOperationException($"find_symbol could not locate {type}.");

        var baseClass = await PickMaxAsync("abstract class", index.TopUnique(index.AbstractClasses, Shortlist),
            name => ("get_type_hierarchy", new() { ["typeName"] = name, ["maxResults"] = 1 }), "TotalDerived", ct)
            ?? type;

        var (methodType, method) = await PickMethodAsync([.. classes.Take(3).Select(c => c.Name)], ct);
        var (methodFile, methodLine) = await MethodLocationAsync(methodType, method, ct);

        var (virtualMethod, virtualClass) = await PickVirtualAsync(index.TopVirtuals(8), ct);
        var largeFile = await PickLargeFileAsync(index, ct);
        var (trace, stackMethods) = await BuildStackTraceAsync(methodType, method, ct);

        return new CorpusTargets
        {
            TypeName = type,
            TypeFile = typeFile,
            InterfaceName = iface,
            BaseClassName = baseClass,
            MethodType = methodType,
            MethodName = method,
            MethodFile = methodFile,
            MethodLine = methodLine,
            VirtualMethodName = virtualMethod,
            VirtualMethodClass = virtualClass,
            LargeFile = largeFile,
            DiagnosticsFile = typeFile,
            StackTrace = trace,
            StackMethods = stackMethods,
            SelectedWith = selectedWith,
            SelectionLog = _log,
        };
    }

    private async Task<string?> PickMaxAsync(
        string label,
        List<string> candidates,
        Func<string, (string Tool, Dictionary<string, object?> Args)> call,
        string countProperty,
        CancellationToken ct)
    {
        var scored = await ScoreAllAsync(label, candidates, call, countProperty, ct);
        return scored.Count == 0 ? null : scored[0].Name;
    }

    /// <summary>Scores every candidate with one server call; returns them best first.</summary>
    private async Task<List<(string Name, int Score)>> ScoreAllAsync(
        string label,
        List<string> candidates,
        Func<string, (string Tool, Dictionary<string, object?> Args)> call,
        string countProperty,
        CancellationToken ct)
    {
        var scored = new List<(string Name, int Score)>();
        foreach (var name in candidates)
        {
            var (tool, args) = call(name);
            var result = await session.CallAsync(tool, args, CallTimeout, ct);
            var score = result.Answered ? ReadInt(result.Payload, countProperty) : null;
            Note($"{label} {name}: {tool} {countProperty}={score?.ToString() ?? $"{result.Status} {result.ErrorCode}"}");
            if (score is > 0) scored.Add((name, score.Value));
        }

        return [.. scored.OrderByDescending(s => s.Score).ThenBy(s => s.Name, StringComparer.Ordinal)];
    }

    private async Task<(string Type, string Method)> PickMethodAsync(List<string> types, CancellationToken ct)
    {
        var best = (Type: "", Method: "", Score: -1);
        foreach (var type in types)
        {
            var api = await session.CallAsync("get_public_api", new() { ["typeName"] = type, ["maxResults"] = 200 }, CallTimeout, ct);
            if (!api.Answered) continue;

            foreach (var method in MethodNames(api.Payload).Take(20))
            {
                var callers = await session.CallAsync("find_callers",
                    new() { ["methodName"] = method, ["className"] = type, ["maxResults"] = 1 }, CallTimeout, ct);
                var score = callers.Answered ? ReadInt(callers.Payload, "TotalFound") ?? 0 : 0;
                Note($"method {type}.{method}: find_callers TotalFound={(callers.Answered ? score : callers.Status)}");

                if (score > best.Score
                    || (score == best.Score && string.CompareOrdinal($"{type}.{method}", $"{best.Type}.{best.Method}") < 0))
                {
                    best = (type, method, score);
                }
            }
        }

        if (best.Score < 0) throw new InvalidOperationException("No method candidate resolved.");
        return (best.Type, best.Method);
    }

    private async Task<(string File, int Line)> MethodLocationAsync(string type, string method, CancellationToken ct)
    {
        var found = await session.CallAsync("find_symbol",
            new() { ["name"] = $"{type}.{method}", ["kind"] = "method", ["maxResults"] = 50 }, CallTimeout, ct);
        var first = Symbols(found.Payload, "Symbols").FirstOrDefault();
        if (first.File is null) throw new InvalidOperationException($"find_symbol could not locate {type}.{method}.");
        return (first.File, first.Line);
    }

    private async Task<(string? Method, string? Class)> PickVirtualAsync(
        List<(string Class, string Method)> candidates, CancellationToken ct)
    {
        var best = (Method: (string?)null, Class: (string?)null, Score: 0);
        foreach (var (cls, name) in candidates)
        {
            var result = await session.CallAsync("find_overrides",
                new() { ["methodName"] = name, ["className"] = cls, ["maxResults"] = 1 }, CallTimeout, ct);
            var score = result.Answered ? ReadInt(result.Payload, "TotalFound") ?? 0 : 0;
            Note($"virtual {cls}.{name}: find_overrides TotalFound={(result.Answered ? score : $"{result.Status} {result.ErrorCode}")}");
            if (score > best.Score) best = (name, cls, score);
        }

        return (best.Method, best.Class);
    }

    private async Task<string> PickLargeFileAsync(SourceIndex index, CancellationToken ct)
    {
        foreach (var (path, bytes) in index.Files.Take(10))
        {
            var outline = await session.CallAsync("get_file_outline", new() { ["filePath"] = path, ["maxResults"] = 1 }, CallTimeout, ct);
            Note($"large file {path} ({bytes} bytes): get_file_outline {outline.Status}");
            if (outline.Answered) return path.Replace('\\', '/');
        }

        throw new InvalidOperationException("No large file could be outlined.");
    }

    /// <summary>
    /// A realistic trace: a BCL throw site, the target method, up to three of its real
    /// callers, then runtime frames. No file/line suffixes — a Release build without PDBs —
    /// so resolution must come from symbols, not from a path printed in the trace.
    /// </summary>
    private async Task<(string Trace, List<string> Methods)> BuildStackTraceAsync(string type, string method, CancellationToken ct)
    {
        var callersResult = await session.CallAsync("find_callers",
            new() { ["methodName"] = method, ["className"] = type, ["maxResults"] = 50 }, CallTimeout, ct);

        var callers = new List<(string Method, string Type)>();
        if (callersResult.Answered && callersResult.Payload is not null)
        {
            using var document = JsonDocument.Parse(callersResult.Payload);
            callers = [.. document.RootElement.GetProperty("Callers").EnumerateArray()
                .Where(c => !c.TryGetProperty("IsGenerated", out var g) || !g.GetBoolean())
                .Select(c => (Method: c.GetProperty("Method").GetString()!, Type: c.GetProperty("ContainingType").GetString()!,
                    File: c.GetProperty("File").GetString()!, Line: c.GetProperty("Line").GetInt32()))
                .OrderBy(c => c.File, StringComparer.Ordinal)
                .ThenBy(c => c.Line)
                .Select(c => (MethodNameOnly(c.Method), c.Type))
                .Distinct()
                .Take(3)];
        }

        var frames = new StringBuilder();
        // "\n" rather than AppendLine so the committed trace is identical on every OS.
        frames.Append("System.InvalidOperationException: Sequence contains no elements\n");
        frames.Append("   at System.Linq.ThrowHelper.ThrowNoElementsException()\n");
        frames.Append($"   at {await QualifyAsync(type, ct)}.{method}()\n");
        foreach (var (callerMethod, callerType) in callers)
            frames.Append($"   at {await QualifyAsync(callerType, ct)}.{callerMethod}()\n");
        frames.Append("   at System.Threading.ExecutionContext.RunInternal(ExecutionContext executionContext, ContextCallback callback, Object state)");

        Note($"stack trace: {type}.{method} + {callers.Count} callers");
        return (frames.ToString(), [method, .. callers.Select(c => c.Method)]);
    }

    private async Task<string> QualifyAsync(string type, CancellationToken ct)
    {
        if (type.Contains('.')) return type;
        var found = await session.CallAsync("find_symbol", new() { ["name"] = type, ["kind"] = "class", ["maxResults"] = 5 }, CallTimeout, ct);
        var ns = NamespaceOf(found.Payload);
        return string.IsNullOrEmpty(ns) ? type : $"{ns}.{type}";
    }

    private async Task<string?> FirstFileAsync(string name, string kind, CancellationToken ct)
    {
        var found = await session.CallAsync("find_symbol", new() { ["name"] = name, ["kind"] = kind, ["maxResults"] = 5 }, CallTimeout, ct);
        return Symbols(found.Payload, "Symbols").FirstOrDefault().File;
    }

    private void Note(string line)
    {
        _log.Add(line);
        log.WriteLine("    " + line);
    }

    private static IEnumerable<string> MethodNames(string? payload)
    {
        if (payload is null) yield break;
        using var document = JsonDocument.Parse(payload);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var names = new List<string>();

        foreach (var member in document.RootElement.GetProperty("Members").EnumerateArray())
        {
            var kind = member.GetProperty("Kind").GetString() ?? "";
            if (!kind.Contains("method", StringComparison.OrdinalIgnoreCase)) continue;
            var name = MethodNameOnly(member.GetProperty("Signature").GetString() ?? "");
            if (name.Length > 0 && name != "ToString" && name != "Equals" && name != "GetHashCode" && seen.Add(name))
                names.Add(name);
        }

        foreach (var name in names) yield return name;
    }

    /// <summary>"Task&lt;Order&gt; GetAsync&lt;T&gt;(Guid id)" → "GetAsync".</summary>
    private static string MethodNameOnly(string signature)
    {
        var paren = signature.IndexOf('(');
        var head = paren >= 0 ? signature[..paren] : signature;
        var generic = head.LastIndexOf('<');
        if (generic > 0 && head.EndsWith('>')) head = head[..generic];
        var start = head.LastIndexOfAny([' ', '.']);
        return head[(start + 1)..].Trim();
    }

    private static List<(string? File, int Line)> Symbols(string? payload, string property)
    {
        if (payload is null) return [];
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty(property, out var symbols)) return [];
            return [.. symbols.EnumerateArray()
                .Select(s => (File: s.GetProperty("File").GetString(), Line: s.GetProperty("Line").GetInt32()))
                .OrderBy(s => s.File, StringComparer.Ordinal)
                .ThenBy(s => s.Line)];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? NamespaceOf(string? payload)
    {
        if (payload is null) return null;
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.TryGetProperty("Symbols", out var symbols) && symbols.GetArrayLength() > 0
            ? symbols[0].GetProperty("Namespace").GetString()
            : null;
    }

    private static int? ReadInt(string? payload, string property)
    {
        if (payload is null) return null;
        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.TryGetProperty(property, out var value) && value.TryGetInt32(out var number)
                ? number
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
