using CWM.RoslynNavigator.Stats;

namespace CWM.RoslynNavigator.Tests.Stats;

public class RepoPathTests
{
    [Theory]
    [InlineData(@"C:\repo\app", "c:/repo/app")]
    [InlineData("/repo/app/", "/repo/app")]
    [InlineData("C:/Repo/App", "c:/repo/app")]
    public void Normalize_PathVariants_BecomeComparable(string input, string expected)
    {
        Assert.Equal(expected, RepoPath.Normalize(input));
    }

    [Theory]
    [InlineData(@"C:\repo\app", "c:/repo/app", true)]
    [InlineData(@"C:\repo\app", "C:/repo/app/src/Api", true)]   // session ran in a subdirectory
    [InlineData("/repo/app", "/repo/app-legacy", false)]        // segment-aware, not prefix
    [InlineData("/repo/app", "/repo/other", false)]
    [InlineData("", "/repo/app", false)]
    public void SameRepository_MatchesOnSegmentBoundaries(string left, string right, bool expected)
    {
        Assert.Equal(expected, RepoPath.SameRepository(left, right));
    }

    [Theory]
    [InlineData("C--Users-mukesh-repos-app", "C:/Users/mukesh/repos/app")]
    [InlineData("-home-mukesh-repos-app", "/home/mukesh/repos/app")]
    [InlineData("nonsense", null)]
    public void FromTranscriptDirectoryName_EncodedPath_DecodesBestEffort(string input, string? expected)
    {
        Assert.Equal(expected, RepoPath.FromTranscriptDirectoryName(input));
    }

    [Fact]
    public void FromTranscriptDirectoryName_HyphenatedName_CannotBeRecovered()
    {
        // Documents why cwd from the line content is authoritative: the encoding is lossy,
        // and a real repository name containing a hyphen decodes wrongly.
        var decoded = RepoPath.FromTranscriptDirectoryName("C--Users-mukesh-repos-dotnet-claude-kit");

        Assert.Equal("C:/Users/mukesh/repos/dotnet/claude/kit", decoded);
        Assert.NotEqual("C:/Users/mukesh/repos/dotnet-claude-kit", decoded);
    }
}

public class ReportBuilderTests
{
    private static RepoTelemetry Telemetry(string solutionPath, params CallRecord[] calls) => new()
    {
        Info = new RepoInfo
        {
            SolutionPath = solutionPath,
            Name = RepoPath.DisplayName(solutionPath),
            TotalSourceTokens = 10_000,
            BytesPerToken = 3.5
        },
        Calls = calls
    };

    private static CallRecord Call(string tool, int baseline = 1000, int response = 100, int results = 1) => new()
    {
        Tool = tool,
        Ok = true,
        State = "ok",
        BaselineTokens = baseline,
        ResponseTokens = response,
        Results = results,
        ElapsedMs = 10
    };

    private static TranscriptStats Transcript(string repoPath, int roslyn, int csharp)
    {
        var stats = new TranscriptStats { RepoPath = RepoPath.Normalize(repoPath) };
        stats.RoslynCalls = roslyn;
        stats.CSharpFileCalls = csharp;
        stats.Sessions.Add("s1");
        return stats;
    }

    [Fact]
    public void Build_TelemetryAndTranscriptForSameRepo_JoinsByPath()
    {
        var report = ReportBuilder.Build(
            [Telemetry(@"C:\repo\app\App.sln", Call("find_symbol"))],
            [Transcript(@"C:\repo\app", roslyn: 3, csharp: 1)],
            3.5, false, 3.00m);

        var row = Assert.Single(report.RepoRows);
        Assert.True(row.HasTelemetry);
        Assert.Equal(0.75, row.AdoptionRate);
        Assert.Equal(1, row.Sessions);
    }

