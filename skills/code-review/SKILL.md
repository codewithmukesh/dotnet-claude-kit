---
name: code-review
description: >
  MCP-powered multi-dimensional code review for .NET projects. Uses Roslyn
  analysis tools for antipatterns, diagnostics, references, and dependency
  graphs combined with structured manual review. Prioritizes effort with
  blast-radius scoring — data access, security, concurrency, and integration
  boundaries before style — and produces severity-categorized findings with
  actionable fixes. Use when: "review", "code review", "PR review", "review
  this", "review my code", "check code quality", "review changes", "what
  should I review", "review priorities", "blast radius", "critical path".
---

# /code-review — MCP-Powered Code Review

## What

Performs a multi-dimensional code review combining Roslyn MCP analysis with
structured manual review. Effort follows the 80/20 rule: the 20% of code that
causes 80% of incidents (data access, security, concurrency, integration
boundaries) gets thorough review; style and formatting are left to tooling.

Review dimensions: **Correctness** (logic, edge cases, null handling, async
pitfalls), **Security** (auth gaps, injection, secrets, CORS), **Performance**
(N+1, allocations, missing cancellation), **Architecture compliance** (layer
violations, boundary breaches), **Test coverage** (behavior tests for changed
types).

## When

- "Review this", "code review", "PR review", before merging a pull request
- After a major refactor to verify no regressions or design drift
- "What should I review?" — deciding where review effort goes on a large change
- Onboarding to unfamiliar code and wanting a quality assessment

## How

### Step 1: Scope and Score Blast Radius

Identify changed files (`git diff main...HEAD`, specified files, or module).
Score each change to set review depth — blast radius determines depth, not
line count. A one-line middleware change outranks a 300-line rename.

| Blast Radius | Examples | Depth |
|---|---|---|
| Critical | Middleware, auth, DB migrations, shared kernel, CI/CD | Thorough — every code path |
| High | Public API changes, message consumers, EF configuration, new module | Focused — consumers + behavior |
| Medium | New feature following existing patterns, bug fix, new endpoint | Standard — checklist pass |
| Low | Docs, formatting, renames, logging statements | Glance — build + tests pass |

### Step 2: MCP Analysis (before reading any file)

```
detect_antipatterns(projectFilter: "affected-project")   → async void, DateTime.Now, new HttpClient(), broad catch
get_diagnostics(scope: "project", path: "affected-project") → new warnings, nullability issues
```

Distinguish newly introduced findings from pre-existing ones — focus on new.

### Step 3: Blast Radius Verification

For each modified public API:

```
find_references(symbolName: "ModifiedType")              → count consumers; high count = high risk
get_dependency_graph(symbolName: "ModifiedMethod", depth: 2) → ripple effects
```

Check whether callers handle changed return types and new error cases.

### Step 4: Architecture Compliance

Verify dependency direction (Domain → nothing; Infrastructure → Application →
Domain) via `get_project_graph` and `detect_circular_dependencies`. Per
architecture: VSA features don't cross-reference; Clean Architecture domain has
zero project references; Modular Monolith modules communicate only via
integration events — `find_references` on a module's DbContext should resolve
only inside that module.

### Step 5: Manual Review — Priority Order

Review what tools can't catch, highest-risk areas first:

| Priority | Area | Check |
|---|---|---|
| 1 | Data access | N+1 (missing `Include`/projection), raw SQL with user input, missing `CancellationToken` |
| 2 | Security | Every endpoint has explicit `[Authorize]`/`[AllowAnonymous]`, input validated, no secrets in code, no PII in logs |
| 3 | Concurrency | Token propagated end-to-end, no `.Result`/`.Wait()`, thread-safe shared state |
| 4 | Integration | Retry/timeout on external calls, consumer idempotency, no swallowed exceptions |
| 5 | Correctness | Business logic, edge cases (empty/null/concurrent), entities mapped to DTOs at the boundary |
| 6 | Tests | Behavior tested (not implementation); happy path + main error case covered |
| — | Style/naming | Mention only after the above; formatters and analyzers own this |

### Step 6: Produce the Review

Every finding states what's wrong, why it matters, and how to fix it. Never
bury a security bug under naming nits.

```markdown
## Code Review: [Scope]

### Summary
[1-3 sentences: scope, risk level, recommendation]

### Critical (must fix before merge)
- **[Title]** — [file:line] [What's wrong. Why it matters. How to fix.]

### Warnings (should fix, creates tech debt)
- **[Title]** — [file:line] [...]

### Suggestions (nice to have)
- **[Title]** — [file:line] [...]

### Architecture Compliance
[PASS/WARN with boundary-violation notes]

### Test Coverage
[Which changed types have tests; specific scenarios to add]

### What's Good
- [Always include — reinforce good patterns]
```

**Quick review** (1-2 files, low blast radius): run `detect_antipatterns` +
`get_diagnostics`, read for correctness, output Summary + Issues + What's Good.

## Example

```
User: /code-review the changes in this PR

Claude: 7 changed files across 3 projects. CreateOrder touches data access
and a public endpoint — High blast radius. Running MCP analysis...

## Code Review: Order Processing Feature

### Summary
Adds CreateOrder/GetOrder endpoints with EF Core persistence. Well-structured
VSA feature. Two issues need attention before merge.

### Critical (must fix before merge)
- **Missing CancellationToken propagation** — CreateOrder.cs:38
  SaveChangesAsync() called without the token. Client disconnects keep
  burning server resources. Pass `ct` from the handler parameter.

### Warnings (should fix, creates tech debt)
- **N+1 query in GetOrder** — GetOrder.cs:25
  Order loaded without `.Include(o => o.Items)`; one lazy load per item
  during serialization. Eager-load or use a projection.

### Suggestions (nice to have)
- **Seal the handler** — CreateOrderHandler.cs:10
  Not designed for inheritance; `sealed` enables devirtualization.

### Architecture Compliance
PASS — all changes within Features/Orders/, no layer violations.

### Test Coverage
Happy path covered. Add tests for validation failure and not-found.

### What's Good
- Clean command/query separation; FluentValidation covers edge cases
- Response DTOs are records, no entity leaks
```

## Related

- `/de-sloppify` — Cleanup pass for the style/formatting issues review skips
- `/verify` — Automated verification pipeline (complements manual review)
- `/health-check` — Broader project health assessment beyond a single PR
