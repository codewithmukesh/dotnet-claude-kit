using System.Globalization;
using System.Text;

namespace CWM.RoslynNavigator.Bench.Reporting;

/// <summary>
/// Renders every run in a results folder as one Markdown report. Measured and modelled
/// numbers are always in separately labelled columns; nothing here adds one to the other.
/// </summary>
internal static class MarkdownReport
{
    private static readonly string[] TierOrder = ["small", "medium", "large", "xl"];
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string Render(IReadOnlyList<RunResult> runs, string? afterLabel)
    {
        var builds = runs.Select(r => r.Build.Label).Distinct().ToList();
        var after = afterLabel ?? builds.FirstOrDefault(b => b == "current") ?? builds.Last();
        var before = builds.FirstOrDefault(b => b != after);
        builds = [.. builds.Where(b => b != after), after];
        var corpora = runs
            .Select(r => r.Corpus)
            .DistinctBy(c => c.Name)
            .OrderBy(c => Array.IndexOf(TierOrder, c.Tier))
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .ToList();

        var sample = runs[0];
        var md = new StringBuilder();

        md.AppendLine($"# CWM.RoslynNavigator benchmark — {sample.RunDate}");
        md.AppendLine();
        md.AppendLine($"Builds: **{before ?? "—"}** (before) vs **{after}** (after). " +
                      $"Machine: {sample.Environment.Os}, {sample.Environment.ProcessorCount} logical CPUs, {sample.Environment.Machine}, harness on {sample.Environment.Runtime}.");
        md.AppendLine();
        md.AppendLine("Every number below is **measured** unless its column says **modelled**. " +
                      $"Tokens are estimated as bytes / {sample.Settings.BytesPerToken.ToString(Invariant)} " +
                      $"(divisor {(sample.Settings.DivisorCalibrated ? "calibrated" : "**not calibrated**")} — the server's own TokenEstimator). " +
                      $"Modelled time assumes **{sample.Settings.PrefillTokensPerSecond.ToString("N0", Invariant)} input tokens/s**; see bench/README.md.");
        md.AppendLine();

        RenderHeadline(md, runs, corpora, after);
        RenderCorpora(md, runs, corpora, builds);
        RenderStartup(md, runs, corpora, builds);
        RenderTasks(md, runs, corpora, after, before);
        RenderTools(md, runs, corpora, before, after);
        RenderSlowest(md, runs, corpora, after);
        RenderProblems(md, runs, corpora, builds);

        return md.ToString();
    }

    private static void RenderHeadline(StringBuilder md, IReadOnlyList<RunResult> runs, List<CorpusInfo> corpora, string after)
    {
        md.AppendLine($"## Headline — {TaskCount(runs)} agent tasks per corpus ({after})");
        md.AppendLine();
        md.AppendLine("Navigator = the tool response. Grep + Read = the search an agent runs plus every file the answer lives in, each file counted once. " +
                      "Only tasks the navigator answered are summed. Measured times are tool round trip vs search + file I/O only — no model time on either side.");
        md.AppendLine();
        md.AppendLine("| Tier | Corpus | Tasks | Navigator tokens | Grep + Read tokens | Tokens saved | × fewer | Measured: navigator | Measured: grep + read I/O | Agent tool calls (nav / manual) | **Modelled** agent time saved |");
        md.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");

        foreach (var corpus in corpora)
        {
            var run = Find(runs, corpus.Name, after);
            if (run is null) continue;
            var answered = run.Tasks.Where(t => t.SavedTokens is not null).ToList();
            var nav = answered.Sum(t => (long)t.NavigatorTokens);
            var manual = answered.Sum(t => (long)t.ManualTokens);
            var navMs = answered.Sum(t => t.NavigatorMeasuredMs ?? 0);
            var manualMs = answered.Sum(t => t.ManualMeasuredMs);
            var modelled = answered.Sum(t => t.ModelledSecondsSaved ?? 0);
            var calls = $"{answered.Sum(t => t.NavigatorCalls)} / {answered.Sum(t => t.ManualCalls)}";

            md.AppendLine($"| {corpus.Tier} | {corpus.Name} | {answered.Count}/{run.Tasks.Count} | {nav:N0} | {manual:N0} | " +
                          $"{Pct(manual - nav, manual)} | {Ratio(manual, nav)} | {navMs:N0} ms | {manualMs:N0} ms | {calls} | {modelled.ToString("N1", Invariant)} s |");
        }

        md.AppendLine();
    }

