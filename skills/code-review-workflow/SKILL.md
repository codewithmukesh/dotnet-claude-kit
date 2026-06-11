---
name: code-review-workflow
description: >
  The methodology behind the /code-review workflow: MCP-first analysis order,
  review dimensions (correctness, security, performance, architecture
  compliance, test coverage), severity classification, and finding format.
  Loaded automatically by the /code-review workflow and the code-reviewer
  agent — to RUN a review, use /code-review. Load this skill directly when
  designing a review process, calibrating severity levels, or defining what
  a review must cover.
---

# Code Review Workflow

## Core Principles

1. **MCP-first analysis** — Use Roslyn MCP tools before reading source files. `detect_antipatterns` catches more than manual scanning, `get_diagnostics` finds what the compiler knows, and `find_references` reveals blast radius. Only read files for context that tools can't provide.
2. **Structured output** — Every review follows the same format: Summary → Critical → Warnings → Suggestions → Architecture Compliance → Test Coverage → What's Good. Consistent structure makes reviews actionable and scannable.
3. **Severity-based findings** — Categorize every finding as Critical (must fix before merge), Warning (should fix, creates tech debt), or Suggestion (nice to have). Never mix severities — a cosmetic issue next to a security bug buries the important finding.
4. **Actionable suggestions** — Every finding includes: what's wrong, why it matters, and how to fix it. "This is bad" is not a review comment. "This creates N+1 queries because X. Fix by adding `.Include()` or using a projection" is.
5. **Acknowledge good work** — Always include a "What's Good" section. Positive reinforcement of good patterns is as important as flagging bad ones.

## Patterns

### Full PR Review Flow

Use for non-trivial PRs (3+ files changed, new features, refactors). Execute steps in order:

**Step 1: Understand the change scope**
Get changed files from git diff or user input. Categorize:
- New files (features, tests, configs)
- Modified files (which layers? domain, application, infrastructure, API?)
- Deleted files (was anything depending on them?)

**Step 2: Automated analysis**
Run MCP tools on changed files:
```
→ detect_antipatterns (file: each changed .cs file)
  Catch: async void, sync-over-async, DateTime.Now, new HttpClient(), broad catch, etc.

→ get_diagnostics (scope: file, path: each changed file)
  Catch: new compiler warnings, nullability issues, unused variables

→ get_public_api (typeName: each modified type)
  Check: API surface changes — new public members, removed members, signature changes
```

**Step 3: Blast radius assessment**
For each changed public API:
```
→ find_references (symbolName: changedMethod)
  Count callers. High count = high risk. Flag breaking changes.
```

**Step 4: Architecture compliance**
```
→ get_project_graph
  Verify: dependency direction is correct (Domain → nothing, Infra → Domain, Api → Application)
  Flag: circular references, wrong-direction dependencies
```

**Step 5: Test coverage check**
```
→ get_test_coverage_map (projectFilter: changed project)
  Check: do test files exist for every changed type?
  Flag: new types without tests, modified logic without test updates
```

**Step 6: Manual review**
Read changed files for things tools can't catch:
- Business logic correctness
- Naming clarity and consistency
- Error handling completeness
- Concurrency safety
- Security: input validation, authorization checks, data exposure

**Step 7: Produce review**

```markdown
## Review Summary
[1-2 sentence overall assessment: scope, risk level, recommendation]

## Critical (must fix)
- **[File:Line] [Title]** — [What's wrong]. [Why it matters]. [How to fix].
- ...

## Warnings (should fix)
- **[File:Line] [Title]** — [What's wrong]. [Impact if not fixed]. [Suggested fix].
- ...

## Suggestions (nice to have)
- **[File:Line] [Title]** — [Current approach]. [Better alternative]. [Why].
- ...

## Architecture Compliance
[Dependency direction check results. Layer violation findings. Module boundary enforcement.]

## Test Coverage
[Which changed types have tests. Which are missing. Specific test scenarios to add.]

## What's Good
- [Positive finding 1 — reinforce good patterns]
- [Positive finding 2]
- ...
```

### Quick Review

Use for small changes (1-2 files, bug fixes, config changes). Lightweight — skip blast radius and architecture checks.

