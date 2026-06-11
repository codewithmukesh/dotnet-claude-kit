---
name: health-check
description: >
  Multi-dimensional health assessment for .NET projects with letter grades (A-F)
  using Roslyn MCP tools. Evaluates 8 dimensions: build health, code quality,
  architecture, test coverage, dead code, API surface, security posture, and
  documentation. Produces a structured report card with actionable recommendations.
  Load this skill when: "health check", "how healthy is this", "project health",
  "code quality report", "grade this project", "assess codebase", "quality audit",
  "technical assessment", "codebase review", "report card".
---

# Health Check

## Core Principles

1. **Data-driven assessment** — Use MCP tools for every dimension. `get_diagnostics` for build health, `detect_antipatterns` for code quality, `detect_circular_dependencies` for architecture, `get_test_coverage_map` for testing, `find_dead_code` for dead code. Gut feeling is not a grade.

2. **Letter grades with justification** — Every dimension gets A (90+), B (80+), C (70+), D (60+), or F (<60). Every grade includes the specific data points that produced it. "B in Code Quality" means nothing. "B in Code Quality: 3 anti-patterns in 2,400 lines (1.25 per 1K)" is actionable.

3. **Actionable recommendations** — Every grade below A comes with specific, prioritized fix suggestions. "Improve test coverage" is not actionable. "Add test classes for OrderService, PaymentProcessor, and ShippingCalculator (3 production types without tests)" is.

4. **Comparative baselines** — Grade against .NET best practices, not perfection. Zero warnings is aspirational. Fewer than 1 warning per 1K lines of code is excellent. Context matters.

5. **Non-judgmental tone** — Health checks are diagnostic, not punitive. A project with a C grade has a clear improvement path. Frame findings as opportunities, not failures.

## Patterns

### 8-Dimension Health Assessment

Run all dimensions. Each uses specific MCP tools and produces a letter grade. For deep code-quality dimensions, delegate to the `code-reviewer` agent with the `code-review` skill.

**Dimension 1: Build Health**
```
Tool: dotnet build --no-restore
Metric: Error count, warning count
```

| Grade | Criteria |
|-------|----------|
| A | 0 errors, 0 warnings |
| B | 0 errors, 1-5 warnings |
| C | 0 errors, 6-15 warnings |
| D | 0 errors, 16-30 warnings |
| F | Any errors, or 30+ warnings |

**Dimension 2: Code Quality**
```
Tool: MCP detect_antipatterns(projectFilter: each project)
Metric: Anti-pattern count per 1K lines of code
```

| Grade | Criteria |
|-------|----------|
| A | 0 anti-patterns |
| B | < 0.5 per 1K lines |
| C | 0.5 - 1.5 per 1K lines |
| D | 1.5 - 3.0 per 1K lines |
| F | > 3.0 per 1K lines |

Common anti-patterns detected: async void, sync-over-async, `new HttpClient()`, `DateTime.Now`,
broad catch blocks, string interpolation in logging, missing CancellationToken.

**Dimension 3: Architecture**
```
Tool: MCP get_project_graph — check dependency direction
Tool: MCP detect_circular_dependencies(scope: "projects") — find cycles
Tool: MCP detect_circular_dependencies(scope: "types", projectFilter: each) — type-level cycles
```

| Grade | Criteria |
|-------|----------|
| A | Correct dependency direction, 0 circular deps (project or type level) |
| B | Correct dependency direction, 1-2 type-level cycles (no project cycles) |
| C | 1-2 minor direction issues, or 3-5 type-level cycles |
| D | Project-level circular dependency, or significant layer violations |
| F | Multiple project-level cycles, no discernible architecture |

**Dimension 4: Test Coverage**
```
Tool: MCP get_test_coverage_map(projectFilter: each production project)
Metric: Percentage of production types with corresponding test classes
```

| Grade | Criteria |
|-------|----------|
| A | 90%+ types have test classes |
| B | 75-89% types have test classes |
| C | 50-74% types have test classes |
| D | 25-49% types have test classes |
| F | < 25% types have test classes |

Note: This is structural coverage (test class exists), not runtime line coverage.
A test class existing does not guarantee thorough testing, but its absence guarantees none.