    [Fact]
    public void Build_TranscriptOnlyRepo_IsReportedNotDropped()
    {
        // The normal case, since telemetry is opt-in: the server was used, but nothing was
        // recorded locally. Dropping the repo would silently shrink the picture.
        var report = ReportBuilder.Build(
            [],
            [Transcript(@"C:\repo\other", roslyn: 5, csharp: 5)],
            3.5, false, 3.00m);

        var row = Assert.Single(report.RepoRows);
        Assert.False(row.HasTelemetry);
        Assert.Equal(5, row.Calls);
        Assert.Equal(0, row.Saved);
    }

    [Fact]
    public void Build_TranscriptWithoutRoslynCalls_IsNotAReportedRepo()
    {
        var report = ReportBuilder.Build(
            [], [Transcript(@"C:\repo\none", roslyn: 0, csharp: 20)], 3.5, false, 3.00m);

        Assert.Empty(report.RepoRows);
        Assert.Null(report.AggregateAdoption);
    }

    [Fact]
    public void Build_AggregateAdoption_WeightsByCallsNotByRepo()
    {
        // A repo with 3 calls must not weigh the same as one with 300; a mean of per-repo
        // rates would let a tiny repo swing the headline number.
        var report = ReportBuilder.Build(
            [],
            [
                Transcript(@"C:\repo\a", roslyn: 300, csharp: 100),
                Transcript(@"C:\repo\b", roslyn: 0, csharp: 0)
            ],
            3.5, false, 3.00m);

        Assert.Equal(0.75, report.AggregateAdoption);
    }

    [Fact]
    public void Build_DeadTools_ExcludesToolsSeenOnlyInTranscripts()
    {
        var transcript = Transcript(@"C:\repo\a", roslyn: 1, csharp: 0);
        transcript.RoslynByTool["get_diagnostics"] = 1;

        var report = ReportBuilder.Build(
            [Telemetry(@"C:\repo\a\A.sln", Call("find_symbol"))],
            [transcript],
            3.5, false, 3.00m);

        // Called somewhere, so not dead — even though it never appears in telemetry.
        Assert.DoesNotContain("get_diagnostics", report.DeadTools);
        Assert.DoesNotContain("find_symbol", report.DeadTools);
        Assert.Contains("find_callers", report.DeadTools);
    }

    [Fact]
    public void Build_SavedCost_UsesTheDisclosedRate()
    {
        var report = ReportBuilder.Build(
            [Telemetry(@"C:\repo\a\A.sln", Call("find_symbol", baseline: 1_000_100, response: 100))],
            [], 3.5, false, 3.00m);

        Assert.Equal(1_000_000, report.SavedTokens);
        Assert.Equal(3.00m, report.SavedCostUsd);
        Assert.Equal(3.00, report.Method.InputRatePerMillion);
    }

    [Fact]
    public void Build_WithNoDataAtAll_ReportsNoUsage()
    {
        var report = ReportBuilder.Build([], [], 3.5, false, 3.00m);

        Assert.False(report.HasAnyUsage);
        Assert.False(report.HasTranscripts);
    }

    [Fact]
    public void Build_TranscriptsButNoMcpCalls_IsUsageAbsentNotDataAbsent()
    {
        var stats = Transcript(@"C:\repo\a", roslyn: 0, csharp: 40);
        stats.InputTokens = 5_000;

        var report = ReportBuilder.Build([], [stats], 3.5, false, 3.00m);

        // These must not read the same: one means "nothing to report", the other means
        // "plenty happened and the server was never used" — a finding in its own right.
        Assert.False(report.HasAnyUsage);
        Assert.True(report.HasTranscripts);
    }

    [Fact]
    public void Build_UncalibratedDivisor_MethodCarriesWarning()
    {
        var report = ReportBuilder.Build([], [], 3.5, calibrated: false, 3.00m);

        Assert.False(report.Method.DivisorCalibrated);
        Assert.Contains("understates", report.Method.Baseline);
        Assert.Contains("upward", report.Method.AdoptionBias);
    }
}
