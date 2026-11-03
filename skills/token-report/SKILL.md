---
name: token-report
description: >
  Report how many tokens the Roslyn Navigator MCP server has saved, across one
  repository or all of them, with a per-tool leaderboard, adoption rate, and the
  methodology behind the number. Use when: "token report", "token savings", "how
  many tokens saved", "is the MCP worth it", "MCP usage", "tool leaderboard",
  "adoption rate", "which MCP tools are used", "prove the savings".
---

# /token-report — Roslyn Navigator token savings

## What

Reports what the Roslyn MCP server has actually saved, measured rather than asserted.
Two surfaces:

| Scope | Surface | Use when |
|---|---|---|
| This repository, mid-session | `get_token_savings` MCP tool | "how are we doing here?" |
| All repositories, aggregate | `CWM.RoslynNavigator.Stats` CLI | producing a public number |

## When

- Deciding whether the MCP server earns its context cost
- Finding tools nobody calls — a description problem, or a tool that should not exist
- Backing a claim about token efficiency with something checkable

## Prerequisite

**Collection is opt-in and off by default.** Nothing was recorded unless the server was
started with it enabled:

```bash
CWM_ROSLYN_TELEMETRY=on cwm-roslyn-navigator --solution MySolution.sln
```

Nothing is transmitted. Records are written under `%LOCALAPPDATA%/cwm-roslyn-navigator/`
(or the XDG equivalent), outside the repository. Delete that directory to erase everything.

Adoption rate is read from Claude Code transcripts and needs no opt-in, so it is available
even when telemetry was never enabled.

## How

**1 — This repository, mid-session.** Call the `get_token_savings` MCP tool. Returns
totals, a per-tool leaderboard, separate zero-result / not-ready / error counts, and the
methodology. If it reports `Enabled: false`, telemetry was never turned on — say so rather
than reporting zero as if it were a measurement.

**2 — All repositories.**

```bash
cd mcp/CWM.RoslynNavigator
dotnet run --project stats/CWM.RoslynNavigator.Stats.csproj            # table
dotnet run --project stats/CWM.RoslynNavigator.Stats.csproj -- --markdown
dotnet run --project stats/CWM.RoslynNavigator.Stats.csproj -- --json
dotnet run --project stats/CWM.RoslynNavigator.Stats.csproj -- --repo myapp
```

**3 — Interpret it honestly.** Three things must be said whenever a number is quoted:

- **The divisor is uncalibrated.** 3.5 bytes/token is an estimate, and it scales every
  figure linearly. If `DivisorCalibrated` is false, the number is indicative, not precise.
- **The baseline understates.** It counts only the files an answer came from, ignoring the
  wrong files a text search would have opened first.
- **Adoption is biased upward.** Its denominator counts only Read/Grep/Glob calls that
  demonstrably targeted C#; ambiguous searches are excluded.

**4 — Act on the leaderboard.** A tool with no calls is the actionable finding. Low
adoption in a configured repository usually means a tool description is not attracting the
work it should — more useful than the savings figure itself.

## Calibrating the divisor

Turns the one estimated input into a measurement. Free, manual, and never run by the
server:

```bash
dotnet run --project stats/CWM.RoslynNavigator.Stats.csproj -- calibrate \
  --api-key $ANTHROPIC_API_KEY --path /path/to/dotnet/repo --sample 200
```

Samples C# files, gets exact counts from the Anthropic `count_tokens` endpoint, and
regresses bytes against tokens. Apply the result to `TokenEstimator` in the server and
record the sample size and R² next to the constant so the figure stays checkable.

## Example

```
  Repositories        3  (1 with telemetry)
  Tool calls          412
  Tokens saved        1,284,000
  Adoption            68%

  Tool leaderboard
  tool                  calls      saved   p50 ms  0-result
  find_references         180    612,000       84        4%
  find_symbol             150    431,000       22        2%

  Never called (2): get_nuget_packages, resolve_stack_trace
  Divisor: 3.5 bytes/token — UNCALIBRATED estimate.
```

When the server was never invoked, the report says so plainly instead of printing zeros —
"no calls" and "no savings" are different findings.

## Related

- `/verify` — quality gates before a PR
- `/health-check` — codebase health grades