**Dimension 5: Dead Code**
```
Tool: MCP find_dead_code(scope: "solution", kind: "all", maxResults: 50)
Metric: Count of unused types, methods, and properties
```

| Grade | Criteria |
|-------|----------|
| A | 0-2 dead symbols |
| B | 3-8 dead symbols |
| C | 9-15 dead symbols |
| D | 16-25 dead symbols |
| F | 25+ dead symbols |

Note: Some false positives are expected (reflection, DI conventions). Verify before penalizing.

**Dimension 6: API Surface**
```
Tool: MCP get_public_api(typeName: each public type) — review public API design
Tool: MCP find_references(symbolName: public members) — check for overexposed APIs
```

| Grade | Criteria |
|-------|----------|
| A | Minimal public surface, proper return types, consistent naming |
| B | Mostly clean, 1-2 overexposed types |
| C | Several types expose internal details, inconsistent return types |
| D | Public APIs leak implementation, mixed return type patterns |
| F | No API design consideration, everything is public |

Check for:
- Services that should be `internal` but are `public`
- Methods returning `Task` instead of `Task<Result<T>>` for operations that can fail
- Inconsistent return types across similar endpoints (some `TypedResults`, some `IResult`)
- Public setters on types that should be immutable

**Dimension 7: Security Posture**
```
Tool: dotnet list package --vulnerable --include-transitive
Tool: MCP detect_antipatterns — filter for security-related patterns
Scan: Hardcoded secrets, connection strings in code, missing auth attributes
```

| Grade | Criteria |
|-------|----------|
| A | 0 vulnerable packages, no hardcoded secrets, auth on all endpoints |
| B | 0 critical/high vulns, 1-2 low/medium vulns, clean auth |
| C | 1-2 medium vulns, or minor auth gaps |
| D | High-severity vuln, or missing auth on sensitive endpoints |
| F | Critical vuln, hardcoded secrets, or systemic auth gaps |

**Dimension 8: Documentation**
```
Scan: XML docs on public API types and methods
Check: README exists, is current, covers setup and architecture
```

| Grade | Criteria |
|-------|----------|
| A | 90%+ public APIs have XML docs, README is comprehensive |
| B | 70-89% XML doc coverage, README covers basics |
| C | 50-69% XML doc coverage, README exists but is sparse |
| D | < 50% XML doc coverage, minimal README |
| F | No XML docs, no README or severely outdated |

### Report Card Format

```markdown
## Project Health Report

**Project:** MyApp | **Date:** 2026-03-04 | **Assessed by:** Claude (MCP-assisted)

### Grades

| Dimension | Grade | Score | Key Finding |
|-----------|-------|-------|-------------|
| Build Health | A | 95 | 0 errors, 2 pre-existing warnings |
| Code Quality | B | 82 | 3 anti-patterns in 4.2K lines |
| Architecture | A | 92 | Clean dependency direction, 0 circular deps |
| Test Coverage | C | 68 | 34/50 production types have test classes |
| Dead Code | B | 85 | 5 unused methods identified |
| API Surface | B | 80 | 2 overexposed service types |
| Security | A | 94 | 0 vulnerable packages, auth coverage complete |
| Documentation | D | 55 | 12/30 public APIs have XML docs |

### Overall GPA: 3.0 (B-)

### Priority Recommendations

1. **Test Coverage (C -> B):** Add test classes for these 16 untested types:
   - `OrderService`, `PaymentProcessor`, `ShippingCalculator` (critical path)
   - `EmailNotifier`, `InventoryChecker`, ... (supporting services)
   Estimated effort: 2-3 days

2. **Documentation (D -> C):** Add XML docs to public API types:
   - Start with the 8 controller/endpoint classes (user-facing APIs)
   - Then cover the 10 public service interfaces
   Estimated effort: 1 day

3. **Code Quality (B -> A):** Fix 3 anti-patterns:
   - `OrderService.cs:47` — Replace `DateTime.Now` with `TimeProvider.GetUtcNow()`
   - `PaymentClient.cs:23` — Replace `new HttpClient()` with `IHttpClientFactory`
   - `NotificationHandler.cs:12` — Replace `async void` with `async Task`
   Estimated effort: 1 hour
```

### GPA Calculation

