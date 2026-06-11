---
name: verification-loop
description: >
  The methodology behind the /verify pipeline: 7 phases (build, diagnostics,
  anti-patterns, tests, security, format, diff review) with PASS/FAIL reporting,
  short-circuit rules, and fix-and-retry loops. Loaded automatically by the
  /verify workflow — to RUN a verification, use /verify. Load this skill
  directly when designing or tuning a verification strategy, choosing which
  phases to run for a given change, or when the user mentions "quality gate",
  "verification strategy", or "which checks should run".
---

# Verification Loop

## Core Principles

1. **Build is the minimum bar** — Never mark a task complete without a green `dotnet build`. If it doesn't compile, nothing else matters. A broken build wastes every subsequent phase's time.

2. **Automated checks before manual review** — Tools catch what humans miss. `get_diagnostics` finds nullability issues, `detect_antipatterns` catches `DateTime.Now` usage, and `dotnet test` proves behavior. Run all of these before eyeballing code.

3. **Short-circuit on critical failures** — If the build fails, don't run tests. If tests fail, don't run formatting checks. Failing fast saves time and keeps the feedback loop tight. Fix the most fundamental issue first.

4. **Structured reporting** — Every phase gets an explicit PASS, FAIL, or WARN status with details. No ambiguity. "It looks fine" is not a verification result. A table with statuses is.

5. **Verification is iterative** — A single pass rarely produces all-green. Fix the first failure, re-run from that phase, repeat until clean. The loop is the point.

## Patterns

### 7-Phase Verification Pipeline

Execute phases in order. Short-circuit on CRITICAL failures (Phase 1 and Phase 4).

**Phase 1: Build (CRITICAL)**
```
dotnet build --no-restore
```
Status: PASS (0 errors) / FAIL (N errors)
Short-circuit: If FAIL, stop all subsequent phases. Fix build errors first.
Note: Capture warning count even on PASS — new warnings are tracked in Phase 2.

**Phase 2: Diagnostics**
```
MCP: get_diagnostics(scope: "file", path: each changed file)
MCP: get_diagnostics(scope: "project", path: changed project)
```
Status: PASS (0 new warnings) / WARN (N new warnings) / FAIL (new errors)
Check: Compare against baseline — only flag NEW warnings introduced by the current changes.
Common findings: CS8600 (null reference), CS8602 (dereference of nullable), CS0219 (unused variable).

**Phase 3: Anti-pattern Scan**
```
MCP: detect_antipatterns(file: each changed file)
```
Status: PASS (0 anti-patterns) / WARN (N anti-patterns found)
Catches: async void, sync-over-async, `new HttpClient()`, `DateTime.Now`, broad catch,
logging string interpolation, missing CancellationToken, EF queries without AsNoTracking.

**Phase 4: Tests (CRITICAL)**
```
dotnet test --no-build
```
Status: PASS (all green) / FAIL (N failures)
Short-circuit: If FAIL, stop remaining phases. Fix failing tests before proceeding.
Note: If no test project exists, status is SKIP with a recommendation to add tests.

**Phase 5: Security Scan**
```
dotnet list package --vulnerable --include-transitive
```
Scan changed files for: hardcoded secrets, connection strings, API keys.
Check: OWASP patterns in new code (SQL injection, XSS, missing auth attributes).
Status: PASS / WARN (medium/low findings) / FAIL (critical/high vulnerabilities)

**Phase 6: Format Compliance**
```
dotnet format --verify-no-changes
```
Status: PASS (no changes needed) / FAIL (formatting violations found)
Fix: Run `dotnet format` and include formatting changes in the commit.

**Phase 7: Diff Review**
```
git diff --stat
git diff
```
Status: PASS (changes match intent) / WARN (unexpected files changed)
Check:
- No accidental files (`.vs/`, `bin/`, `obj/`, `.env`, secrets)
- No unrelated changes mixed in
- Commit scope matches the task description
- No debug code (`Console.WriteLine`, `#if DEBUG` blocks in production paths)

### Structured Report Format

After running all phases, produce a verification report:

```markdown
## Verification Report

| Phase | Status | Details |
|-------|--------|---------|
| 1. Build | PASS | 0 errors, 2 warnings (pre-existing) |
| 2. Diagnostics | WARN | CS8600 in OrderService.cs:47 — possible null reference |
| 3. Anti-patterns | PASS | 0 anti-patterns in changed files |
| 4. Tests | PASS | 42 passed, 0 failed, 0 skipped |
| 5. Security | PASS | No vulnerable packages, no secrets detected |
| 6. Format | PASS | No formatting violations |
| 7. Diff Review | PASS | 3 files changed, all match task scope |

**Overall: PASS (with warnings)**

### Action Items
- [ ] Fix CS8600 in OrderService.cs:47 — add null check or use null-forgiving operator with justification
```

### Fix-and-Retry Loop

When a phase fails, follow this exact sequence:

