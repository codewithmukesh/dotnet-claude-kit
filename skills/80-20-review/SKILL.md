---
name: 80-20-review
description: >
  Review PRIORITIZATION — decide where review effort goes, not how to review.
  Focuses effort on the 20% of code that causes 80% of issues using blast
  radius scoring: data access, security, concurrency, and integration
  boundaries before formatting and style. Includes checkpoint schedules and
  critical path identification. Load this skill when deciding what to review
  or how deep, planning review checkpoints for a large change, or when the
  user mentions "what should I review", "review priorities", "blast radius",
  "critical path", or "review strategy". To RUN a review, use /code-review.
---

# 80/20 Review

## Core Principles

1. **Review at checkpoints, not continuously** — Constant review interrupts flow. Schedule reviews at natural breakpoints: post-implementation, pre-PR, post-integration, and post-deploy. Each checkpoint has a different focus.

2. **Focus on data access, security, concurrency, integration** — These are the 20% of code areas that cause 80% of production incidents. A missing `CancellationToken` is more dangerous than a misnamed variable. Review depth should match risk.

3. **Blast radius determines depth** — A utility function used in one place gets a glance. A middleware change that affects every request gets a thorough review. Score changes by blast radius and invest review time proportionally.

4. **Automate the trivial** — Formatting, import ordering, naming conventions, and basic anti-patterns should be caught by tools (formatters, analyzers, hooks), not humans. Save human attention for things tools can't catch: logic errors, design flaws, and missing edge cases.

## Patterns

### Checkpoint Schedule

Review at these natural breakpoints, each with a specific focus:

```
CHECKPOINT 1: Post-Implementation (self-review)
WHEN: After completing a feature or fix, before committing
FOCUS: Does it work? Does it compile? Do tests pass?
DEPTH: Quick — 5 minutes
CHECKLIST:
□ dotnet build passes
□ dotnet test passes (all existing + new tests)
□ get_diagnostics shows no new warnings
□ No obvious anti-patterns (DateTime.Now, new HttpClient, async void)

CHECKPOINT 2: Pre-PR (focused review)
WHEN: Before creating a pull request
FOCUS: Would a staff engineer approve this?
DEPTH: Thorough on critical paths, glance at routine code — 15-30 minutes
CHECKLIST:
□ Data access: N+1 queries, missing Include, no tracking where possible
□ Security: Auth checks, input validation, no secrets in code
□ Concurrency: CancellationToken propagated, no deadlocks, thread-safe state
□ Error handling: Result pattern used, no swallowed exceptions
□ API surface: TypedResults, proper status codes, response DTOs (not entities)
□ Integration: Events published correctly, consumer idempotency
□ Tests: Integration tests cover the happy path + main error case
□ Breaking changes: Public API surface unchanged (or intentionally changed)

CHECKPOINT 3: Post-Integration (system review)
WHEN: After merging to main or integrating with other modules
FOCUS: Does it play well with the rest of the system?
DEPTH: Targeted — check integration points — 10 minutes
CHECKLIST:
□ Cross-module events consumed correctly
□ Database migrations applied cleanly
□ No circular dependencies introduced
□ CI pipeline passes

CHECKPOINT 4: Post-Deploy (production readiness)
WHEN: Before or immediately after deploying
FOCUS: Is it safe in production?
DEPTH: Quick but critical — 5 minutes
CHECKLIST:
□ Health checks pass
□ Logs produce structured output (no PII)
□ Retry/circuit breaker policies configured for external calls
□ Feature flags in place for risky changes (if applicable)
```

### Critical Path Identification

Use MCP tools to identify the code that matters most:

```
HIGH-RISK CODE (review thoroughly):
1. Data access layer
   → find_references for DbContext usage
   → Check for N+1 (missing Include/AsSplitQuery), missing CancellationToken
   → Check for raw SQL injection risks

2. Authentication & authorization
   → find_implementations of IAuthorizationHandler
   → Check every endpoint has [Authorize] or explicit [AllowAnonymous]
   → Verify token validation configuration

3. External service integration
   → find_references for HttpClient, IHttpClientFactory
   → Check for retry policies (Polly), timeout configuration
   → Verify error handling for external failures

4. Concurrency & shared state
   → find_references for static fields, ConcurrentDictionary
   → Check BackgroundService implementations for scope management
   → Verify CancellationToken propagation in async chains

5. Message consumers
   → find_implementations of IConsumer
   → Check for idempotency (handle duplicate messages)
   → Verify error handling and dead letter configuration

LOW-RISK CODE (glance or skip):
- DTOs and record definitions
- Extension method registration (AddXxx pattern)
- Configuration binding (Options pattern)
- Simple CRUD with no business logic
- Test helper/fixture code
```

### Blast Radius Scoring

Score each change to determine review investment:

```
CRITICAL (30+ min review):
- Middleware changes (affects every request)
- Authentication/authorization changes
- Database schema changes (migrations)
- Shared kernel / cross-cutting concern changes
- CI/CD pipeline changes
Signal: Many dependents, hard to roll back, security implications

HIGH (15-30 min review):
- New module or subsystem
- Public API surface changes
- Message consumer changes (affects async workflows)
- EF Core configuration changes (query behavior, indexes)
Signal: Multiple consumers, behavioral changes, data integrity

MEDIUM (5-15 min review):
- New feature within existing module (follows patterns)
- Test additions or modifications
- New endpoint following established conventions
Signal: Localized impact, follows existing patterns

LOW (glance or auto-approve):
- Documentation updates
- Formatting / import ordering
- Adding logging statements
- Renaming internal variables
Signal: No behavioral change, cosmetic only
```