    private static void RenderCorpora(StringBuilder md, IReadOnlyList<RunResult> runs, List<CorpusInfo> corpora, List<string> builds)
    {
        md.AppendLine("## Corpora");
        md.AppendLine();
        md.AppendLine($"| Tier | Corpus | Commit | Solution | Projects declared | Projects loaded ({string.Join(" / ", builds)}) | .cs files | C# source |");
        md.AppendLine("|---|---|---|---|---:|---:|---:|---:|");
        foreach (var c in corpora)
        {
            var loaded = string.Join(" / ", builds.Select(b => Find(runs, c.Name, b)?.Corpus.ProjectsLoaded?.ToString(Invariant) ?? "—"));
            md.AppendLine($"| {c.Tier} | [{c.Name}]({c.Repository}) | `{c.Commit[..Math.Min(10, c.Commit.Length)]}` | {c.Solution} | " +
                          $"{c.ProjectsDeclared} | {loaded} | {c.CsFiles:N0} | {c.CsBytes / 1048576.0:0.0} MB |");
        }

        md.AppendLine();
    }

    private static void RenderStartup(StringBuilder md, IReadOnlyList<RunResult> runs, List<CorpusInfo> corpora, List<string> builds)
    {
        md.AppendLine("## Startup and memory");
        md.AppendLine();
        md.AppendLine("Ready = process start until a tool stops answering \"loading\". First answer = a `find_symbol` issued the moment it is ready. " +
                      "Warm = the server's own \"All compilations warmed\" log line (n/a = not logged within the 20-minute wait). " +
                      "Server peak WS is the OS-reported peak of the server process. Server + build hosts is the largest simultaneous total seen at a 500 ms sample; " +
                      "build hosts are the MSBuild processes the server spawns (count in brackets).");
        md.AppendLine();
        md.AppendLine("| Tier | Corpus | Build | Ready | First answer | All compilations warm | Server peak WS | Server + build hosts (peak, sampled) | Restarts |");
        md.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---:|");
        foreach (var c in corpora)
        {
            foreach (var b in builds)
            {
                var run = Find(runs, c.Name, b);
                if (run is null) continue;
                var s = run.Startup;
                var memory = s.Memory.Supported ? $"{s.Memory.ServerPeakBytes / 1048576:N0} MB" : "n/a";
                var hosts = s.Memory.Supported ? $"{s.Memory.CombinedPeakBytes / 1048576:N0} MB ({s.Memory.BuildHostProcesses})" : "n/a";
                md.AppendLine($"| {c.Tier} | {c.Name} | {b} | {Seconds(s.ReadyMs)} | {Seconds(s.FirstAnswerMs)} | {Seconds(s.WarmMs)} | {memory} | {hosts} | {s.Restarts} |");
            }
        }

        md.AppendLine();
    }

