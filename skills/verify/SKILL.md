---
name: verify
description: >
  Run a comprehensive 7-phase verification pipeline for .NET projects: build,
  analyzers, antipattern detection, tests, security, formatting, and diff
  review. Each phase produces PASS/FAIL with actionable output and the pipeline
  short-circuits on critical failures. Also the authority on verification
  strategy: which phases to run for a given change, quality gates, and
  fix-and-retry loops. Use when: "verify", "check everything", "is this ready",
  "pre-PR check", "run all checks", "quality gate", "verification strategy",
  "which checks should run", or after completing a feature or refactor.
---

# /verify -- 7-Phase Verification Pipeline

## What

Runs a sequential, 7-phase verification pipeline that catches issues at every level --
from compiler errors to subtle antipatterns to formatting drift. Each phase produces
an explicit PASS, WARN, or FAIL with details. "It looks fine" is not a verification
result; a table of statuses is. Critical failures (Phase 1 build, Phase 4 tests)
short-circuit the pipeline because later phases cannot produce meaningful results
on broken code.

The pipeline answers one question: **"Is this code ready for review?"**

| Phase | Tool | What It Catches | Critical |
|-------|------|-----------------|----------|
| 1. Build | `dotnet build` | Compilation errors, missing references | Yes |
| 2. Diagnostics | `get_diagnostics` (MCP) | New analyzer warnings, nullability issues | FAIL on new errors |
| 3. Antipatterns | `detect_antipatterns` (MCP) | async void, sync-over-async, `DateTime.Now`, more | No |
| 4. Tests | `dotnet test` | Failing tests, regressions | Yes |
| 5. Security | `dotnet list package --vulnerable` + scan | Secrets, SQL injection, missing auth, vulnerable packages | FAIL on critical/high |
| 6. Format | `dotnet format --verify-no-changes` | Style drift, formatting inconsistencies | No |
| 7. Diff Review | `git diff` analysis | Accidental changes, debug leftovers, TODOs | No |

## When

- After completing a feature, bug fix, or major refactor
- Before creating a pull request -- non-negotiable, full pipeline
- After merging upstream changes or updating dependencies
- When the user says "verify", "check everything", "is this ready", "run all checks"
- As the final step before marking a task complete

### Which Phases to Run

Full pipeline is the default. For scoped changes, run a subset:

| Scenario | Phases | Notes |
|----------|--------|-------|
| Feature complete / Pre-PR / new endpoint | All 7 | No shortcuts |
| Bug fix | 1, 2, 4 | Add a test first if none covers it |
| After refactor | 1, 2, 3, 4 | Correctness focus; add 5-7 if security-sensitive |
| Dependency update | 1, 4, 5 | Build, tests, vulnerability scan |
| Config or test-only change | 1, 4 | Build and test |
| Formatting only | 6 | Format check is sufficient |

When in doubt, run all 7. Extra phases cost minutes; a missed security issue costs
days of incident response. Never cherry-pick phases because a change "looks safe".

## How

### Phase 1: Build (CRITICAL -- short-circuits)

```bash
dotnet build --no-restore --verbosity quiet
```

- If the build fails, STOP. Report errors and fix before continuing -- nothing
  downstream is meaningful on code that does not compile.
- Capture the warning count even on PASS; new warnings are tracked in Phase 2.
- Output: PASS (0 errors) or FAIL (with error list)

### Phase 2: Diagnostics

Use the Roslyn MCP `get_diagnostics` tool, scoped to changed files/projects
(full solution for cross-cutting changes). Compare against baseline -- flag only
NEW warnings introduced by the current changes. Common findings: CS8600/CS8602
(nullability), CS0219 (unused variable).

Output: PASS (0 new) / WARN (new warnings) / FAIL (new errors). Treat new
warnings as work -- today's CS8600 is next month's production NullReferenceException.

### Phase 3: Antipattern Detection

