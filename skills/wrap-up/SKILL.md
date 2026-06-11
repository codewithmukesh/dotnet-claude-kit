---
name: wrap-up
description: >
  Owns the session handoff lifecycle: the end-of-session ritual that captures
  completed work, pending tasks, and learnings into .claude/handoff.md, and the
  session-start protocol that loads it back. Triggers on: /wrap-up, "wrap up",
  "done for today", "that's all", "end session", "signing off", "handoff" —
  and at session start: "start session", "session start", "load handoff",
  "pick up where we left off", "what were we working on".
---

# /wrap-up

## What

The session continuity ritual. Sessions are ephemeral; knowledge is permanent.
Wrap-up bridges sessions in both directions:

- **Session END** — capture exactly three things: what was DONE, what is
  PENDING, and what was LEARNED. Write them to `.claude/handoff.md` and flow
  durable learnings into `MEMORY.md`.
- **Session START** — load the handoff, memory, and instincts, then present a
  resume summary so no session starts blind.

## When

- End of a working session — "done for today", "that's all", "signing off"
- Before switching projects or after a major milestone
- Implicit endings — "thanks" after completed tasks, "good enough for now":
  offer the handoff, don't just say goodbye
- Start of a session — "start session", "load handoff", "what were we working on"
- For a mid-session save without ending, use `/checkpoint` instead

## How

### Session End

1. **Review the session** — From git status/diff and the conversation: files
   touched, tasks completed vs unfinished, decisions made and why, user
   corrections observed.
2. **Check uncommitted changes** — If any exist, offer to commit before wrapping.
3. **Write the handoff** — `.claude/handoff.md`, using the format below.
   Single file, always overwritten — only the current state matters. If the
   existing handoff has pending tasks from someone else, ask before
   overwriting: merge, overwrite, or skip.
4. **Extract learnings** — Corrections and discoveries worth keeping go to
   `MEMORY.md` (via `instinct-system`); emerging patterns update
   `.claude/instincts.md` (via `instinct-system`). The handoff's Learned
   section is the trigger, not the destination — handoffs are ephemeral.
5. **Confirm** — Summarize the handoff and learnings captured for the user.

### Handoff File Format (`.claude/handoff.md`)

Write it for a stranger with zero context — file paths, rationale, specific
next steps. "Continue the refactor" is useless; "Refactor
`src/Orders/CreateOrder.cs` to the Result pattern — see Catalog for the
established pattern" is actionable.

```markdown
# Session Handoff

> Generated: 2026-06-12 | Branch: feature/order-validation

## Completed
- [x] Added FluentValidation to CreateOrder (src/Orders/Features/CreateOrder.cs:15-35)
- [x] Fixed N+1 in GetOrderDetails (src/Orders/Features/GetOrderDetails.cs:28)

## Pending
- [ ] Add validation to UpdateOrder (src/Orders/Features/UpdateOrder.cs)
  - Reference: CreateOrder validator for the established pattern
- [ ] Full test run — 2 pre-existing Catalog failures, not from today

## Learned
- FluentValidation validators must be registered in the module's DI setup
- Test fixture seeds 1 item per order, which hides N+1 queries

## Context
- Branch: feature/order-validation | Last commit: "Add CreateOrder validation"
- Uncommitted changes: no | Solution: src/MyApp.slnx
```

When multiple developers share the project, add a `## Decisions Made` table
(Decision | Choice | Rationale) and `## Open Questions`.

### Learning Extraction Checklist

1. Did the user correct anything? → MEMORY.md via `instinct-system`
2. Did we discover something non-obvious? → Learned section
3. Did we decide something with rationale? → document the "why"
4. Did a tool/approach fail? → note it so the next session doesn't retry it

Good learning: "EF compiled queries don't work with Include()". Bad learning:
"Worked on Orders module" (vague) or "Used TimeProvider" (already a rule).

### Session Start

1. **Load context** — Read `.claude/handoff.md` (pending work), `MEMORY.md`
   (permanent rules), `.claude/instincts.md` (patterns at 0.7+ confidence).
   Missing files are fine: note it and start clean — never block on them.
2. **Detect the solution** — Find `.slnx`/`.sln` (current dir, then parents,
   then children) so Roslyn MCP tools work from the first prompt.
3. **Present a resume summary** — Last session's work, pending tasks, active
   rules/instincts count, detected solution. Then ask what to work on —
   never auto-start pending work.

## Example

```
User: /wrap-up

Claude: Wrapping up session...

## Session Summary
- Implemented OrderService with validation pipeline
- Fixed N+1 query in GetOrdersByCustomer

## Pending
- [ ] Add retry logic to payment gateway calls (OrderService.cs:42)

## Learnings Captured
- User prefers explicit mapping over AutoMapper → MEMORY.md

Written to .claude/handoff.md. See you next session.
```

## Related

- `/checkpoint` — Mid-session save (commit + brief note) without ending the session
- `instinct-system` — Confidence-scored pattern capture fed by the Learned section
- `instinct-system` — Turns user corrections into permanent MEMORY.md rules