    private static void RenderTasks(StringBuilder md, IReadOnlyList<RunResult> runs, List<CorpusInfo> corpora, string after, string? before)
    {
        md.AppendLine($"## Tokens per task ({after})");
        md.AppendLine();
        md.AppendLine("Grep shows matching lines / lines an agent receives (head limit). Read is the file-read floor: the files the answer lives in, each once. " +
                      "\"Trunc.\" marks a navigator answer that hit its result cap — its file list, and so the manual baseline, is then partial (conservative).");
        md.AppendLine();

        foreach (var c in corpora)
        {
            var run = Find(runs, c.Name, after);
            if (run is null) continue;
            var beforeRun = before is null ? null : Find(runs, c.Name, before);

            md.AppendLine($"### {c.Tier}: {c.Name}");
            md.AppendLine();
            md.AppendLine("| Task | Navigator tok | Trunc. | Grep tok (matches / shown) | Read tok (files) | Grep + Read tok | Saved | × | Measured: nav ms | Measured: grep + read ms | **Modelled** s saved |" +
                          (beforeRun is null ? "" : $" Nav tok ({before}) |"));
            md.AppendLine("|---|---:|:-:|---:|---:|---:|---:|---:|---:|---:|---:|" + (beforeRun is null ? "" : "---:|"));

            foreach (var t in run.Tasks)
            {
                var prior = beforeRun?.Tasks.FirstOrDefault(p => p.Id == t.Id);
                var priorCell = beforeRun is null ? "" : $" {(prior is null ? "—" : prior.NavigatorStatus == "ok" ? prior.NavigatorTokens.ToString("N0", Invariant) : prior.NavigatorStatus)} |";
                var nav = t.NavigatorStatus == "ok" ? t.NavigatorTokens.ToString("N0", Invariant) : t.NavigatorStatus;
                var grep = t.GrepPattern is null ? "—" : $"{t.GrepTokens:N0} ({t.GrepMatches:N0} / {t.GrepLinesShown:N0})";

                md.AppendLine($"| {Escape(t.Title)} | {nav} | {(t.NavigatorTruncated == true ? "yes" : "")} | {grep} | {t.FileTokens:N0} ({t.FilesRead}) | {t.ManualTokens:N0} | " +
                              $"{(t.SavingsPercent is null ? "—" : t.SavingsPercent.Value.ToString("0.0", Invariant) + "%")} | " +
                              $"{(t.Multiplier is null || t.ManualTokens == 0 ? "—" : t.Multiplier.Value.ToString("0.0", Invariant) + "×")} | " +
                              $"{(t.NavigatorMeasuredMs is null ? "—" : t.NavigatorMeasuredMs.Value.ToString("N0", Invariant))} | {t.ManualMeasuredMs:N0} | " +
                              $"{(t.ModelledSecondsSaved is null ? "—" : t.ModelledSecondsSaved.Value.ToString("0.0", Invariant))} |{priorCell}");
            }

            md.AppendLine();
        }
    }

    private static void RenderTools(StringBuilder md, IReadOnlyList<RunResult> runs, List<CorpusInfo> corpora, string? before, string after)
    {
        md.AppendLine("## Per-tool latency — before vs after");
        md.AppendLine();
        md.AppendLine("Client-side round trip over stdio, in ms. Cold = first call to that tool in the session, after warm-up was awaited. " +
                      "Warm = median / p95 of five repeats (p95 of five is the slowest). n/a = tool not in that build.");
        md.AppendLine();

        foreach (var c in corpora)
        {
            var afterRun = Find(runs, c.Name, after);
            if (afterRun is null) continue;
            var beforeRun = before is null ? null : Find(runs, c.Name, before);

            md.AppendLine($"### {c.Tier}: {c.Name}");
            md.AppendLine();
            md.AppendLine($"| Tool | Cold ({before ?? "—"}) | Cold ({after}) | Warm p50 ({before ?? "—"}) | Warm p50 ({after}) | Warm p95 ({after}) | Tokens ({before ?? "—"}) | Tokens ({after}) |");
            md.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");

            foreach (var tool in afterRun.Tools)
            {
                var prior = beforeRun?.Tools.FirstOrDefault(t => t.Tool == tool.Tool);
                md.AppendLine($"| `{tool.Tool}` | {Cold(prior)} | {Cold(tool)} | {Warm(prior?.WarmMedianMs, prior)} | {Warm(tool.WarmMedianMs, tool)} | " +
                              $"{Warm(tool.WarmP95Ms, tool)} | {Tok(prior)} | {Tok(tool)} |");
            }

            md.AppendLine();
        }
    }