Use the Roslyn MCP `detect_antipatterns` tool on changed files (full project for
broad changes). Catches: `async void`, sync-over-async (`.Result`,
`.GetAwaiter().GetResult()`), `new HttpClient()`, `DateTime.Now`/`UtcNow` instead
of `TimeProvider`, broad `catch (Exception)`, string interpolation in logging,
missing `CancellationToken`, EF read queries without `AsNoTracking`.

Output: PASS (0 findings) / WARN (findings) / FAIL (critical antipatterns)

### Phase 4: Tests (CRITICAL -- short-circuits)

```bash
dotnet test --no-build --verbosity quiet
```

- Full suite, or scoped to affected test projects for large solutions.
- Any failing test is a FAIL -- no exceptions. Stop and fix before later phases.
- If no test project exists: SKIP with a recommendation to add tests.

Output: PASS (all green) or FAIL (failing test names + error messages)

### Phase 5: Security Scan

```bash
dotnet list package --vulnerable --include-transitive
```

Then review changed files for: hardcoded secrets/connection strings/API keys,
SQL injection (raw SQL without parameterization), missing `[Authorize]` on
endpoints that need it, permissive CORS, missing input validation, disabled
HTTPS or certificate validation.

Output: PASS / WARN (medium/low findings) / FAIL (critical/high vulnerabilities)

### Phase 6: Format Check

```bash
dotnet format --verify-no-changes --verbosity quiet
```

Reports drift without auto-fixing. To resolve, run `dotnet format` and include
the changes in the commit. If no `.editorconfig` exists, note it as a recommendation.

Output: PASS / WARN (with file list)

### Phase 7: Diff Review

Analyze `git diff --stat` and `git diff` (staged + unstaged) for:
- Accidental or unrelated file changes (`.vs/`, `bin/`, `obj/`, `.env`, secrets)
- Debug leftovers (`Console.WriteLine`, `#if DEBUG` in production paths)
- Unresolved TODO/HACK/FIXME markers
- Scope mismatch -- changes must match the task/PR description

Output: PASS (clean, matches intent) / WARN (with findings)

### Fix-and-Retry Loop

A single pass rarely produces all-green. The loop is the point:

1. **IDENTIFY** -- which phase failed, and the specific error
2. **FIX** -- make the minimal change that resolves it
3. **RE-RUN** -- from Phase 1 if the fix changed code; otherwise from the failed phase
4. **REPEAT** -- until all phases pass, or an issue needs user input

### Final Summary

```
## Verification Results

| Phase | Result | Details |
|-------|--------|---------|
| 1. Build | PASS | 0 errors, 0 warnings |
| 2. Diagnostics | PASS | 0 new diagnostics |
| 3. Antipatterns | WARN | 1 missing CancellationToken |
| 4. Tests | PASS | 47 passed, 0 failed |
| 5. Security | PASS | No findings |
| 6. Format | PASS | Clean |
| 7. Diff Review | WARN | 1 TODO marker found |

**Verdict: READY FOR REVIEW** (with 2 non-blocking warnings)
```

Verdicts: **READY FOR REVIEW** (all PASS, or only non-blocking WARNs) or
**NEEDS FIXES** (any FAIL, with specific remediation steps). For pre-PR runs,
include the verification report in the PR description.

## Example

```
User: /verify

Claude: Running 7-phase verification pipeline...

Phase 1: Build ............ PASS (0 errors)
Phase 2: Diagnostics ...... PASS (0 new warnings)
Phase 3: Antipatterns ..... WARN
  - src/Features/Orders/CreateOrder.cs:42 -- DateTime.Now usage, use TimeProvider
Phase 4: Tests ............ PASS (23 passed, 0 failed, 0 skipped)
Phase 5: Security ......... PASS
Phase 6: Format ........... PASS
Phase 7: Diff Review ...... PASS

Verdict: READY FOR REVIEW (1 non-blocking warning)

Recommendation: Replace DateTime.Now with TimeProvider on line 42 before
merging. Not blocking, but it will fail the antipattern check in CI.
```

## Related

- `/build-fix` -- Auto-fix build errors when Phase 1 fails
- `/code-review` -- Multi-dimensional review once verification passes
- `/health-check` -- Whole-project graded assessment (beyond this change set)
