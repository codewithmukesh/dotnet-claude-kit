using CWM.RoslynNavigator.Stats;

namespace CWM.RoslynNavigator.Tests.Stats;

/// <summary>
/// Exercised against handcrafted fixture transcripts. Real transcripts are never committed:
/// they contain prompts, file contents, and paths — PII by any reasonable reading.
/// </summary>
public sealed class TranscriptMinerTests : IDisposable
{
    private readonly string _root;

    public TranscriptMinerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cwm-tr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    private void WriteTranscript(string directoryName, string fileName, params string[] lines)
    {
        var dir = Path.Combine(_root, directoryName);
        Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir, fileName), lines);
    }

    private const string DefaultUsage =
        """{"input_tokens":10,"output_tokens":20,"cache_creation_input_tokens":5,"cache_read_input_tokens":7}""";

    // Built by concatenation rather than interpolated raw strings: JSON is mostly braces,
    // and the escaping needed to embed it in an interpolated literal obscures the fixture.
    private static string Assistant(
        string cwd, string session, string contentJson, string usageJson = DefaultUsage) =>
        "{\"type\":\"assistant\",\"cwd\":\"" + cwd
        + "\",\"sessionId\":\"" + session
        + "\",\"isSidechain\":false,\"message\":{\"usage\":" + usageJson
        + ",\"content\":" + contentJson + "}}";

    private static string ToolUse(string name, string inputJson) =>
        "[{\"type\":\"tool_use\",\"id\":\"t1\",\"name\":\"" + name
        + "\",\"input\":" + inputJson + "}]";

    private IReadOnlyList<TranscriptStats> Mine() => new TranscriptMiner(_root).Mine();

    [Fact]
    public void Mine_UsesCwdFromLineContent_NotTheMangledDirectoryName()
    {
        // The directory name cannot be decoded reliably — the hyphens in "dotnet-claude-kit"
        // are indistinguishable from path separators. The cwd field is authoritative.
        WriteTranscript(
            "C--Users-mukesh-repos-codewithmukesh-dotnet-claude-kit", "s1.jsonl",
            Assistant("C:\\\\Users\\\\mukesh\\\\repos\\\\codewithmukesh\\\\dotnet-claude-kit", "s1",
                ToolUse("mcp__cwm-roslyn-navigator__find_symbol", """{"name":"Order"}""")));

        var stats = Assert.Single(Mine());

        Assert.Equal("c:/users/mukesh/repos/codewithmukesh/dotnet-claude-kit", stats.RepoPath);
        Assert.Equal(1, stats.RoslynCalls);
    }

    [Fact]
    public void Mine_SeveralAssistantTurns_AccumulatesMeasuredUsage()
    {
        WriteTranscript("proj", "s1.jsonl",
            Assistant("/repo/a", "s1", ToolUse("Read", """{"file_path":"/repo/a/X.cs"}""")),
            Assistant("/repo/a", "s1", ToolUse("Read", """{"file_path":"/repo/a/Y.cs"}""")));

        var stats = Assert.Single(Mine());

        Assert.Equal(20, stats.InputTokens);
        Assert.Equal(40, stats.OutputTokens);
        Assert.Equal(10, stats.CacheCreationTokens);
        Assert.Equal(14, stats.CacheReadTokens);
        Assert.Single(stats.Sessions);
    }

    [Fact]
    public void Mine_RoslynToolUses_CountsCallsPerTool()
    {
        WriteTranscript("proj", "s1.jsonl",
            Assistant("/repo/a", "s1", ToolUse("mcp__cwm-roslyn-navigator__find_symbol", "{}")),
            Assistant("/repo/a", "s1", ToolUse("mcp__cwm-roslyn-navigator__find_symbol", "{}")),
            Assistant("/repo/a", "s1", ToolUse("mcp__cwm-roslyn-navigator__find_references", "{}")));

        var stats = Assert.Single(Mine());

        Assert.Equal(3, stats.RoslynCalls);
        Assert.Equal(2, stats.RoslynByTool["find_symbol"]);
        Assert.Equal(1, stats.RoslynByTool["find_references"]);
    }

    [Fact]
    public void Mine_AdoptionRate_IsRoslynShareOfCSharpNavigation()
    {
        WriteTranscript("proj", "s1.jsonl",
            Assistant("/repo/a", "s1", ToolUse("mcp__cwm-roslyn-navigator__find_symbol", "{}")),
            Assistant("/repo/a", "s1", ToolUse("mcp__cwm-roslyn-navigator__find_symbol", "{}")),
            Assistant("/repo/a", "s1", ToolUse("mcp__cwm-roslyn-navigator__find_symbol", "{}")),
            Assistant("/repo/a", "s1", ToolUse("Read", """{"file_path":"/repo/a/X.cs"}""")));

        var stats = Assert.Single(Mine());

        Assert.Equal(3, stats.RoslynCalls);
        Assert.Equal(1, stats.CSharpFileCalls);
        Assert.Equal(0.75, stats.AdoptionRate);
    }

    [Fact]
    public void Mine_NonCSharpReads_AreNotInTheDenominator()
    {
        // Reading a markdown file is not work this server could have replaced.
        WriteTranscript("proj", "s1.jsonl",
            Assistant("/repo/a", "s1", ToolUse("mcp__cwm-roslyn-navigator__find_symbol", "{}")),
            Assistant("/repo/a", "s1", ToolUse("Read", """{"file_path":"/repo/a/README.md"}""")),
            Assistant("/repo/a", "s1", ToolUse("Read", """{"file_path":"/repo/a/app.json"}""")));

        var stats = Assert.Single(Mine());

        Assert.Equal(0, stats.CSharpFileCalls);
        Assert.Equal(1.0, stats.AdoptionRate);
    }

    [Theory]
    [InlineData("Grep", """{"pattern":"class Order","glob":"**/*.cs"}""", 1)]
    [InlineData("Grep", """{"pattern":"class Order","type":"csharp"}""", 1)]
    [InlineData("Glob", """{"pattern":"src/**/*.cs"}""", 1)]
    [InlineData("Grep", """{"pattern":"TODO"}""", 0)]
    [InlineData("Grep", """{"pattern":"x","glob":"**/*.ts"}""", 0)]
    public void Mine_SearchCalls_CountOnlyWithExplicitCSharpEvidence(
        string tool, string input, int expected)
    {
        WriteTranscript("proj", "s1.jsonl", Assistant("/repo/a", "s1", ToolUse(tool, input)));

        var stats = Assert.Single(Mine());

        // An unfiltered Grep is not counted. That biases adoption upward, which the report
        // discloses rather than hides.
        Assert.Equal(expected, stats.CSharpFileCalls);
    }

    [Fact]
    public void Mine_RepoWithoutRoslynCalls_HasNoAdoptionRate()
    {
        WriteTranscript("proj", "s1.jsonl",
            Assistant("/repo/a", "s1", ToolUse("Read", """{"file_path":"/repo/a/X.cs"}""")));

        var stats = Assert.Single(Mine());

        // The server was never used here, so this repo belongs in no adoption denominator.
        Assert.Null(stats.AdoptionRate);
    }

    [Fact]
    public void Mine_SessionsInSeveralFiles_GroupsByRepo()
    {
        WriteTranscript("proj", "s1.jsonl",
            Assistant("/repo/a", "s1", ToolUse("mcp__cwm-roslyn-navigator__find_symbol", "{}")));
        WriteTranscript("proj", "s2.jsonl",
            Assistant("/repo/a", "s2", ToolUse("mcp__cwm-roslyn-navigator__find_symbol", "{}")));

        var stats = Assert.Single(Mine());

        Assert.Equal(2, stats.Sessions.Count);
        Assert.Equal(2, stats.RoslynCalls);
    }

    [Fact]
    public void Mine_MalformedAndPartialLines_AreSkipped()
    {
        WriteTranscript("proj", "s1.jsonl",
            Assistant("/repo/a", "s1", ToolUse("mcp__cwm-roslyn-navigator__find_symbol", "{}")),
            "",
            "not json at all",
            """{"type":"assistant","message":{"usage":""",   // partially flushed trailing line
            """{"type":"file-history-delta","usage":"ignored"}""");

        var stats = Assert.Single(Mine());

        Assert.Equal(1, stats.RoslynCalls);
        Assert.Equal(10, stats.InputTokens);
    }

    [Fact]
    public void Mine_NoTranscriptDirectory_ReturnsNothing()
    {
        Assert.Empty(new TranscriptMiner(Path.Combine(_root, "missing"))
            .Mine(TestContext.Current.CancellationToken));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
