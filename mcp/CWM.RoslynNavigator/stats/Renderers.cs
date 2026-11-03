using System.Text;

namespace CWM.RoslynNavigator.Stats;

/// <summary>
/// Renders a report as a terminal table or as markdown. Both surfaces state the
/// methodology and the uncalibrated divisor; neither prints a bare number.
/// </summary>
internal static class Renderers
{
    public static string Table(Report report)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Roslyn Navigator — token savings");
        sb.AppendLine(new string('=', 64));
        sb.AppendLine();

        if (!report.HasAnyUsage)
        {
            if (!report.HasTranscripts)
            {
                sb.AppendLine("No data found — no telemetry and no transcripts.");
                sb.AppendLine();
                sb.AppendLine("  Telemetry is opt-in. Set CWM_ROSLYN_TELEMETRY=on and restart the");
                sb.AppendLine("  MCP server to start recording. Nothing is transmitted.");
                return sb.ToString();
            }

            // Transcripts exist but the server was never invoked. That is a finding, not an
            // absence of data, and it is the one the adoption metric exists to surface.
            sb.AppendLine("  The server was never invoked in any indexed session.");
            sb.AppendLine();
            sb.AppendLine($"  Sessions indexed    {report.Sessions:N0}");
            sb.AppendLine($"  Measured spend      {report.MeasuredInputTokens:N0} in / {report.MeasuredOutputTokens:N0} out");
            sb.AppendLine($"  Cache reads         {report.MeasuredCacheReadTokens:N0}");
            sb.AppendLine($"  Adoption            0% — no MCP calls against any repository");
            sb.AppendLine();
            sb.AppendLine("  Savings cannot be computed without calls. Either the server is not");
            sb.AppendLine("  registered with the client, or its tool descriptions are not attracting");
            sb.AppendLine("  the navigation work they should — check `claude mcp list` first.");
            return sb.ToString();
        }

        sb.AppendLine($"  Repositories        {report.Repos}  ({report.ReposWithTelemetry} with telemetry)");
        sb.AppendLine($"  Tool calls          {report.TotalCalls:N0}");
        sb.AppendLine($"  Tokens saved        {report.SavedTokens:N0}");
        sb.AppendLine($"  Responses cost      {report.ResponseTokens:N0}");
        sb.AppendLine($"  Baseline would cost {report.BaselineTokens:N0}");
        sb.AppendLine($"  Est. saved          ${report.SavedCostUsd:N2} at ${report.Method.InputRatePerMillion:N2}/M input");
        sb.AppendLine();

        sb.AppendLine($"  Sessions            {report.Sessions:N0}");
        sb.AppendLine($"  Measured spend      {report.MeasuredInputTokens:N0} in / {report.MeasuredOutputTokens:N0} out");
        sb.AppendLine($"  Cache reads         {report.MeasuredCacheReadTokens:N0}");
        sb.AppendLine($"  Adoption            {FormatAdoption(report.AggregateAdoption)}");
        sb.AppendLine();

        sb.AppendLine($"  Zero-result calls   {report.ZeroResultCalls:N0}");
        sb.AppendLine($"  Not-ready calls     {report.NotReadyCalls:N0}");
        sb.AppendLine($"  Errors              {report.ErrorCalls:N0}");
        sb.AppendLine();

        if (report.Tools.Count > 0)
        {
            sb.AppendLine("  Tool leaderboard");
            sb.AppendLine($"  {"tool",-30} {"calls",7} {"saved",10} {"p50 ms",8} {"0-result",9}");
            sb.AppendLine("  " + new string('-', 68));
            foreach (var tool in report.Tools)
                sb.AppendLine($"  {Truncate(tool.Tool, 30),-30} {tool.Calls,7:N0} {tool.Saved,10:N0} {tool.MedianMs,8:N0} {tool.ZeroResultPct,8}%");
            sb.AppendLine();
        }