```
1. IDENTIFY — Which phase failed? What's the specific error?
2. FIX — Make the minimal change to resolve the issue
3. RE-RUN — Start from the failed phase (not from Phase 1, unless the fix changed code)
   Exception: If the fix involved code changes, re-run from Phase 1 (build)
4. REPEAT — Until all phases pass or you've identified an issue that needs user input

EXAMPLE:
Phase 4 fails: OrderServiceTests.CreateOrder_ReturnsCreated fails with 404
  → Fix: Route was "/orders" but test expected "/api/orders"
  → Re-run from Phase 1 (code changed) → Build PASS → Phase 4 PASS
  → Continue Phase 5, 6, 7
```

### Pre-PR Verification

Before creating any pull request, run the full 7-phase pipeline. This is non-negotiable.

```
PRE-PR CHECKLIST:
1. All 7 phases PASS (warnings acceptable only if pre-existing)
2. No new warnings introduced
3. All new code has corresponding tests
4. Diff review confirms changes match the PR description
5. No TODO comments left unresolved (or tracked in issues)

Only after all 7 phases pass:
→ Create the PR with the verification report as part of the PR description
```

### Quick Verification

For minor changes (typo fix, config change, documentation), run a subset:

```
QUICK VERIFICATION (3 phases):
Phase 1: dotnet build
Phase 4: dotnet test
Phase 6: dotnet format --verify-no-changes

Use when:
- Single-line bug fix with existing test coverage
- Configuration change
- Documentation-only change
- Dependency version bump (also add Phase 5 for security)
```

### Post-Refactor Verification

After structural refactoring, focus on correctness:

```
POST-REFACTOR VERIFICATION (4 phases):
Phase 1: dotnet build — refactors often break compilation
Phase 2: get_diagnostics — refactors often introduce new warnings
Phase 3: detect_antipatterns — refactors can accidentally introduce anti-patterns
Phase 4: dotnet test — the ultimate refactor validation

Skip Phase 5-7 unless the refactor touches security-sensitive code or public APIs.
```

## Anti-patterns

### Skipping Verification Entirely

```
# BAD — "it compiles in my head"
"I've made the changes. The code looks correct. Let me create the PR."
# No build run, no tests, no diagnostics — hope-driven development

# GOOD — verify before declaring done
"Let me run the verification pipeline before we create the PR."
→ Phase 1: Build PASS → Phase 2: Diagnostics PASS → ... → Phase 7: PASS
"All 7 phases passed. Creating the PR now."
```

### Running All Phases When Build Fails

```
# BAD — wasting time on downstream checks
Phase 1: Build FAIL (3 errors)
Phase 2: Running diagnostics anyway...
Phase 3: Running anti-pattern scan...
Phase 4: Running tests... (they'll fail because build failed)

# GOOD — short-circuit and fix
Phase 1: Build FAIL (3 errors)
→ STOP. Fix the 3 build errors first.
→ Re-run from Phase 1.
```

### Ignoring Warnings

```
# BAD — "warnings are just suggestions"
Phase 2: 12 new CS8600 warnings introduced
"These are just warnings, not errors. Moving on."
# Three weeks later: NullReferenceException in production

# GOOD — treat new warnings as failures
Phase 2: 12 new CS8600 warnings introduced
"12 new nullability warnings detected. Fixing before proceeding."
→ Add null checks or non-nullable assertions with justification
→ Re-run Phase 2: 0 new warnings. PASS.
```

### Manual-Only Verification

```
# BAD — reading code instead of running tools
"Let me read through OrderService.cs... looks good to me."
# Missed: DateTime.Now on line 23, async void on line 67, CS8600 on line 91

# GOOD — tools first, then human judgment
→ detect_antipatterns: Found DateTime.Now (line 23), async void (line 67)
→ get_diagnostics: CS8600 on line 91
"Found 3 issues via automated analysis. Fixing all three before manual review."
```

### Cherry-Picking Phases

```
# BAD — only running the phases you think are relevant
"It's just a service change, I'll skip the security scan."
# The service change added a raw SQL query with string concatenation

# GOOD — full pipeline for non-trivial changes
When in doubt, run all 7 phases. The cost of running extra phases is minutes.
The cost of missing a security issue is days of incident response.
```

## Decision Guide

| Scenario | Phases | Notes |
|----------|--------|-------|
| Feature complete | All 7 | Full pipeline, no shortcuts |
| Bug fix (with test) | 1, 2, 4 | Build, diagnostics, tests |
| Bug fix (no test) | 1, 2, 3, 4 | Add a test, then verify |
| Formatting only | 6 | Format check is sufficient |
| Dependency update | 1, 4, 5 | Build, tests, security |
| Pre-PR | All 7 | Non-negotiable full pipeline |
| After refactor | 1, 2, 3, 4 | Focus on correctness |
| Config change | 1, 4 | Build and test |
| New endpoint added | All 7 | Full pipeline including security |
| Test-only changes | 1, 4 | Build and run the new tests |
| Performance optimization | 1, 2, 3, 4 | Correctness first, benchmark separately |
| CI failure investigation | Run the failing phase locally | Reproduce, fix, verify |
