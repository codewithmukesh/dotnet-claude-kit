---
name: session-management
description: >
  Session-START bootstrap for .NET projects: load .claude/handoff.md, MEMORY.md,
  and instincts, detect the solution file, and present a resume summary. Load
  this skill when starting or resuming a session, when the user says "new
  session", "pick up where we left off", "what were we working on", "resume",
  or when Claude needs to bootstrap itself in an unfamiliar project. Session
  ENDING is handled by the /wrap-up workflow; mid-session saves by /checkpoint.
---

# Session Management

## Core Principles

1. **Sessions start with context, not from scratch** — Every session begins by loading three files: `.claude/handoff.md` (pending work), `MEMORY.md` (permanent rules), and `.claude/instincts.md` (learned patterns). Then detect the .NET solution so MCP tools are connected. A session that starts blind wastes the first 10 minutes re-discovering what was already known.

2. **Sessions end with capture, never abruptly** — When a session ends, three things are captured: what was DONE, what is PENDING, and what was LEARNED. This is non-negotiable. Context lost between sessions is context the user must re-provide, which wastes their time.

3. **Context preservation is a chain** — Handoff files pass state session-to-session. MEMORY.md accumulates permanent rules. Instincts track emerging patterns. Git commits preserve code state. Together, these four mechanisms create continuity that no single mechanism can provide alone.

4. **Solution detection enables tooling** — .NET MCP tools (`get_diagnostics`, `find_symbol`, `get_project_graph`) require a loaded solution. Detecting the `.slnx`/`.sln` file on session start ensures these tools are available from the first prompt, not discovered mid-conversation.

5. **Graceful degradation over hard failure** — If no handoff file exists, start clean. If no MEMORY.md exists, offer to create one on first learning. If no solution file is found, work without MCP tools. Never block a session because a context file is missing.

## Patterns

### Session Start Protocol

Execute this sequence at the beginning of every session:

```
STEP 1: Load Handoff
  → Check for .claude/handoff.md
  → If found: read and summarize pending work
  → If not found: note "No handoff file — starting fresh"

STEP 2: Load Memory
  → Check for MEMORY.md (project root or .claude/)
  → If found: scan for rules relevant to the likely task
  → If not found: note "No memory file — will create on first learning"

STEP 3: Load Instincts
  → Check for .claude/instincts.md
  → If found: load instincts at 0.7+ into active context
  → If not found: note "No instincts file — will create on first observation"

STEP 4: Detect .NET Solution
  → Search for .slnx files in current directory
  → If not found, search for .sln files
  → If not found, search parent directories (up to 3 levels)
  → If not found, search child directories (1 level)
  → If found: confirm MCP tools are connected
  → If not found: warn "No solution detected — MCP tools unavailable"

STEP 5: Present Summary
  "Session context loaded:
   - Last session: [summary from handoff or 'no previous session']
   - Pending tasks: [list from handoff or 'none']
   - Active rules: [count from MEMORY.md]
   - Active instincts: [count at 0.7+]
   - Solution: [solution name and path, or 'not detected']
   Ready to continue. What would you like to work on?"
```

### Session End Protocol

Execute this sequence when the session is ending:

```
STEP 1: Review Accomplishments
  → List everything completed this session with file paths
  → Include line numbers for significant changes

STEP 2: Check for Uncommitted Changes
  → Run git status
  → If uncommitted changes exist:
    "You have uncommitted changes. Want me to commit before wrapping up?"
  → If clean: note "All changes committed"

STEP 3: Write Handoff
  → Write .claude/handoff.md using the Handoff File Template (see below)

STEP 4: Extract Learnings
  → Review session for corrections from the user
  → Generalize corrections into rules (via self-correction-loop)
  → Write to MEMORY.md under appropriate category

STEP 5: Update Instincts
  → Review any new patterns observed during the session
  → Update confidence scores in .claude/instincts.md
  → Promote any instincts that reached 0.9 (via instinct-system)

STEP 6: Confirm
  "Session wrapped up:
   - Handoff written to .claude/handoff.md
   - [N] learnings added to MEMORY.md
   - [N] instincts updated
   Next session will pick up right where we left off."
```

### Solution Detection Strategy

Find the .NET solution for MCP tool connectivity:

