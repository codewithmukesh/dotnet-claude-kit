---
name: checkpoint
description: >
  Mid-session save point: create a descriptive git commit and a brief handoff
  note, then keep working. Use before a risky change or refactor, when
  switching tasks, or to bank progress without ending the session. Triggers
  on: /checkpoint, "checkpoint", "save progress", "commit and handoff",
  "save state", "pause here", "before a risky change". For the full
  end-of-session ritual with learning extraction, use /wrap-up instead.
---

# /checkpoint

## What

A quick mid-session save that banks the known-good state in two moves:

1. **Descriptive git commit** — stage relevant changes and commit with a
   message that summarizes the work.
2. **Brief handoff note** — write `.claude/handoff.md` so a resumed session
   (or you, after a botched refactor) knows exactly where things stand.

Checkpoint is the mid-session save; `/wrap-up` is the end-of-session ritual.
Checkpoint commits and jots a note, then keeps working. Wrap-up does the full
handoff plus learning extraction into `MEMORY.md`.

## When

- Before a risky refactor or destructive change — checkpoint the known-good state
- When switching to a different task or feature mid-session
- After completing a logical unit of work — bank it
- User says "checkpoint", "save progress", "save state", "pause here"
- If the session is actually ending, use `/wrap-up` for the full ritual

## How

1. **Assess state** — `git status` and `git diff` to see staged and unstaged
   changes.
2. **Stage changes** — Stage specific files; never `git add -A`. Exclude
   generated files, build artifacts, and secrets (`.env`, credentials).
3. **Commit** — Descriptive message following the repo's commit style
   (conventional prefixes: `feat:`, `fix:`, `refactor:`). New commit, never
   amend. If there is nothing to commit, skip to the handoff.
4. **Write handoff note** — `.claude/handoff.md`, same format as `/wrap-up`
   but abbreviated: Completed, Pending (with specific next steps), Learned
   (only if something non-obvious surfaced), and Context (branch + commit
   hash). One file, always overwritten.
5. **Confirm** — Report the commit hash and the handoff summary, then resume
   work.

### Safety Rules

- Never push, force-push, or touch remote branches
- Never commit secrets
- Never amend — a checkpoint is a new commit you can return to
- No changes to commit? Update the handoff only

## Example

```
User: /checkpoint — about to try the EF Core split-query refactor

Claude: 3 files modified, 1 new.

  Commit a1b2c3d: "feat: add CreateOrder validation with FluentValidation"
  - src/Orders/Features/CreateOrder.cs
  - src/Orders/Features/CreateOrderValidator.cs (new)
  - tests/Orders.Tests/Features/CreateOrderTests.cs

  Handoff updated (.claude/handoff.md):
  ## Completed
  - [x] CreateOrder validation + 3 test scenarios
  ## Pending
  - [ ] Split-query refactor in GetOrderDetails (about to attempt)
  ## Context
  - Branch: feature/order-validation | Checkpoint: a1b2c3d

  Checkpoint saved — safe to proceed with the risky refactor.
```

## Related

- `/wrap-up` — End-of-session ritual: full handoff format definition plus
  learning extraction into MEMORY.md
- `/build-fix` — Get the build green before checkpointing
