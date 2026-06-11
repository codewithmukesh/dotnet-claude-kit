---
name: verify
description: >
  Run a comprehensive 7-phase verification pipeline for .NET projects. Covers
  build, analyzers, antipattern detection, tests, security, formatting, and diff
  review. Each phase produces PASS/FAIL with actionable output. Short-circuits
  on critical failures. Use when: "verify", "check everything", "is this ready",
  "pre-PR check", "run all checks", or after completing a feature or refactor.
---

# /verify -- 7-Phase Verification Pipeline

## What

Runs a sequential, 7-phase verification pipeline that catches issues at every level --
from compiler errors to subtle antipatterns to formatting drift. Each phase produces
a clear PASS or FAIL result. Critical failures (phases 1-2) short-circuit the pipeline
because later phases cannot produce meaningful results on broken code.

The pipeline is designed to answer one question: **"Is this code ready for review?"**

### The 7 Phases

| Phase | Tool | What It Catches |
|-------|------|-----------------|
| 1. Build | `dotnet build` | Compilation errors, missing references, type mismatches |
| 2. Diagnostics | `get_diagnostics` (MCP) | Analyzer warnings, nullable reference issues, code quality |
| 3. Antipatterns | `detect_antipatterns` (MCP) | async void, sync-over-async, `new HttpClient()`, `DateTime.Now`, broad catch, missing CancellationToken |
| 4. Tests | `dotnet test` | Failing unit and integration tests, regressions |
| 5. Security | Security scan | Hardcoded secrets, SQL injection patterns, missing auth attributes |
| 6. Format | `dotnet format --verify-no-changes` | Code style drift, formatting inconsistencies |
| 7. Diff Review | `git diff` analysis | Accidental changes, debug leftovers, TODO/HACK markers |

## When

- After completing a feature or bug fix
- Before creating a pull request
- After a major refactor
- After merging upstream changes
- When the user says "verify", "check this", "is this ready", "run checks"
- As the final step before marking a task complete

**Quick check alternative:** For small changes where the full pipeline is overkill,
run just phases 1 and 4 (build + test).

## How

### Phase 1: Build (Critical -- short-circuits on failure)

```bash
dotnet build --no-restore --verbosity quiet
```

- If the build fails, STOP. Report errors and fix them before continuing.
- No point running analyzers or tests on code that does not compile.
- Output: PASS (0 errors) or FAIL (with error list)

### Phase 2: Diagnostics (Critical -- short-circuits on failure)

Use the Roslyn MCP `get_diagnostics` tool:
- Scope to the modified project(s) when possible, full solution for cross-cutting changes
- Filter for warnings and errors
- Flag any new warnings introduced by the current changes

Output: PASS (0 new warnings/errors) or FAIL (with diagnostic list, grouped by severity)

### Phase 3: Antipattern Detection

Use the Roslyn MCP `detect_antipatterns` tool:
- Scan modified files or the full project depending on change scope
- Flag all detected antipatterns with severity and file location

Common antipatterns caught:
- `async void` methods (except event handlers)
- Sync-over-async (`Task.Result`, `.GetAwaiter().GetResult()`)
- `new HttpClient()` instead of `IHttpClientFactory`
- `DateTime.Now`/`DateTime.UtcNow` instead of `TimeProvider`
- Broad `catch (Exception)` without specific handling
- String interpolation in logging (`logger.LogInformation($"...")`)
- Missing `CancellationToken` propagation
- EF Core queries without `AsNoTracking` for read-only scenarios

Output: PASS (0 antipatterns) or WARN (with findings) or FAIL (critical antipatterns)

### Phase 4: Tests

```bash
dotnet test --no-build --verbosity quiet
```

- Run the full test suite, or scoped to affected test projects for large solutions
- Report test count, pass count, fail count, and skip count
- Any failing test is a FAIL -- no exceptions

Output: PASS (all tests green) or FAIL (with failing test names and error messages)

### Phase 5: Security Scan

Review the changes for common security issues:
- Hardcoded connection strings, API keys, or secrets
- SQL injection vulnerabilities (raw SQL without parameterization)
- Missing `[Authorize]` attributes on endpoints that should be protected
- CORS misconfiguration (overly permissive origins)
- Missing input validation on public endpoints
- Disabled HTTPS or certificate validation

Output: PASS (no findings) or WARN/FAIL (with findings by severity)

### Phase 6: Format Check

```bash
dotnet format --verify-no-changes --verbosity quiet
```

- Verifies code matches the project's formatting rules
- Does NOT auto-fix -- reports what needs fixing
- If no `.editorconfig` exists, note it as a recommendation

Output: PASS (no formatting issues) or WARN (with file list)

### Phase 7: Diff Review

Analyze `git diff` (staged and unstaged) for:
- Accidental file changes (unrelated modifications)
- Debug leftovers (`Console.WriteLine`, `debugger` statements, `#if DEBUG` blocks in production code)
- TODO/HACK/FIXME markers that should be resolved before merge
- Large files that might need review (> 500 lines changed)
- Sensitive files modified (`.env`, `appsettings.json` secrets section)

Output: PASS (clean diff) or WARN (with findings)

### Final Summary

After all phases complete, output a summary table:

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

Verdicts:
- **READY FOR REVIEW** -- All phases PASS or only non-blocking WARNs
- **NEEDS FIXES** -- Any phase FAIL, with specific remediation steps

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

Recommendation: Replace DateTime.Now with TimeProvider on line 42
before merging. Not blocking, but it will fail the antipattern check in CI.
```

## Related

- `/build-fix` -- Auto-fix build errors when Phase 1 fails
- `/de-sloppify` -- Deep cleanup when Phases 3/6 have many findings
- `/security-scan` -- Focused deep security review (beyond Phase 5 basics)