### Batch Review Checklist

The 10 highest-value checks for any .NET code review:

```
THE TOP 10 (in priority order):

1. SQL INJECTION — Any raw SQL or string-interpolated queries?
   → EF parameterizes by default, but check for FromSqlRaw with user input

2. AUTH GAPS — Every endpoint has explicit auth? No open endpoints by accident?
   → Check for missing [Authorize] on new controllers/endpoint groups

3. N+1 QUERIES — Loading collections without Include/join?
   → Check any LINQ that accesses navigation properties after the query

4. CANCELLATION PROPAGATION — CancellationToken passed through the full chain?
   → From endpoint → handler → service → EF query. Breaking the chain = uninterruptible

5. SECRET EXPOSURE — Any connection strings, API keys, or tokens in code?
   → Check for hardcoded strings that look like credentials

6. EXCEPTION SWALLOWING — Catch blocks that silently discard errors?
   → Empty catch, catch with only a log, catch(Exception) without rethrow

7. ASYNC DEADLOCKS — .Result, .Wait(), .GetAwaiter().GetResult()?
   → Any synchronous blocking on async code = potential deadlock

8. ENTITY LEAKS — Domain entities returned directly from API endpoints?
   → Entities should map to response DTOs/records at the API boundary

9. MISSING VALIDATION — User input reaching business logic unchecked?
   → Every command/request DTO should have a corresponding validator

10. RESOURCE LEAKS — Disposable objects not in using/await using blocks?
    → HttpClient, DbContext, FileStream, etc. created without disposal
```

### Review with MCP Tools

Leverage Roslyn MCP tools for efficient, targeted review:

```
REVIEW WORKFLOW WITH MCP:

1. get_project_graph → Understand what changed in the solution structure
2. get_diagnostics → Catch compiler warnings (CS warnings often signal real issues)
3. detect_antipatterns → Automated anti-pattern scan
4. find_dead_code → Check if the change left any dead code behind
5. detect_circular_dependencies → Verify no new cycles introduced
6. get_test_coverage_map → Verify changed code has test coverage

This MCP-first approach reviews the system-level impact in ~300 tokens
before you even read a single file.
```

## Anti-patterns

### Reviewing Every Trivial Change

```
// BAD — spending 20 minutes reviewing a rename
PR: Rename `OrderSvc` → `OrderService` across 8 files
Reviewer spends 20 minutes verifying each rename is correct.
*This is what Find & Replace + tests are for*

// GOOD — trust the tooling for mechanical changes
PR: Rename `OrderSvc` → `OrderService` across 8 files
Reviewer: "Tests pass? Build passes? Auto-approve."
*Spend that 20 minutes reviewing the authentication change instead*
```

### Skipping Reviews Because "It's Just a Small Change"

```
// BAD — one-line change to auth middleware, no review
"It's just adding a header, no need to review"
*That header now leaks internal server info to every response*

// GOOD — blast radius determines review, not line count
One-line change to middleware → CRITICAL blast radius → thorough review
"This adds a header to every HTTP response. Is it safe? Does it leak info?
Does it affect caching? Does it break CORS?"
```

### Style Over Substance

```
// BAD — reviewer focuses on naming while missing the N+1
"Line 15: rename 'x' to 'order' for clarity"
"Line 23: add a blank line between methods"
*Meanwhile, line 28 has an N+1 query that will hammer the database*

// GOOD — substance first, style if time permits
"Line 28: This will produce an N+1 — add .Include(o => o.Items)"
"Line 42: Missing CancellationToken in the EF query"
*Only after critical issues are addressed: "Line 15: consider renaming 'x'"*
```

### Manual Review of Automatable Checks

```
// BAD — manually checking formatting in every PR
Reviewer spends 5 minutes checking import ordering, bracket placement,
and whitespace consistency.
*Formatters and analyzers do this in milliseconds*

// GOOD — automate the trivial, review the meaningful
Pre-commit hook: dotnet format --verify-no-changes
CI step: dotnet format --verify-no-changes (catches anything the hook missed)
Reviewer: focuses on logic, security, performance, and design
```

## Decision Guide

| Scenario | Review Depth | Focus Area |
|----------|-------------|------------|
| New endpoint following existing pattern | Medium (5-15 min) | Auth, validation, response mapping |
| Authentication/authorization change | Critical (30+ min) | Every code path, edge cases, token handling |
| Database migration | Critical (30+ min) | Data loss risk, rollback strategy, index impact |
| New module or subsystem | High (15-30 min) | Architecture, boundaries, integration points |
| Bug fix with clear root cause | Medium (5-15 min) | Root cause correctness, regression test |
| Rename/formatting/docs PR | Low (glance) | Tests pass, build passes, auto-approve |
| EF Core query changes | High (15-30 min) | N+1, tracking, cancellation, SQL generated |
| Middleware or filter changes | Critical (30+ min) | Blast radius — affects every request |
| Test additions | Low-Medium | Test quality, are they testing behavior not implementation |
| CI/CD pipeline changes | High (15-30 min) | Security (secrets), deployment safety, rollback |