```
SEARCH ORDER:
1. Current directory: *.slnx, *.sln
2. Parent directory: *.slnx, *.sln (common in src/ subdirectory layouts)
3. Grandparent directory: *.slnx, *.sln (up to 3 levels)
4. Child directories: */**.slnx, */**.sln (1 level deep)

PREFERENCE:
- .slnx over .sln (modern format)
- If multiple solutions found, prefer the one matching the directory name
- If still ambiguous, list all and ask the user

AFTER DETECTION:
- Confirm MCP connection by running get_project_graph
- If MCP returns "loading", wait briefly and retry (solution may be initializing)
- Cache the solution path for the session — don't re-detect on every tool call
```

### Context Preservation Architecture

Four mechanisms work together to prevent context loss:

```
FILE                    SCOPE       LIFETIME        PURPOSE
.claude/handoff.md      Session     Overwritten      Pass state between sessions
MEMORY.md               Project     Permanent        Store confirmed rules
.claude/instincts.md    Project     Evolving         Track emerging patterns
Git commits             Code        Permanent        Preserve code state

FLOW:
  Session N ends → writes handoff.md, updates MEMORY.md, updates instincts.md
  Session N+1 starts → reads handoff.md, MEMORY.md, instincts.md
  Result: zero context loss between sessions
```

### Handoff File Template

The standard format for `.claude/handoff.md`:

```markdown
# Session Handoff

> Generated: 2025-07-15 | Branch: feature/order-validation

## Completed
- [x] Added FluentValidation to CreateOrder command
  - File: `src/Orders/Features/CreateOrder.cs` (lines 15-35)
  - Validator: non-empty CustomerId, at least 1 item, positive quantities
- [x] Fixed N+1 query in GetOrderDetails
  - File: `src/Orders/Features/GetOrderDetails.cs` (line 28)
  - Added `.Include(o => o.Items)` to the query

## Pending
- [ ] Add validation to UpdateOrder command (same pattern as CreateOrder)
  - Start from: `src/Orders/Features/UpdateOrder.cs`
  - Reference: CreateOrder validator for the established pattern
- [ ] Run full test suite — last run had 2 unrelated failures in Catalog module

## Learned
- FluentValidation validators must be registered in the module's DI setup
- The N+1 in GetOrderDetails was hidden because test data seeds only 1 item per order

## Context
- Branch: feature/order-validation
- Last commit: "Add CreateOrder validation + tests"
- Uncommitted changes: no
- Solution: src/MyApp.slnx
```

### Resuming from Handoff

When a handoff file exists, present a clear summary and let the user decide:

```
SESSION RESUME FLOW:
1. Read .claude/handoff.md
2. Summarize concisely:
   "Last session (2025-07-15) on branch feature/order-validation:
    - Completed: CreateOrder validation, N+1 fix in GetOrderDetails
    - Pending: UpdateOrder validation, test suite failures
    Shall I continue with the UpdateOrder validation?"
3. Wait for user direction — never auto-start pending work
4. If the user wants something different, acknowledge and proceed
   "Got it, setting aside the pending tasks. What would you like to work on?"
```

### First Session Bootstrap

When no context files exist (brand new project or first Claude session):

```
BOOTSTRAP PROTOCOL:
1. No handoff.md → "No previous session found. Starting fresh."
2. No MEMORY.md → "No project memory found. I'll create one when we discover
   project-specific rules."
3. No instincts.md → "No instincts file. I'll start tracking patterns
   as we work together."
4. Detect solution → Run full detection, report findings
5. Offer convention scan:
   "This is our first session. Want me to scan the codebase to learn
    the project's conventions? (Uses the convention-learner skill)"
```

### Multi-Developer Handoff

When the handoff might be read by a different developer or Claude session:

```
ENHANCED HANDOFF (add when multiple developers use Claude on the project):

## Decisions Made
| Decision | Choice | Rationale |
|----------|--------|-----------|
| Validation library | FluentValidation | Already used in Catalog module |
| Error format | ProblemDetails | RFC 7807, consistent with API standard |

## Open Questions
- Should we validate product existence at command level or handler level?
  - Current: handler level (after DB lookup)
  - Trade-off: invalid product IDs return 500 instead of 400

## Dependencies
- Requires FluentValidation 11.x NuGet package (already referenced)
- No new infrastructure dependencies
```

