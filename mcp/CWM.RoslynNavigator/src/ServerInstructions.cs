namespace CWM.RoslynNavigator;

/// <summary>
/// Conventions shared by every tool, sent once in the MCP initialize response. Tool
/// definitions are re-sent to the model on every request, so a rule stated here instead of in
/// 26 descriptions is paid for once. Descriptions keep only what picks the right tool and
/// fills its parameters; anything true of all tools belongs here.
///
/// <para>Plain ASCII without quotes, apostrophes, or angle brackets: the SDK's JSON encoder
/// writes each of those as a six-byte escape.</para>
/// </summary>
internal static class ServerInstructions
{
    public const string Text = """
        Roslyn semantic navigation for the loaded C# solutions. Prefer these tools over grep and reading whole files.
        Conventions shared by all tools:
        - A symbol argument takes a bare name, Type.Member, a fully qualified name, or a SymbolId from find_symbol or get_symbol_at (exact, overload included). Qualify names reused across types; the optional file, line, className, or containingType arguments narrow an ambiguous name. An ambiguous name returns AmbiguousMatch with candidates.
        - File arguments match a path suffix (./ and backslashes accepted). A file that matches nothing returns FileNotFound.
        - maxResults caps a list, and responses stay under about 30k characters. Count, TotalFound, and Truncated report the full size; when truncated, call again with offset set to NextOffset for the next page, or narrow the filter.
        - format auto (default) returns full entries when they fit, else compact line strings grouped by file (Files) with per-project counts (ByProject). full or compact forces one.
        - IsGenerated: true marks generated code (omitted when false).
        - If an answer is empty or reports State Loading, call get_workspace_status.
        """;
}
