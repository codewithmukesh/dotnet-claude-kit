using System.Text.Json;

namespace CWM.RoslynNavigator.Stats;

/// <summary>Measured spend and navigation-tool usage for one repository.</summary>
internal sealed class TranscriptStats
{
    public string RepoPath { get; init; } = "";
    public HashSet<string> Sessions { get; } = [];

    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheCreationTokens { get; set; }
    public long CacheReadTokens { get; set; }

    /// <summary>Calls to this server's MCP tools.</summary>
    public int RoslynCalls { get; set; }

    /// <summary>Read/Grep/Glob calls that demonstrably targeted C#.</summary>
    public int CSharpFileCalls { get; set; }

    public Dictionary<string, int> RoslynByTool { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Share of C#-navigation work that went through the MCP rather than raw file reads.
    /// Null when the server was never used here, because a repo without it configured
    /// belongs in no denominator — including such repos would drag the aggregate toward
    /// zero for reasons that have nothing to do with the tools.
    /// </summary>
    public double? AdoptionRate =>
        RoslynCalls == 0 ? null : (double)RoslynCalls / (RoslynCalls + CSharpFileCalls);
}

/// <summary>
/// Reads Claude Code transcripts for real, measured token spend and for how much C#
/// navigation actually went through this server.
///
/// <para>Telemetry can only report on calls that were made. Only the transcripts can say
/// what share of the work the tools captured — and low adoption in a configured repo is
/// usually a more actionable finding than the savings number, because it means a tool
/// description is failing to attract the calls it should.</para>
/// </summary>
internal sealed class TranscriptMiner(string transcriptRoot)
{
    private const string McpToolPrefix = "mcp__cwm-roslyn-navigator__";

    /// <summary>Tools whose C#-targeting calls this server could have replaced.</summary>
    private static readonly HashSet<string> FileAccessTools =
        new(StringComparer.Ordinal) { "Read", "Grep", "Glob" };

    public IReadOnlyList<TranscriptStats> Mine(CancellationToken ct = default)
    {
        if (!Directory.Exists(transcriptRoot)) return [];

        var byRepo = new Dictionary<string, TranscriptStats>(StringComparer.Ordinal);

        foreach (var directory in Directory.EnumerateDirectories(transcriptRoot))
        {
            ct.ThrowIfCancellationRequested();

            var fallbackPath = RepoPath.FromTranscriptDirectoryName(Path.GetFileName(directory));

            foreach (var file in Directory.EnumerateFiles(directory, "*.jsonl"))
            {
                try
                {
                    MineFile(file, fallbackPath, byRepo, ct);
                }
                catch (IOException)
                {
                    // A transcript being written live can be briefly unreadable.
                }
            }
        }

        return byRepo.Values.OrderByDescending(s => s.RoslynCalls).ToList();
    }

