---
name: model-selection
description: >
  Strategic Claude model selection for .NET development workflows. Guides when
  to use Fable (frontier reasoning, highest-stakes decisions) vs Opus (deep
  reasoning, architecture, ambiguous problems) vs Sonnet (throughput, large
  context, routine implementation) vs Haiku (fast, cheap subagent tasks).
  Covers model switching workflows, subagent model assignment, and
  cost-effective task routing. Load this skill when choosing models for tasks,
  optimizing costs, working with subagents, or when the user mentions "model",
  "Fable", "Opus", "Sonnet", "Haiku", "which model", "cost", "switch model",
  or "fast mode".
---

# Model Selection

Current lineup (mid-2026): **Fable 5** (frontier tier, above Opus), **Opus 4.8**,
**Sonnet 4.6**, **Haiku 4.5**. Always use the aliases (`fable`, `opus`, `sonnet`,
`haiku`) in configuration and frontmatter — they track the latest version of each
tier automatically, so guidance never goes stale.

## Core Principles

1. **Match model to complexity, not size** — A 50-file refactor that follows a clear pattern is a Sonnet task (high throughput, simple logic). A 3-file architecture decision with trade-offs is an Opus or Fable task (deep reasoning). File count and complexity are orthogonal.

2. **Sonnet is the workhorse** — 80% of .NET development tasks are routine: implement a feature following an established pattern, write tests, fix a known bug, run scaffolding. Sonnet handles all of these at higher speed and lower cost.

3. **Opus is the architect** — Use Opus for tasks that require weighing trade-offs, reasoning about system design, debugging subtle issues, or making decisions with incomplete information. Opus excels when the answer isn't obvious.

4. **Fable is the frontier** — Fable sits above Opus in capability. Reserve it for the highest-stakes work: greenfield architecture for a long-lived system, untangling a problem that resisted Opus, or reviews where a missed subtle issue is very expensive. If Opus would do fine, use Opus.

5. **Context window is a budget, not a dumping ground** — A large context window enables working with big codebases but doesn't mean you should load everything. Apply `context-discipline` principles regardless of model. A focused session outperforms a bloated one.

6. **Haiku for fire-and-forget subagents** — When a subagent does a simple lookup, runs a script, or fetches information, Haiku is fast and cheap. Reserve heavier models for subagents that need to reason.

## Patterns

### Task Complexity Assessment

Classify each task to select the right model:

```
ROUTINE TASKS → Sonnet
- Implement a feature following an existing pattern
- Write tests for existing code
- Fix a bug with a clear stack trace
- Add a new endpoint matching the project's convention
- Run scaffolding or code generation
- Apply a known refactoring pattern
- Format, lint, or fix build errors
- Write documentation from existing code

COMPLEX TASKS → Opus
- Design a new module or subsystem from scratch
- Choose between architecture approaches (VSA vs CA vs DDD)
- Debug a subtle issue with no clear stack trace
- Refactor with trade-offs (performance vs readability, consistency vs simplicity)
- Review architecture for design flaws
- Make decisions with incomplete or conflicting requirements
- Untangle complex dependencies or circular references
- Write a migration strategy for breaking changes

HIGHEST-STAKES TASKS → Fable
- Architect a greenfield system expected to live for years
- Debug an issue that survived an Opus investigation
- Security or architecture review where a miss is very expensive
- Multi-system analysis with deeply interacting constraints

SIMPLE TASKS → Haiku (subagents only)
- Look up a file path or symbol location
- Run a build/test and report results
- Search for a pattern across files
- Summarize a file or module
- Format or validate data
```

### Model Switching Workflow

The most effective pattern: a reasoning model plans, Sonnet executes, a reasoning model reviews.

```
PHASE 1: PLAN (Opus — Fable for highest-stakes designs)
├── Analyze requirements and constraints
├── Identify architectural trade-offs
├── Design the approach with rationale
├── Define acceptance criteria
└── Output: detailed implementation plan

PHASE 2: EXECUTE (Sonnet)
├── Implement following the plan
├── Write code, tests, configurations
├── Run build and test verification
├── Handle routine issues (compilation errors, test fixes)
└── Output: working implementation

PHASE 3: REVIEW (Opus — Fable when a missed issue is very expensive)
├── Review implementation against the plan
├── Check for subtle issues (race conditions, N+1, security)
├── Evaluate architectural compliance
├── Suggest refinements
└── Output: approval or specific revision requests
```

How to switch in practice:

```
Claude Code CLI:
- /model fable   → Switch to Fable (highest-stakes planning/review)
- /model opus    → Switch to Opus (planning/review phases)
- /model sonnet  → Switch to Sonnet (implementation phase)
- /model auto    → Let Claude Code choose based on task

Toggle fast mode:
- /fast          → Toggle fast mode (Opus with faster output for throughput)
```

### Subagent Model Assignment

Assign models to subagents based on task complexity:

```
SUBAGENT: "Find all authentication middleware in the project"
→ MODEL: Haiku
→ WHY: Simple search task, no reasoning required

SUBAGENT: "Run dotnet test and summarize failures"
→ MODEL: Haiku
→ WHY: Execute command, parse output, no complex analysis

SUBAGENT: "Analyze the dependency graph for circular references and suggest fixes"
→ MODEL: Sonnet
→ WHY: Needs to understand project structure and propose solutions

SUBAGENT: "Review this PR for architectural issues and security vulnerabilities"
→ MODEL: Opus
→ WHY: Deep reasoning about trade-offs, subtle issue detection

SUBAGENT: "Summarize what the Orders module does"
→ MODEL: Haiku or Sonnet
→ WHY: Haiku for a quick overview, Sonnet if the module is complex
```

## Anti-patterns

### Using Heavy Models for Simple Tasks

```
// BAD — Opus (or Fable) for a routine CRUD endpoint
Using Opus to implement GetOrderById following the exact same pattern
as the existing GetCustomerById. No decisions to make, just pattern replication.
*Slower and more expensive than needed*

// GOOD — Sonnet for pattern replication
Using Sonnet to implement GetOrderById. The pattern is established,
the code is straightforward, and Sonnet executes it faster.
```

### Using Sonnet for Architecture Decisions

```
// BAD — Sonnet for "should we use VSA or Clean Architecture?"
Sonnet gives a reasonable answer but may miss nuanced trade-offs
about team size, domain complexity, and long-term maintenance.
*The wrong architecture costs months of refactoring*

// GOOD — Opus (or Fable for long-lived systems) for architectural decisions
Opus weighs team size, domain complexity, current codebase patterns,
and long-term implications. The architecture decision is worth the
extra reasoning power.
```

### Same Model for All Subagents

```
// BAD — all subagents use Opus
5 subagents running Opus:
- "Find OrderService.cs" (Haiku could do this)
- "Run dotnet test" (Haiku could do this)
- "Summarize the Catalog module" (Haiku/Sonnet could do this)
- "Analyze circular dependencies" (Sonnet is sufficient)
- "Review architecture for security issues" (Opus is appropriate)
*4 out of 5 subagents are using more model than needed*

// GOOD — model matches subagent task
- "Find OrderService.cs" → Haiku
- "Run dotnet test" → Haiku
- "Summarize the Catalog module" → Haiku
- "Analyze circular dependencies" → Sonnet
- "Review architecture for security issues" → Opus
```

### Hardcoding Model Versions

```
// BAD — pinned version in agent frontmatter or docs
model: claude-opus-4-6   ← rots when the next version ships

// GOOD — tier alias that tracks the latest version
model: opus
```

## Decision Guide

| Scenario | Model | Rationale |
|----------|-------|-----------|
| Architect a greenfield, long-lived system | Fable | Highest-stakes decision, frontier reasoning |
| Plan a new feature or module | Opus | Requires weighing trade-offs |
| Implement a feature following existing patterns | Sonnet | Pattern replication, high throughput |
| Debug a subtle intermittent issue | Opus | Requires deep reasoning about state/timing |
| Debug an issue that resisted an Opus pass | Fable | Escalate when deep reasoning wasn't enough |
| Fix a compilation error | Sonnet | Clear error, mechanical fix |
| Write tests for existing code | Sonnet | Test patterns are established |
| Architecture review / PR review | Opus | Subtle issues need deep analysis |
| Code review for anti-patterns | Sonnet | Pattern matching, well-defined rules |
| Refactor across many files (same pattern) | Sonnet | Volume + consistency, not deep reasoning |
| Design database schema from requirements | Opus | Normalization trade-offs, domain modeling |
| Subagent: file lookup or search | Haiku | Simple task, fast and cheap |
| Subagent: summarize a module | Haiku | Straightforward reading + compression |
| Subagent: analyze dependencies | Sonnet | Needs to reason about structure |
| Working in a very large codebase | Sonnet | Large context window + discipline |
| End-of-day wrap-up / handoff | Sonnet | Structured capture, no deep reasoning |