Convert letter grades to points: A=4.0, B=3.0, C=2.0, D=1.0, F=0.0
GPA = average of all 8 dimension scores.

| GPA Range | Overall Assessment |
|-----------|--------------------|
| 3.5 - 4.0 | Excellent — production-ready, well-maintained |
| 3.0 - 3.4 | Good — solid foundation, minor improvements needed |
| 2.5 - 2.9 | Fair — functional but accumulating tech debt |
| 2.0 - 2.4 | Needs Work — significant improvements required |
| < 2.0 | Critical — major structural issues to address |

### Quick Health Check

For a rapid assessment, run dimensions 1-4 only:

```
QUICK HEALTH (4 dimensions):
1. Build Health — dotnet build
2. Code Quality — detect_antipatterns
3. Architecture — get_project_graph + detect_circular_dependencies
4. Test Coverage — get_test_coverage_map

Use when:
- Mid-sprint checkpoint
- Quick status before a demo
- Onboarding to an unfamiliar codebase
- After a major merge
```

### Trend Tracking

If a previous health report exists, compare grades:

```markdown
### Trend

| Dimension | Previous | Current | Change |
|-----------|----------|---------|--------|
| Build Health | B | A | Improved — fixed 4 warnings |
| Code Quality | C | B | Improved — resolved 7 anti-patterns |
| Test Coverage | C | C | No change — still 68% |
| Dead Code | B | B | No change |
```

Track trends to show progress over time. Improving grades validate cleanup efforts.

## Anti-patterns

### Grading Without MCP Tools

```
# BAD — gut-feeling assessment
"The code looks pretty clean, I'd give it a B overall."
# No data. No specific findings. No actionable recommendations.

# GOOD — MCP-driven assessment with data
MCP: detect_antipatterns → 3 findings
MCP: get_diagnostics → 2 warnings
MCP: get_test_coverage_map → 68% coverage
"Code Quality: B (3 anti-patterns in 4.2K lines = 0.71 per 1K).
 Specific anti-patterns: DateTime.Now in OrderService.cs:47, ..."
```

### Only Checking Build Health

```
# BAD — build passes, ship it
"dotnet build succeeded with 0 errors. The project is healthy!"
# Misses: 12 anti-patterns, 3 circular dependencies, 30% test coverage, 2 CVEs

# GOOD — all 8 dimensions for a complete picture
Build passes, but Architecture is D (circular deps), Test Coverage is F (15%),
and Security is D (high-severity CVE). Overall GPA: 2.1 — Needs Work.
```

### Inflated Grades

```
# BAD — grading on a curve to make the project look good
15 warnings → "That's pretty good for a project this size" → Grade: B
# Absolute standards exist for a reason

# GOOD — consistent grading against defined thresholds
15 warnings → Grade C (6-15 warnings bracket)
"15 warnings puts this in the C range. Here are the 5 highest-priority
 warnings to fix to reach B (under 6 warnings)."
```

### Recommendations Without Specifics

```
# BAD — vague improvement suggestions
"Improve test coverage."
"Fix code quality issues."
"Address security concerns."

# GOOD — specific, prioritized, estimated
"Add test classes for OrderService, PaymentProcessor, ShippingCalculator.
 These are on the critical path and have 0 test coverage.
 Start with OrderService — it has the most complex logic.
 Estimated effort: 4 hours for all three."
```

## Decision Guide

| Scenario | Assessment Type | Dimensions |
|----------|----------------|------------|
| New project onboarding | Full Health Check | All 8 |
| Mid-sprint checkpoint | Quick Health | 1-4 |
| Pre-release quality gate | Full Health Check | All 8 |
| After major refactor | Targeted | 1 (Build), 3 (Architecture), 4 (Tests) |
| Post-dependency update | Targeted | 1 (Build), 7 (Security) |
| Tech debt prioritization | Full Health Check | All 8, focus on lowest grades |
| Monthly maintenance review | Full Health Check | All 8 with trend comparison |
| Before hiring/onboarding | Full Health Check | All 8 — sets baseline for new team member |
| After cleanup sprint | Targeted | Re-grade dimensions that were cleaned up |
| Executive summary needed | Full Health Check | All 8 with GPA summary |
