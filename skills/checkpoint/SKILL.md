---
name: checkpoint
description: >
  Save progress by creating a descriptive git commit and writing a handoff note.
  Use when the user wants to checkpoint work, save progress before a risky change,
  switch tasks, or pause for the day. Invoke when the user says "checkpoint",
  "save progress", "commit and handoff", "save state", "pause here".
---

# /checkpoint

## What

Creates a progress checkpoint by performing two actions in sequence:

1. **Git commit** -- Stages all relevant changes and creates a descriptive commit
   message summarizing the current work.
2. **Handoff note** -- Writes `.claude/handoff.md` capturing what was done, what
   is pending, and what was learned so the next session can resume immediately.

This is a quick, safe operation -- it preserves your current state without pushing
to remote or modifying branches.

## When

- User says "save progress", "checkpoint", "commit what we have"
- Before switching to a different task or feature
- Before attempting a risky refactor (checkpoint the known-good state)
- Before ending a session (lighter alternative to full `/wrap-up`)
- After completing a logical unit of work mid-session

## How

1. **Assess state** -- Run `git status` and `git diff` to understand staged and
   unstaged changes.
2. **Stage changes** -- Stage relevant files. Exclude generated files, build
   artifacts, and secrets (`.env`, credentials). Prefer staging specific files
   over `git add -A`.
3. **Create commit** -- Write a descriptive commit message that summarizes the
   work completed. Follow the repository's existing commit message style.
4. **Write handoff** -- Create or overwrite `.claude/handoff.md` with:
   - **Completed** -- What was accomplished (with file paths and line references)
   - **Pending** -- What remains to be done (with specific next steps)
   - **Learned** -- Any discoveries, corrections, or non-obvious findings
   - **Context** -- Current branch, last commit hash, uncommitted state
5. **Confirm** -- Report the commit hash and summarize the handoff contents.

### Safety Rules

- Never force-push or modify remote branches
- Never commit files that contain secrets
- Never amend a previous commit -- always create a new one
- If there are no changes to commit, skip the commit and only update the handoff

## Invokes

| Type  | Name              | Purpose                                          |
|-------|-------------------|--------------------------------------------------|
| Skill | `wrap-up-ritual`  | Handoff note structure and learning extraction    |
| Skill | `self-correction-loop` | Capture any learnings into MEMORY.md        |

## Example

```
User: /checkpoint

Claude: Assessing current state...
  3 files modified, 1 new file.

  Committing: "Add CreateOrder validation with FluentValidation"
  - src/Orders/Features/CreateOrder.cs
  - src/Orders/Features/CreateOrderValidator.cs (new)
  - tests/Orders.Tests/Features/CreateOrderTests.cs

  Commit: a1b2c3d

  Writing handoff to .claude/handoff.md:
  ## Completed
  - [x] Added FluentValidation to CreateOrder (src/Orders/Features/CreateOrder.cs)
  - [x] Integration test covering 3 validation scenarios

  ## Pending
  - [ ] Add same validation pattern to UpdateOrder
  - [ ] Add validation to DeleteOrder (check authorization first)

  ## Learned
  - FluentValidation validators must be registered in module DI setup

  Checkpoint saved. Safe to switch tasks or end session.
```

## Related

- `/wrap-up` -- Full session wrap-up ritual with deeper learning extraction
- `/build-fix` -- Fix build errors before checkpointing