## Anti-patterns

### Starting Blind

```
# BAD — ignoring existing context
*Session starts*
Claude: "How can I help you today?"
*User: "Continue the order validation work"*
Claude: "What order validation work? Can you tell me what files to look at?"
*User wastes 5 minutes re-explaining*

# GOOD — loading context automatically
*Session starts*
Claude: "Last session completed CreateOrder validation. Pending: UpdateOrder
         validation (start from src/Orders/Features/UpdateOrder.cs).
         Shall I continue with that?"
```

### Ending Without Capture

```
# BAD — session ends, context evaporates
User: "Thanks, that's all for today!"
Claude: "You're welcome! Happy to help anytime."
*Next session starts from scratch*

# GOOD — capture before closing
User: "Thanks, that's all for today!"
Claude: "Before we wrap up, let me save our progress to .claude/handoff.md
         so the next session picks up right where we left off."
*Writes handoff, extracts learnings, updates instincts*
```

### Overwriting Without Consent

```
# BAD — silently overwriting an existing handoff with different pending tasks
*Previous handoff has 3 pending tasks from another developer*
*This session worked on something different*
*Overwrites handoff, losing the other developer's pending tasks*

# GOOD — check before overwriting
"There's an existing handoff from [date] with pending tasks:
 - [task 1], [task 2], [task 3]
 These appear unrelated to our session. Should I:
 a) Merge our session with existing pending tasks
 b) Overwrite (their tasks are done or no longer relevant)
 c) Skip writing handoff this time"
```

### Skipping Solution Detection

```
# BAD — trying to use MCP tools without a loaded solution
Claude: "Let me check diagnostics..." *tool fails*
Claude: "Let me find the symbol..." *tool fails*
Claude: "I'll just read the files manually" *misses project-wide context*

# GOOD — detect solution on start, verify MCP connectivity
*Session start*
Claude: "Detected solution at src/MyApp.slnx. MCP tools connected.
         Project graph shows 5 projects with 3 test projects."
*All MCP tools work throughout the session*
```

### Bloated Handoffs

```
# BAD — handoff file is 500 lines with every detail
## Completed
- Changed line 15 in file A from X to Y because Z and also considered W...
*So long that the next session's context window is wasted on the handoff*

# GOOD — concise, actionable handoff
## Completed
- [x] Added CreateOrder validation (src/Orders/Features/CreateOrder.cs:15-35)
*Reference the diff or commit for full details, don't duplicate them*
```

### Context File Sprawl

```
# BAD — multiple context files with overlapping purposes
.claude/
  handoff.md
  handoff-backup.md
  session-notes-july.md
  session-notes-august.md
  todo.md
  context.md
*6 files, unclear which is authoritative*

# GOOD — exactly 3 context files, each with a clear purpose
.claude/
  handoff.md       ← session-to-session state (overwritten each time)
  instincts.md     ← emerging patterns (evolving)
MEMORY.md          ← permanent rules (append-only, audited)
```

## Decision Guide

| Scenario | Action |
|----------|--------|
| Starting a new session | Run full Session Start Protocol (5 steps) |
| User says "wrap up" / "done" / "that's all" | Run full Session End Protocol (6 steps) |
| No handoff.md exists | Start clean, create on first session end |
| No MEMORY.md exists | Offer to create on first correction or learning |
| No solution file found | Warn user, work without MCP tools, suggest creating one |
| Multiple solution files found | List all, ask user which to use |
| Handoff has pending tasks from another dev | Ask before overwriting: merge, overwrite, or skip |
| User wants to resume pending work | Summarize and confirm before starting |
| User wants something different from handoff | Acknowledge, proceed with new task, update handoff at end |
| Session had user corrections | Extract to MEMORY.md before ending |
| Session discovered new patterns | Update instincts.md before ending |
| First-ever session on a project | Run bootstrap protocol, offer convention scan |
| Solution is still loading (MCP returns "loading") | Wait 5 seconds, retry once, then proceed without MCP |
| Mid-session context getting large | Offload research to subagents, keep main context focused |
| User asks "what were we working on?" | Read handoff.md and summarize |
