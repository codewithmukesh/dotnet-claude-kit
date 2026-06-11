---
name: code-review
description: >
  MCP-powered multi-dimensional code review for .NET projects. Uses Roslyn
  analysis tools for antipatterns, diagnostics, references, and dependency
  graphs combined with structured manual review. Produces categorized findings
  with severity levels and actionable fix suggestions.
  Use when: "review", "code review", "PR review", "review this", "review my
  code", "check code quality", "review changes".
---

# /code-review -- MCP-Powered Code Review

## What

Performs a comprehensive, multi-dimensional code review that combines Roslyn MCP
tool analysis with structured manual review. Every review follows a consistent
7-step process and produces findings categorized by severity (Critical, Warning,
Suggestion) with actionable remediation guidance.

Review dimensions:
- **Correctness** -- Logic errors, edge cases, null handling, async pitfalls
- **Security** -- Auth gaps, injection risks, secret exposure, CORS issues
- **Performance** -- N+1 queries, missing indexes, unnecessary allocations, missing cancellation
- **Architecture** -- Layer violations, coupling issues, boundary breaches
- **Maintainability** -- Naming, complexity, duplication, test coverage
- **API Design** -- HTTP semantics, error responses, versioning, OpenAPI metadata

## When

- "Review this code", "PR review", "code review"
- Before merging a pull request
- After a major refactor to verify no regressions or design drift
- When onboarding to unfamiliar code and wanting a quality assessment
- Periodic health checks on critical modules

## How

### Step 1: Scope the Review

Determine what is being reviewed:
- **PR review**: Use `git diff main...HEAD` to identify all changed files
- **File review**: Focus on specified files
- **Module review**: Identify all files in the module/feature

Categorize changed files by layer (domain, application, infrastructure, API, tests).

### Step 2: MCP Diagnostics Scan

Run Roslyn MCP tools on the review scope:

```
get_diagnostics(scope: "project", path: "affected-project")
```

- Flag new warnings and errors
- Group by severity and category
- Identify nullable reference warnings -- these often indicate missing null checks

### Step 3: Antipattern Detection

```
detect_antipatterns(projectFilter: "affected-project")
```

- Review each finding against the change context
- Distinguish pre-existing antipatterns from newly introduced ones
- Focus review effort on new antipatterns

### Step 4: Blast Radius Analysis

For each significant change, measure its impact:

```
find_references(symbolName: "ModifiedType")
find_callers(methodName: "ModifiedMethod")
get_dependency_graph(symbolName: "ModifiedMethod", depth: 2)
```

- Identify all consumers of modified APIs
- Check if callers handle new error cases or changed return types
- Flag changes with high blast radius for extra scrutiny

### Step 5: Architecture Compliance

Verify changes respect architectural boundaries:

```
get_type_hierarchy(typeName: "NewOrModifiedType")
find_implementations(interfaceName: "AffectedInterface")
```

- Domain types must not reference infrastructure
- Application layer must not bypass domain logic
- New dependencies should follow existing dependency direction
- Cross-module communication must go through defined contracts

### Step 6: Manual Review

Review aspects that tools cannot catch:
- **Business logic correctness** -- Does the code do what it is supposed to?
- **Edge cases** -- What happens with empty collections, null inputs, concurrent access?
- **Error messages** -- Are they actionable for the end user?
- **Naming clarity** -- Do names communicate intent?
- **Test quality** -- Do tests verify behavior or just exercise code paths?

### Step 7: Produce the Review

Output a structured review with this format:

```markdown
## Code Review: [Scope Description]

### Summary
[1-3 sentence overview of the change and overall assessment]

### Critical (must fix before merge)
- **[Finding title]** -- [file:line]
  [What is wrong, why it matters, how to fix]

### Warnings (should fix, creates tech debt)
- **[Finding title]** -- [file:line]
  [What is wrong, why it matters, how to fix]

### Suggestions (nice to have)
- **[Finding title]** -- [file:line]
  [Recommendation and rationale]

### Architecture Compliance
[PASS/WARN with notes on boundary violations]

### Test Coverage
[Assessment of test coverage for the changes]

### What's Good
- [Positive observations -- always include this section]
```

## Example

```
User: /code-review the changes in this PR

Claude: Scoping review to 7 changed files across 3 projects...

## Code Review: Order Processing Feature

### Summary
Adds CreateOrder and GetOrder endpoints with EF Core persistence.
Well-structured VSA feature with good test coverage. Two issues
need attention before merge.

### Critical
- **Missing CancellationToken propagation** -- CreateOrder.cs:38
  The handler calls DbContext.SaveChangesAsync() without passing
  the CancellationToken. If the client disconnects, the server
  continues processing. Pass `ct` from the handler parameter.

### Warnings
- **N+1 query in GetOrder** -- GetOrder.cs:25
  Loading Order without .Include(o => o.Items) causes a lazy-load
  per item during serialization. Add eager loading or use a projection.

### Suggestions
- **Consider sealed class** -- CreateOrderHandler.cs:10
  Handler classes are not inherited. Mark as sealed for minor
  performance benefit and clearer intent.

### Architecture Compliance
PASS -- All changes within Features/Orders/, no layer violations.

### Test Coverage
Two integration tests cover the happy path. Consider adding tests
for validation failures and not-found scenarios.

### What's Good
- Clean separation of command and query features
- FluentValidation rules cover all edge cases
- Response DTOs use records with good property names
```

## Related

- `/verify` -- Run automated verification pipeline (complements manual review)
- `/health-check` -- Broader project health assessment beyond a single PR
