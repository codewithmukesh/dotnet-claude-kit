---
name: wrap-up-ritual
description: >
  The methodology behind the /wrap-up session ending: the handoff note
  structure for .claude/handoff.md, learning extraction into MEMORY.md, and
  multi-developer handoff templates. Loaded automatically by the /wrap-up
  workflow — when the user says "wrap up" or "done for today", use /wrap-up.
  Load this skill directly when designing handoff content, deciding what
  counts as a capturable learning, or improving session-ending habits.
---

# Wrap-Up Ritual

## Core Principles

1. **Sessions are ephemeral, knowledge is permanent** — When a session ends, context is lost. But learnings, decisions, and progress don't have to be. The wrap-up ritual bridges the gap between sessions by writing a handoff note.

2. **Three captures every time** — Every session ending captures exactly three things: what was DONE, what is PENDING, and what was LEARNED. No exceptions. Skipping any of these creates gaps for the next session.

3. **Handoff notes are written for a stranger** — Write the handoff as if the next person has zero context. Include file paths, decision rationale, and specific next steps. "Continue the refactor" is useless. "Refactor `src/Orders/CreateOrder.cs` to use the Result pattern — see the Catalog module for the established pattern" is actionable.

4. **Consistent location, always overwritten** — The handoff file lives at `.claude/handoff.md`. Each session overwrites the previous one — there's only ever one active handoff. Old handoffs are not valuable; current state is.

5. **Learnings flow to permanent memory** — The "learned" section of a wrap-up is a trigger for the `self-correction-loop` skill. Any correction or discovery worth remembering should be captured in `MEMORY.md` as a permanent rule, not just in the ephemeral handoff.

## Patterns

### Session Summary Template

The handoff file follows a consistent structure:

```markdown
# Session Handoff

> Generated: 2025-07-15 | Branch: feature/order-validation

## Completed
- [x] Added FluentValidation to CreateOrder command
  - File: `src/Orders/Features/CreateOrder.cs` (lines 15-35)
  - Validator checks: non-empty CustomerId, at least 1 item, positive quantities
- [x] Added integration test for validation
  - File: `tests/Orders.Tests/Features/CreateOrderTests.cs`
  - Tests: InvalidCustomerId_Returns400, EmptyItems_Returns400
- [x] Fixed N+1 query in GetOrderDetails
  - File: `src/Orders/Features/GetOrderDetails.cs` (line 28)
  - Added `.Include(o => o.Items)` to the query

## Pending
- [ ] Add validation to UpdateOrder command (same pattern as CreateOrder)
  - Start from: `src/Orders/Features/UpdateOrder.cs`
  - Reference: CreateOrder validator for the established pattern
- [ ] Run full test suite — last run had 2 unrelated failures in Catalog module
  - Failures: `CatalogTests.GetProduct_NotFound` and `CatalogTests.ListProducts_Pagination`
  - These appear pre-existing, not caused by today's changes

## Learned
- FluentValidation validators must be registered in the module's DI setup
  (added to `OrdersModule.cs` line 12) — easy to forget
- The N+1 in GetOrderDetails was not caught by existing tests because the test
  fixture seeds only 1 item per order. Consider adding multi-item test data.

## Context
- Working in the `feature/order-validation` branch
- All changes committed up to "Add CreateOrder validation + tests"
- No uncommitted changes
```

### Trigger Detection

Recognize when the user is ending a session:

```
EXPLICIT SIGNALS:
- "Let's wrap up"
- "That's all for today"
- "I'm done"
- "Save progress"
- "Let's stop here"
- "End of session"
- "Handoff"
- "Pick this up tomorrow"

IMPLICIT SIGNALS:
- User says "thanks" after a series of completed tasks
- User says "good enough for now"
- Long pause after completing a task followed by no new request

RESPONSE:
When you detect a wrap-up signal, offer to write the handoff:
"Let me write a handoff note to .claude/handoff.md so the next session
can pick up exactly where we left off."
```

### Learning Extraction at Session End

Before writing the handoff, review the session for learnings:

```
LEARNING EXTRACTION CHECKLIST:
1. Did the user correct anything? → Capture in MEMORY.md (via self-correction-loop)
2. Did we discover something non-obvious? → Add to "Learned" section
3. Did we make a decision with rationale? → Document the "why" in handoff
4. Did a tool/approach not work? → Note it so the next session doesn't retry
5. Did we find a pattern worth reusing? → Note for potential skill/memory addition

EXAMPLES OF GOOD LEARNINGS:
- "EF Core compiled queries don't work with Include() — use regular queries"
- "The payment gateway sandbox returns 500 on amounts > $10,000"
- "FluentValidation async validators need .MustAsync(), not .Must() with await"

EXAMPLES OF BAD LEARNINGS:
- "We worked on the Orders module" (too vague)
- "Things went well" (not actionable)
- "Used TimeProvider" (already a known rule, not a learning)
```

## Anti-patterns

### Abrupt Endings

```
// BAD — session ends with no capture
User: "Thanks, that's all!"
Claude: "You're welcome! Happy to help."
*Next session: "What were we working on? Let me read through the git log..."*

// GOOD — session ends with a handoff
User: "Thanks, that's all!"
Claude: "Before we wrap up, let me write a handoff note to .claude/handoff.md
         so the next session can pick up where we left off."
*Next session: reads handoff, immediately knows the state*
```

### Vague Handoffs

```
// BAD — handoff that doesn't help
## Completed
- Worked on Orders module

## Pending
- More work on Orders

// GOOD — specific, actionable handoff
## Completed
- [x] Added CreateOrder validation (src/Orders/Features/CreateOrder.cs:15-35)
- [x] Integration test for validation (tests/Orders.Tests/Features/CreateOrderTests.cs)

## Pending
- [ ] Add same validation pattern to UpdateOrder (src/Orders/Features/UpdateOrder.cs)
  - Reference CreateOrder validator for the pattern
```

### Accumulating Handoff Files

```
// BAD — creating timestamped handoff files
.claude/
├── handoff-2025-07-13.md
├── handoff-2025-07-14.md
├── handoff-2025-07-15.md
*3 files, unclear which is current, stale data accumulating*

// GOOD — single file, always overwritten
.claude/
├── handoff.md    ← always the current state
*Permanent learnings go to MEMORY.md, not handoff files*
```

### Skipping the Learning Extraction

```
// BAD — handoff without learnings
## Completed
- Fixed N+1 query
## Pending
- Nothing

*The discovery that seeded test data only has 1 item per order (hiding N+1s)
 is lost forever*

// GOOD — extract and preserve the insight
## Learned
- Test fixture seeds only 1 item per order, which hides N+1 queries.
  Consider adding multi-item test data to the default fixture.
→ Also added to MEMORY.md > Testing: "Seed test data with multiple
  child entities to catch N+1 queries"
```

## Decision Guide

| Scenario | Action |
|----------|--------|
| User says "wrap up" / "that's all" / "done" | Write handoff to `.claude/handoff.md` |
| Session completed multiple tasks | List each with file paths and line numbers |
| Session had user corrections | Extract to MEMORY.md AND note in handoff Learned section |
| Next session is likely a different person | Include Decisions Made table with rationale |
| Session had no pending work | Still write a handoff — document what was completed and learned |
| Previous handoff exists | Overwrite it — only the current state matters |
| Work was on a feature branch | Include branch name and last commit message in handoff |
| Session ended with failing tests | Document which tests fail and suspected cause in Pending |
| User doesn't want a handoff | Respect it — but suggest capturing learnings in MEMORY.md at minimum |
| Session was purely exploratory (no code changes) | Write a lighter handoff with findings and recommendations |
