---
name: instinct-system
description: >
  Unified learning-and-memory system: confidence-scored instincts
  (observe-hypothesize-confirm, stored in .claude/instincts.md), user
  corrections captured as permanent rules in MEMORY.md, and organic
  discoveries logged to .claude/learning-log.md. Includes status, export,
  and import modes. Load this skill when you notice a recurring pattern,
  a user corrects your output, or you discover something non-obvious.
  Triggers: "show instincts", "what have you learned", "list instincts",
  "export instincts", "share instincts", "import instincts",
  "load instincts from", "learn this", "I think they always",
  "notice a pattern", "instinct", "hypothesis", "confidence",
  "learn from mistakes", "remember this", "don't do that again",
  "log this", "document this finding", "gotcha", "what did we learn",
  "learnings", "discoveries", or at session start (to load existing knowledge).
---

# Instinct System

One learning system, three tiers. Every signal Claude receives during work — an observed pattern, a user correction, a surprising discovery — routes to exactly one store.

## Core Principles

1. **Three tiers, one routing decision** — *Instincts* are unconfirmed hypotheses (`.claude/instincts.md`). *Corrections* are user-confirmed rules (`MEMORY.md`). *Discoveries* are insights that explain the world (`.claude/learning-log.md`). Rules prescribe behavior; insights describe it; instincts are rules-in-waiting. Never mix the tiers — a hypothesis in MEMORY.md pollutes permanent knowledge, and a confirmed rule left as an instinct gets forgotten.

2. **Instincts are hypotheses, not rules** — An instinct starts as a guess from a single observation and has no authority until confirmed. "One handler uses `sealed`" is an instinct at 0.3; "all 12 handlers use `sealed`" is a rule at 0.9. Confidence (0.3–0.9) drives behavior: note at 0.3, mention at 0.5, follow at 0.7, promote at 0.9.

3. **A user correction is a confirmed instinct at full confidence** — When the user corrects you, skip the confirmation cycle entirely. Generalize the lesson, capture it immediately in MEMORY.md with the "why", and confirm what was captured. A correction costs the user 30 seconds today and saves hours across all future sessions — losing one is the most expensive mistake this system can make.

4. **Project-scoped, never global** — What holds in one codebase may be wrong in another. Instincts live per-project; transfers between projects go through export/import with confidence decay, never at full confidence.

5. **Review at session start, prune periodically** — Read MEMORY.md and load instincts at 0.7+ before writing any code; scan recent log entries for the working area. Knowledge captured but never reviewed is wasted effort. Audit all three stores when they bloat — stale instincts, duplicate rules, and unreviewed logs defeat the purpose.

## Patterns

### Tier Routing

| Signal | Destination | Lifespan |
|--------|-------------|----------|
| Pattern observed, not yet confirmed ("I think they always...") | `.claude/instincts.md` at 0.3 | Until promoted or discarded |
| User correction ("no, use X", "don't do that again", "remember this") | `MEMORY.md` immediately, generalized | Permanent until proven wrong |
| Instinct reaching 0.9 confidence | Promote to `MEMORY.md`, remove from instincts | Permanent |
| Non-obvious discovery (bug root cause, gotcha, workaround, perf finding) | `.claude/learning-log.md` | 3–6 months, then archive or promote |
| Same gotcha logged 3+ times | Promote to `MEMORY.md` as a preventive rule | Permanent |
| Session state (done/pending) | Handoff via wrap-up — not this system | Until next session |

### Instinct Lifecycle (Observe-Hypothesize-Confirm)

```
1. OBSERVE     — "This handler is internal sealed class. Convention?"
2. HYPOTHESIZE — Write to .claude/instincts.md at confidence 0.3:
                 - Use `sealed` on handler classes | confidence: 0.3 | seen: 1 | last: 2026-06-12
3. SEEK        — Actively check 2-3 related files (find_symbol, get_public_api).
                 Passive observation is not enough.
4. ADJUST      — Confirmed: raise per the adjustment track.
                 Contradicted: halve confidence, note the exception.
                 Mixed: hold steady, note the split.
5. PROMOTE     — At 0.9, present evidence and offer promotion to MEMORY.md.
```

### Instinct Storage Format

`.claude/instincts.md`, grouped by category with structured metadata:

```markdown
# Project Instincts

## Code Style [0.7]
- Use `sealed` on all handler classes | confidence: 0.8 | seen: 5 | last: 2026-06-12
- Prefix private fields with underscore | confidence: 0.5 | seen: 2 | last: 2026-06-10

## Architecture [0.7]
- Feature folders use singular names | confidence: 0.7 | seen: 4 | last: 2026-06-12
```

Category header `[0.7]` = average confidence. Standard categories: Code Style, Architecture, Naming, Testing, Data Access, API Design, Configuration, Performance, Tooling.

### Confidence Adjustment Rules

No ad-hoc scoring. Follow this track precisely:

```
CONFIRMATION:  1st observation → 0.3 | 2nd → 0.5 | 3rd → 0.7 | 4th → 0.8
               5th+ with zero contradictions → 0.9 (promotion candidate)

CONTRADICTION: Any contradiction → halve current confidence (0.7 → 0.35)
               Two in a row → 0.1 (effectively dead)

USER OVERRIDE: Explicit confirm → 0.8 | Explicit correct → 0.0 (remove,
               capture in MEMORY.md instead) | "Sometimes" → cap at 0.5

STALENESS:     No observations for 10+ sessions → flag for review
               Contradicted, unreconfirmed for 5 sessions → remove
```

### Acting on Instincts by Confidence

```
0.0–0.2 → IGNORE  — insufficient evidence, do not mention
0.3–0.4 → NOTE    — record internally, do not apply
0.5–0.6 → MENTION — "This project may use [pattern]. Follow it?"
0.7–0.8 → FOLLOW  — apply by default, mention on first use
0.9     → PROMOTE — offer to add to MEMORY.md as a permanent rule
```

Never silently apply an instinct below 0.7.

### Correction Capture Flow (Tier 2)

When the user corrects your output ("no, use X instead", "we don't do it that way", "always/never do X", "remember this"):

```
1. DETECT      — Recognize the correction signal.
2. ACKNOWLEDGE — "Got it — HybridCache instead of IMemoryCache."
3. GENERALIZE  — Specific: "Don't use IMemoryCache in the Orders endpoint"
                 General:  "Always use HybridCache over IMemoryCache —
                            stampede protection + L1/L2 out of the box."
                 Ask: is this specific to this file? This layer? What's the
                 underlying principle? Store the broadest correct statement.
4. CHECK       — Scan MEMORY.md for overlap. Update an existing rule rather
                 than adding a near-duplicate.
5. STORE       — Write under the right category, one line, rationale after
                 the dash. If a matching instinct exists, remove it — it
                 just graduated.
6. CONFIRM     — "Added to Memory > Data Access: HybridCache over IMemoryCache."
```

MEMORY.md format — same categories as instincts, one actionable rule per line:

```markdown
## Data Access
- Always use HybridCache over IMemoryCache — stampede protection + L1/L2
- Never use repository pattern over EF Core — use DbContext directly
```

### Discovery Logging (Tier 3)

Log organic findings to `.claude/learning-log.md` the moment they occur — a 2-line entry written immediately beats a paragraph reconstructed later. Entry format:

```markdown
## 2026-06-12 | Gotcha | MassTransit Consumer Registration Order Matters
Multiple consumers for one message type run in registration order; if the
first throws, the rest are skipped. Caused missed audit events.
**Files:** `src/Shared/Extensions/MassTransitConfig.cs:15-30`
**Resolution:** Independent consumer endpoints or the retry filter.
```

Log when (and only when) you hit one of these triggers:

```
Bug Root Cause        — the cause was NOT where the error appeared
Architecture Decision — discovered WHY something is built a certain way
Gotcha                — framework/library behaved surprisingly
Performance Discovery — unexpected perf behavior, with the measurement
Pattern Found         — reusable codebase pattern worth remembering
External Service      — third-party API behaves differently than documented
```

Those six trigger names are also the category vocabulary — use them verbatim in entries. Routine changes with nothing surprising do not get logged.

### Session-Start Loading

```
1. Read MEMORY.md — apply rules proactively, don't wait to be reminded
2. Read .claude/instincts.md — load 0.7+ as defaults, note 0.5–0.6
3. Scan recent learning-log entries for the area you're working in
4. Flag stale instincts (10+ sessions without observation) for review
```

## Modes

Three operations on the instinct store, invoked by name or trigger phrase.

### Status ("show instincts", "what have you learned", "list instincts")

