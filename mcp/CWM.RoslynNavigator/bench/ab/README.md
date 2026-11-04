# Real A/B: Claude Code with and without the navigator

The main bench measures the server over MCP. This folder measures what a user feels: real
Claude Code sessions answering the same questions with and without the navigator, timed
end to end, with every model round trip included.

The committed run is in [`../results/2026-11-04-ab/`](../results/2026-11-04-ab/report.md).

## Method

- **Tasks.** Eleven of the bench's twelve tasks per corpus, asked the way a developer would
  ask them ("Which methods call `AbpModule.IsAbpModule`? List each caller with its file and
  line."). "Diagnostics for one file" is left out because, with Bash off, the default route
  has no way to run a build.
- **Sessions.** Every task runs in a fresh headless session (`claude -p`, stream-json) in the
  corpus folder, once per arm. The arm order alternates between tasks.
  - **without:** the built-in Grep, Glob, and Read tools only.
  - **with:** the same three tools plus the navigator over MCP, and a one-paragraph system
    note telling the agent to prefer it, which is what the kit's rules do.
- **Same for both arms.** Bash, edits, web, and subagents are off. The model is pinned
  (`--model sonnet`; the committed run resolved to `claude-sonnet-5-5`). Claude Code runs
  with an isolated `CLAUDE_CONFIG_DIR` holding only login credentials, so personal hooks,
  plugins, CLAUDE.md, and memory reach neither arm. One throwaway session per arm per corpus
  warms the prompt cache first, so neither arm pays the first cache write.
- **Navigator startup is excluded.** The server's stderr goes to a file
  (`stderr_to_file.py`), and the question is sent only after it logs
  `All compilations warmed`. Startup is measured by the main bench.
- **Measured per session:** wall time from sending the question to the final answer, API
  time, turns, tool calls by tool, token usage (input, cache writes, cache reads, output),
  and cost, all from the session's own `result` event.
- **Accuracy.** `judge_ab.py` gives each pair to a separate session with no tools, labelled A
  and B in a random order, and asks for a 0–100 score per answer. The reference is the
  navigator's raw output from the with-navigator transcript, and the judge is told it can be
  capped, so correct items missing from the reference are not penalised.

## Reproduce

Prerequisites: the corpora cloned and restored as in [`../README.md`](../README.md), the
server built in Release, and Claude Code signed in.

```bash
# 1. An isolated Claude Code config with only your login in it
mkdir -p ab-config && cp ~/.claude/.credentials.json ab-config/

# 2. One MCP config per corpus, named mcp-<CloneDirectory>.json, plus mcp-none.json
#    {"mcpServers":{"cwm-roslyn-navigator":{"type":"stdio","command":"dotnet",
#      "args":["<path>/CWM.RoslynNavigator.dll","--solution","<corpus>/<solution>"]}}}
echo '{"mcpServers":{}}' > ab-config/mcp-none.json

# 3. Run (from mcp/CWM.RoslynNavigator/bench), then judge, then render
python ab/run_ab.py --corpus-root <corpus-root> --corpus-defs corpus \
  --corpus clean-architecture --corpus eshop --corpus jellyfin --corpus orchardcore --corpus abp \
  --mcp-dir ab-config --config-dir ab-config --out ab-out --transcripts ab-out/t
python ab/judge_ab.py --results ab-out/results.json --transcripts ab-out/t --config-dir ab-config
python ab/report_ab.py --results ab-out/results.json --out ab-out/report.md
```

`run_ab.py` resumes: rows that already succeeded are kept, and anything that failed
(including an API or sign-in error, which Claude Code reports with `is_error`) runs again.
The committed run took about two hours of sessions, one at a time, on a subscription login.
`claude setup-token` gives a long-lived token for unattended runs.

## Caveats

- **One session per task per arm.** Model runs vary, so trust the per-solution and overall
  totals more than any single row.
- **The judge is a model**, and its reference is the navigator's own output. It is blinded
  to which arm wrote which answer and told not to penalise correct extras, and its stated
  reasons are concrete (wrong line numbers, callers that do not exist, vague lists where the
  question asked for files), but it is not a human review.
- **More input tokens with the navigator.** Its 26 tool definitions add roughly 21k tokens
  to every request. Most of those are prompt-cache reads, which is why cost still drops.
- **The default route is narrower than real life.** Without Bash the agent cannot run `rg`,
  `find`, or `dotnet build`. With Bash it could do more, and it would also take longer.
- **Startup is not in these times.** On a large solution the first question of a session
  can wait for the load (see the main bench's startup table).
