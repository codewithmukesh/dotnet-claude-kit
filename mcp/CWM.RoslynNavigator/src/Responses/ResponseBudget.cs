namespace CWM.RoslynNavigator.Responses;

/// <summary>
/// The size every default response must stay under, so the agent reads it inline.
/// <para>
/// Claude Code's limits (https://code.claude.com/docs/en/mcp#mcp-output-limits-and-warnings,
/// checked 2026-11-04): it warns when an MCP result passes 10,000 tokens, caps results at
/// MAX_MCP_OUTPUT_TOKENS (default 25,000 tokens), and saves any successful text result longer
/// than 50,000 characters to a file, replacing it in the conversation with the file's path.
/// A result saved to a file is the failure the A/B run kept hitting: the agent then reads a
/// fraction of it, or none, and answers from that.
/// </para>
/// <para>
/// 30,000 characters keeps a JSON response under the 50,000-character file threshold with a wide
/// margin and, at the ~3 characters per token JSON paths tokenize to, near the 10,000-token
/// warning. Tools fit their page to it (<see cref="Paging.Fit{T}"/>) and say where the next page
/// starts, so a large answer costs a few calls rather than a file the agent never reads.
/// </para>
/// </summary>
internal static class ResponseBudget
{
    public const int MaxChars = 30_000;
}
