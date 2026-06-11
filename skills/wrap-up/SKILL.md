---
name: wrap-up
description: >
  End-of-session wrap-up ritual. Triggers on: /wrap-up, "done for today",
  "wrap up", "that's all", "end session", "signing off".
  Captures completed work, pending tasks, and learnings for the next session.
---

# /wrap-up

## What

An end-of-session ritual that ensures continuity between coding sessions by:

1. Reviewing what was accomplished during the session
2. Writing a handoff document at `.claude/handoff.md` for the next session
3. Extracting learnings and corrections into `MEMORY.md` for long-term retention

## When

- End of a working session -- "done for today", "that's all"
- Before switching to a different project or context
- After completing a major milestone -- capture state before moving on
- When the user says "wrap up" or "signing off"

## How

### Step 1: Invoke Skills

- **Skill**: `wrap-up-ritual` -- Provides the handoff template and checklist
- **Skill**: `session-management` -- Handles reading/writing session state files

### Step 2: Review the Session

Analyze the current session to identify:

- Files created or modified (from git status/diff)
- Tasks completed vs tasks started but unfinished
- Decisions made and their rationale
- Any user corrections or preferences observed

### Step 3: Write Handoff and Learnings

- Write `.claude/handoff.md` with: completed work, pending tasks, blockers, and next steps
- Append new learnings to `MEMORY.md` if any corrections or patterns were discovered
- Report the summary to the user for confirmation

## Example

```
User: /wrap-up

Claude: Wrapping up session...

## Session Summary
- Implemented OrderService with validation pipeline
- Added integration tests for order creation endpoint
- Fixed N+1 query in GetOrdersByCustomer

## Pending
- [ ] Add retry logic to payment gateway calls (OrderService.cs:42)
- [ ] Wire up FluentValidation for UpdateOrderCommand

## Learnings Captured
- User prefers explicit mapping over AutoMapper
- This project uses vertical slice architecture

Written to .claude/handoff.md. See you next session.
```

## Related

- `/checkpoint` -- Mid-session state capture without ending the session