    private void MineFile(
        string file,
        string? fallbackPath,
        Dictionary<string, TranscriptStats> byRepo,
        CancellationToken ct)
    {
        foreach (var line in File.ReadLines(file))
        {
            ct.ThrowIfCancellationRequested();

            if (line.Length == 0) continue;

            // Most lines are neither usage nor tool calls. A substring test skips them for
            // far less than a parse — transcripts routinely run to tens of megabytes.
            var hasUsage = line.Contains("\"usage\"", StringComparison.Ordinal);
            var hasToolUse = line.Contains("\"tool_use\"", StringComparison.Ordinal);
            if (!hasUsage && !hasToolUse) continue;

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                // Expected on the trailing line of a transcript that is still being written.
                continue;
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) continue;

                if (!TryGetString(root, "type", out var type) || type != "assistant") continue;

                // Subagent turns are real spend and real tool use, so they are counted.
                var repoKey = ResolveRepoKey(root, fallbackPath);
                if (repoKey is null) continue;

                if (!byRepo.TryGetValue(repoKey, out var stats))
                {
                    stats = new TranscriptStats { RepoPath = repoKey };
                    byRepo[repoKey] = stats;
                }

                if (TryGetString(root, "sessionId", out var session)) stats.Sessions.Add(session);

                if (root.TryGetProperty("message", out var message)
                    && message.ValueKind == JsonValueKind.Object)
                {
                    AccumulateUsage(message, stats);
                    AccumulateToolCalls(message, stats);
                }
            }
        }
    }

    /// <summary>
    /// Prefers the cwd recorded on the line itself. The directory-name decode is lossy and
    /// only stands in when no line carried one.
    /// </summary>
    private static string? ResolveRepoKey(JsonElement root, string? fallbackPath)
    {
        if (TryGetString(root, "cwd", out var cwd) && cwd.Length > 0)
            return RepoPath.Normalize(cwd);

        return fallbackPath is null ? null : RepoPath.Normalize(fallbackPath);
    }

    private static void AccumulateUsage(JsonElement message, TranscriptStats stats)
    {
        if (!message.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
            return;

        // Real accounting from the API response, not an estimate. Cache reads are billed
        // differently from fresh input, so they are kept apart rather than summed.
        stats.InputTokens += GetInt64(usage, "input_tokens");
        stats.OutputTokens += GetInt64(usage, "output_tokens");
        stats.CacheCreationTokens += GetInt64(usage, "cache_creation_input_tokens");
        stats.CacheReadTokens += GetInt64(usage, "cache_read_input_tokens");
    }

    private static void AccumulateToolCalls(JsonElement message, TranscriptStats stats)
    {
        if (!message.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Array)
            return;

        foreach (var block in content.EnumerateArray())
        {
            if (block.ValueKind != JsonValueKind.Object) continue;
            if (!TryGetString(block, "type", out var blockType) || blockType != "tool_use") continue;
            if (!TryGetString(block, "name", out var name)) continue;

            if (name.StartsWith(McpToolPrefix, StringComparison.Ordinal))
            {
                stats.RoslynCalls++;
                var shortName = name[McpToolPrefix.Length..];
                stats.RoslynByTool[shortName] = stats.RoslynByTool.GetValueOrDefault(shortName) + 1;
                continue;
            }

            if (FileAccessTools.Contains(name)
                && block.TryGetProperty("input", out var input)
                && TargetsCSharp(name, input))
            {
                stats.CSharpFileCalls++;
            }
        }
    }

    /// <summary>
    /// Whether a file-access call was aimed at C#. Deliberately requires evidence: a Grep
    /// with no language filter is not counted.
    ///
    /// <para>Note the bias this creates. These calls form the denominator of the adoption
    /// rate, so excluding ambiguous ones makes adoption look <i>better</i> than it is. The
    /// report says so rather than letting the reader assume otherwise.</para>
    /// </summary>
    private static bool TargetsCSharp(string tool, JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object) return false;

        if (tool == "Read")
            return TryGetString(input, "file_path", out var path) && IsCSharpPath(path);

        // Grep and Glob: a glob, a type filter, or a path that names C# explicitly.
        if (TryGetString(input, "glob", out var glob) && glob.Contains(".cs", StringComparison.OrdinalIgnoreCase))
            return true;

        if (TryGetString(input, "type", out var type)
            && (type.Equals("csharp", StringComparison.OrdinalIgnoreCase)
                || type.Equals("cs", StringComparison.OrdinalIgnoreCase)))
            return true;

        if (TryGetString(input, "pattern", out var pattern)
            && pattern.Contains(".cs", StringComparison.OrdinalIgnoreCase))
            return true;

        return TryGetString(input, "path", out var searchPath) && IsCSharpPath(searchPath);
    }

    private static bool IsCSharpPath(string path) =>
        path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetString(JsonElement element, string name, out string value)
    {
        if (element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString() ?? "";
            return true;
        }

        value = "";
        return false;
    }

    private static long GetInt64(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.Number
        && property.TryGetInt64(out var value)
            ? value
            : 0;
}