1. Read `.claude/instincts.md`; parse each entry's metadata.
2. Sort by confidence descending, group by category, render as a table:
   instinct | confidence | category | status (new / stable / reinforced / decaying / promotion candidate).
3. Summarize health: total count, average confidence, recently reinforced vs decaying entries, any conflicting instincts.

### Export ("export instincts", "share instincts")

1. Read `.claude/instincts.md`; filter to confidence > 0.7 (threshold configurable).
2. Strip project-specific context (file paths, line numbers) while preserving the pattern itself.
3. Write to `.claude/instincts-export.md` with portable metadata.
4. Report what was exported and what was skipped (below threshold), with the output path.

### Import ("import instincts", "load instincts from")

1. Read the export file (user-provided path or `.claude/instincts-export.md`) and the current `.claude/instincts.md`.
2. Merge each imported instinct:
   - **No local match** — add with confidence decayed by 0.2 (0.9 → 0.7); never import above 0.7. Mark `source: imported from [project]`.
   - **Matching instinct exists** — keep the higher confidence, mark reinforced.
   - **Conflicting instinct exists** — present both to the user for resolution; do not auto-overwrite local evidence.
3. Write the merged result and report imported / merged / conflicts. Every project must confirm imported patterns locally.

## Anti-patterns

### Over-Eager Pattern Recognition

```
# BAD — first observation treated as a rule
*Reads one handler* "This project always uses internal sealed class."
*Generates 5 handlers that contradict 8 of 9 existing ones*

# GOOD — hypothesis at 0.3, then active confirmation
"Noticed internal sealed in CreateOrderHandler. Instinct at 0.3.
 Checking 3 more handlers..." *All public* → "Disconfirmed. Discarding."
```

### Fixing Without Capturing

```
# BAD — correction applied, lesson lost
User: "No, we use HybridCache here"
Claude: "Fixed." *Next session: same mistake*

# GOOD — fix AND capture
Claude: "Fixed. Added to Memory > Data Access:
         Always use HybridCache over IMemoryCache."
```

### Logging Everything

```
# BAD — noise entry
## 2026-06-12 | Pattern Found | Used Primary Constructors

# GOOD — only the non-obvious earns an entry
## 2026-06-12 | Gotcha | Primary Constructor Params Captured as Fields
Declaring an explicit field with the same name warns but compiles —
and the two can silently diverge.
```

### Write-Only Stores

```
# BAD — 40 instincts at 0.3 from 3 months ago; MEMORY.md at 200 lines
  with duplicates; 500-line log nobody reads
# GOOD — periodic audit: remove instincts below 0.2 or stale 10+
  sessions; merge/prune MEMORY.md past 50 rules; every ~20 log entries,
  promote recurring findings and archive the stale. Keep instincts
  under 50 active entries.
```

## Decision Guide

| Scenario | Action |
|----------|--------|
| First time seeing a pattern | Instinct at 0.3, check 2-3 related files |
| Pattern seen 3+ times, no contradictions | Raise to 0.7, follow by default |
| Pattern contradicted | Halve confidence, note the exception |
| User says "we always do X" | Instinct at 0.8 (user confirmation) |
| User corrects your code | Generalize → MEMORY.md immediately; drop any matching instinct |
| User says "remember this" / "always/never" | Capture in MEMORY.md as stated, generalized |
| Same correction given twice | High priority — the rule wasn't captured or reviewed |
| Correction about a one-time task | Don't store — only reusable patterns |
| User asks to forget a rule | Remove from MEMORY.md immediately |
| Non-obvious bug, gotcha, workaround, perf surprise | Log to learning-log with category + files |
| Same gotcha logged 3+ times | Promote to MEMORY.md as a preventive rule |
| Instinct at 0.9 | Present evidence, offer promotion to MEMORY.md |
| User partially confirms ("only for commands") | Narrow scope, reset to 0.5, re-confirm |
| Sharing patterns with another project | Export at 0.7+, import with 0.2 decay |
| Conflict during import | Present both to the user; never auto-overwrite |
| Any store bloats (50+ entries/rules) | Audit: prune dead, merge duplicates, promote mature |
| Starting a session | Load MEMORY.md, instincts 0.7+, recent log entries |

## Related

- `convention-learner` — detects codebase conventions in bulk; feed its findings in as instincts
- `wrap-up` — end-of-session ritual; routes session learnings into the correct tier of this system