        if (report.RepoRows.Count > 0)
        {
            sb.AppendLine("  Repositories");
            sb.AppendLine($"  {"repo",-28} {"calls",7} {"saved",10} {"adoption",9} {"tel",4}");
            sb.AppendLine("  " + new string('-', 62));
            foreach (var repo in report.RepoRows)
                sb.AppendLine($"  {Truncate(repo.Name, 28),-28} {repo.Calls,7:N0} {repo.Saved,10:N0} {FormatAdoption(repo.AdoptionRate),9} {(repo.HasTelemetry ? "yes" : "no"),4}");
            sb.AppendLine();
        }

        // Only meaningful once the server is being used at all — otherwise every tool is
        // trivially "never called" and the list says nothing about any individual tool.
        if (report.DeadTools.Count > 0 && report.HasAnyUsage)
        {
            sb.AppendLine($"  Never called ({report.DeadTools.Count}): {string.Join(", ", report.DeadTools)}");
            sb.AppendLine("  A tool with no calls is a description that is not attracting work,");
            sb.AppendLine("  or a tool that should not exist.");
            sb.AppendLine();
        }

        AppendMethod(sb, report.Method, "  ");
        return sb.ToString();
    }

    public static string Markdown(Report report)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Roslyn Navigator — token savings");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("|---|---:|");
        sb.AppendLine($"| Repositories | {report.Repos} ({report.ReposWithTelemetry} with telemetry) |");
        sb.AppendLine($"| Tool calls | {report.TotalCalls:N0} |");
        sb.AppendLine($"| **Tokens saved** | **{report.SavedTokens:N0}** |");
        sb.AppendLine($"| Responses cost | {report.ResponseTokens:N0} |");
        sb.AppendLine($"| Baseline would cost | {report.BaselineTokens:N0} |");
        sb.AppendLine($"| Estimated saving | ${report.SavedCostUsd:N2} |");
        sb.AppendLine($"| Sessions | {report.Sessions:N0} |");
        sb.AppendLine($"| Measured spend | {report.MeasuredInputTokens:N0} in / {report.MeasuredOutputTokens:N0} out |");
        sb.AppendLine($"| Adoption | {FormatAdoption(report.AggregateAdoption)} |");
        sb.AppendLine($"| Zero-result / not-ready / errors | {report.ZeroResultCalls:N0} / {report.NotReadyCalls:N0} / {report.ErrorCalls:N0} |");
        sb.AppendLine();

        if (report.Tools.Count > 0)
        {
            sb.AppendLine("## Tool leaderboard");
            sb.AppendLine();
            sb.AppendLine("| Tool | Calls | Saved | p50 ms | Zero-result |");
            sb.AppendLine("|---|---:|---:|---:|---:|");
            foreach (var tool in report.Tools)
                sb.AppendLine($"| `{tool.Tool}` | {tool.Calls:N0} | {tool.Saved:N0} | {tool.MedianMs:N0} | {tool.ZeroResultPct}% |");
            sb.AppendLine();
        }

        if (report.DeadTools.Count > 0)
        {
            sb.AppendLine("## Never called");
            sb.AppendLine();
            sb.AppendLine(string.Join(", ", report.DeadTools.Select(t => $"`{t}`")));
            sb.AppendLine();
        }

        sb.AppendLine("## Methodology");
        sb.AppendLine();
        AppendMethod(sb, report.Method, "- ");
        return sb.ToString();
    }

    private static void AppendMethod(StringBuilder sb, Methodology method, string prefix)
    {
        sb.AppendLine($"{prefix}Baseline: {method.Baseline}");
        sb.AppendLine($"{prefix}Session dedup: {method.SessionDedup}");
        sb.AppendLine($"{prefix}Adoption: {method.AdoptionBias}");
        sb.AppendLine($"{prefix}Divisor: {method.BytesPerToken} bytes/token — " +
            (method.DivisorCalibrated
                ? "calibrated against measured token counts."
                : "UNCALIBRATED estimate. Treat savings as indicative, not precise."));
    }

    private static string FormatAdoption(double? rate) =>
        rate is null ? "n/a" : $"{rate.Value * 100:N0}%";

    private static string Truncate(string value, int width) =>
        value.Length <= width ? value : value[..(width - 1)] + "…";
}