**Steps:**
1. Run `detect_antipatterns` on changed files
2. Run `get_diagnostics` on changed files
3. Read the changed code for correctness
4. Produce abbreviated review (Summary + Issues + What's Good)

```markdown
## Quick Review
[1 sentence assessment]

### Issues
- [Finding with severity tag: 🔴 Critical / 🟡 Warning / 🔵 Suggestion]

### What's Good
- [Positive note]
```

### Architecture Compliance Check

Standalone check for architecture-level concerns. Use when reviewing project structure changes, new project additions, or module boundary modifications.

**Steps:**
1. Run `get_project_graph` — visualize the full dependency tree
2. Verify dependency rules per architecture:

| Architecture | Rule | Violation Example |
|-------------|------|-------------------|
| VSA | Features don't reference each other | Feature A imports from Feature B |
| Clean Architecture | Domain has zero project references | Domain references Infrastructure |
| DDD | Aggregates don't reference other aggregates | Order aggregate imports Product aggregate |
| Modular Monolith | Modules communicate only via integration events | Module A directly references Module B's DbContext |

3. Run `find_references` on module/layer boundary types to verify encapsulation:
```
→ find_references(symbolName: "OrdersDbContext")
  Should only be referenced within the Orders module.
  External references = module boundary violation.
```

4. Run `detect_circular_dependencies` to find cycles:
```
→ detect_circular_dependencies(scope: projects)
  Flag any project-level cycles.

→ detect_circular_dependencies(scope: types, projectFilter: "MyApp.Application")
  Flag type-level cycles within the application layer.
```

## Anti-patterns

### Reviewing Without MCP Tools

```
# BAD — Reading every file manually, missing patterns across the codebase
"Let me read OrderService.cs... looks fine to me."
# Missed: 3 DateTime.Now usages, 1 async void, 2 compiler warnings
```

```
# GOOD — MCP-first, then targeted file reads
→ detect_antipatterns: Found 3 DateTime.Now (AP004), 1 async void (AP001)
→ get_diagnostics: 2 CS8600 warnings in OrderService.cs
"I found 6 issues via static analysis. Let me read the files for business logic review..."
```

### Vague Feedback

```
# BAD
"The code could be better."
"This doesn't look right."
"Consider refactoring this."
```

```
# GOOD
"OrderService.cs:47 — `DateTime.Now` should be `TimeProvider.GetUtcNow()`.
DateTime.Now is untestable and uses local timezone. Inject TimeProvider
via primary constructor and call GetUtcNow()."
```

### Missing Security Checks

```
# BAD — Only checking code style and patterns
"Code looks clean, approved!"
# Missed: SQL injection in raw query, missing authorization attribute, exposed PII in logs
```

```
# GOOD — Security is a review dimension
"## Critical
- **OrderController.cs:23** Missing `[Authorize]` — endpoint exposes order data without auth
- **SearchService.cs:45** SQL injection — user input concatenated into raw SQL. Use parameterized query.
## Suggestions
- **LoggingMiddleware.cs:12** PII exposure — email logged at Information level. Mask or use Debug level."
```

### Blocking on Style, Ignoring Substance

```
# BAD — 10 comments about naming, 0 about the race condition
"Rename `svc` to `service`. Use `var` instead of explicit type. Add XML docs."
```

```
# GOOD — Prioritize by impact
"## Critical
- Race condition in OrderService.ProcessAsync — concurrent calls can double-charge
## Suggestions
- Consider renaming `svc` to `service` for clarity"
```

## Decision Guide

| Scenario | Review Type | MCP Tools |
|----------|------------|-----------|
| Feature PR (3+ files) | Full PR Review | All tools |
| Bug fix (1-2 files) | Quick Review | detect_antipatterns, get_diagnostics |
| Config/infra changes | Quick Review + Manual | get_project_graph |
| New project/module added | Architecture Compliance | get_project_graph, detect_circular_dependencies |
| Refactor PR | Full PR Review + Architecture | All tools + find_references (blast radius) |
| Security-sensitive change | Full PR Review → escalate to security-auditor | detect_antipatterns + manual security review |
| Test-only changes | Quick Review | get_diagnostics only |
| Performance-critical path | Full PR Review → escalate to performance-analyst | get_diagnostics + manual review |