    private static void RenderSlowest(StringBuilder md, IReadOnlyList<RunResult> runs, List<CorpusInfo> corpora, string after)
    {
        md.AppendLine($"## Slowest tools per corpus ({after}, cold)");
        md.AppendLine();
        md.AppendLine("| Tier | Corpus | Slowest five (cold ms) |");
        md.AppendLine("|---|---|---|");
        foreach (var c in corpora)
        {
            var run = Find(runs, c.Name, after);
            if (run is null) continue;
            var slowest = run.Tools
                .Where(t => t.Status != "n/a")
                .OrderByDescending(t => t.ColdMs)
                .Take(5)
                .Select(t => $"`{t.Tool}` {t.ColdMs:N0}{(t.Status == "ok" ? "" : $" ({t.Status})")}");
            md.AppendLine($"| {c.Tier} | {c.Name} | {string.Join(", ", slowest)} |");
        }

        md.AppendLine();
    }

    private static void RenderProblems(StringBuilder md, IReadOnlyList<RunResult> runs, List<CorpusInfo> corpora, List<string> builds)
    {
        md.AppendLine("## Errors, timeouts, and load problems");
        md.AppendLine();
        var any = false;
        foreach (var c in corpora)
        {
            foreach (var b in builds)
            {
                var run = Find(runs, c.Name, b);
                if (run is null) continue;

                foreach (var t in run.Tools.Where(t => t.Status is not ("ok" or "n/a")))
                {
                    any = true;
                    md.AppendLine($"- **{c.Name} / {b}** `{t.Tool}` → {t.Status}" +
                                  $"{(t.ErrorCode is null ? "" : $" `{t.ErrorCode}`")}: {Escape(OneLine(t.ErrorMessage) ?? "")}");
                }

                if (run.Startup.LoadProblems.Count > 0)
                {
                    any = true;
                    md.AppendLine($"- **{c.Name} / {b}** workspace diagnostics logged by the server ({run.Startup.LoadProblems.Count}; non-fatal unless projects loaded < declared): " +
                                  string.Join("; ", run.Startup.LoadProblems.Take(5).Select(p => Escape(OneLine(p)!))));
                }
            }

            foreach (var note in c.Notes)
            {
                any = true;
                md.AppendLine($"- **{c.Name}** note: {Escape(note)}");
            }
        }

        if (!any) md.AppendLine("None.");
        md.AppendLine();
    }

    private static int TaskCount(IReadOnlyList<RunResult> runs) => runs.Max(r => r.Tasks.Count);

    private static RunResult? Find(IReadOnlyList<RunResult> runs, string corpus, string build) =>
        runs.FirstOrDefault(r => r.Corpus.Name == corpus && r.Build.Label == build);

    private static string Cold(ToolResult? t) => t is null ? "—"
        : t.Status == "n/a" ? "n/a"
        : t.Status == "ok" ? t.ColdMs.ToString("N0", Invariant)
        : $"{t.ColdMs.ToString("N0", Invariant)} ({t.Status})";

    private static string Warm(double? value, ToolResult? t) =>
        t is null ? "—" : t.Status == "n/a" ? "n/a" : value is null ? "skipped" : value.Value.ToString("N0", Invariant);

    private static string Tok(ToolResult? t) => t is null ? "—" : t.Status == "n/a" ? "n/a" : t.Tokens.ToString("N0", Invariant);

    private static string Seconds(double? ms) => ms is null ? "n/a" : (ms.Value / 1000).ToString("0.0", Invariant) + " s";

    private static string Seconds(long? ms) => Seconds(ms is null ? null : (double?)ms.Value);

    private static string Pct(long part, long whole) =>
        whole <= 0 ? "—" : (100.0 * part / whole).ToString("0.0", Invariant) + "%";

    private static string Ratio(long manual, long nav) =>
        nav <= 0 ? "—" : ((double)manual / nav).ToString("0.0", Invariant) + "×";

    private static string? OneLine(string? text) =>
        text?.Replace("\r", " ").Replace("\n", " ").Trim();

    private static string Escape(string text) => text.Replace("|", "\\|");
}
