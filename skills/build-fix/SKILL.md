---
name: build-fix
description: >
  Autonomous iteration loops for .NET: drive a broken build or failing test
  suite to green with bounded iterations, progress detection, and fail-safe
  guards that prevent infinite retries and wasted tokens. The build-fix loop
  (dotnet build, parse, categorize, fix, rebuild) is the primary flow; the
  test-fix loop is a first-class variant. Invoke when the build is broken,
  after a major refactor or dependency update, or when the user says "fix the
  build", "build is broken", "make it compile", "make the tests pass", "fix
  failing tests", "keep going until it works", "autonomous", "loop",
  "auto-fix", or "keep fixing".
---

# /build-fix

## What

The kit's autonomous iteration-loop skill. It drives a broken `dotnet build`
(or failing `dotnet test`) to green by looping: run, parse failures,
categorize by root cause, apply targeted fixes, re-run. It repeats until
green or a guard fires — the same way an experienced developer works through
a wall of red, but with hard limits that stop it from thrashing.

This is not a single-pass fix. It handles cascading errors where fixing one
issue reveals the next.

## When

- The build is broken and there are multiple compiler errors
- Tests are failing after code changes or a build-fix pass
- After a major refactor that touched type names, namespaces, or signatures
- After updating NuGet packages (especially major version bumps)
- After merging a branch with conflicts resolved but not compiled
- After scaffolding or code generation that needs manual adjustments
- User says: "fix the build", "make it compile", "make the tests pass",
  "keep going until it works", "keep fixing"

## How

### Loop Discipline (applies to every loop)

1. **Bounded iteration, always** — Default max 5 iterations, hard cap 10
   (user "keep going" extends by 3, never past 10). If 5 iterations cannot
   solve it, the problem needs human judgment, not a 6th identical attempt.
2. **Progress or exit** — Each iteration must reduce the error/failure count.
   Same errors after a fix attempt = STUCK: stop, report, and re-plan with a
   different approach. Never retry the same fix that already failed.
3. **Categorize before fixing** — Group errors by root cause and fix the
   highest-leverage first (one missing `using` can erase a dozen downstream
   errors).
4. **Transparency per iteration** — Report what changed and why:
   `Iteration 3/5: fixed CS0246 by adding using System.Text.Json, 2 remain`.
   Never modify files silently.
5. **Atomicity** — Each iteration leaves the codebase no worse than before.
   If iteration 3 fails, the code stays in iteration 2's state.

### Primary Flow: Build-Fix Loop (max 5 iterations)

1. **Build** — Run `dotnet build`, capture full error output
2. **Parse** — Extract every `error CS####` with file, line, and message
3. **Categorize** — Group by root cause:

   | Category | Codes | Fix strategy |
   |---|---|---|
   | Missing using/reference | CS0246, CS0234 | Add using, package, or project ref |
   | Type mismatch | CS0029, CS1503 | Check expected type, cast or convert |
   | API change | CS0117, CS7036 | Check new signature, update call sites |
   | Nullability | CS8600–CS8604 | Add null check, `?.` or `??` |
   | Ambiguity / duplicate | CS0104, CS0121, CS0111 | Qualify namespace, remove dupe |
   | Missing member | CS1061 | Check spelling, verify member exists |
   | Missing implementation | CS0535 | Implement interface/abstract members |
   | Obsolete API | CS0618 | Replace with recommended alternative |

4. **Fix** — Apply targeted fixes, root-cause/highest-leverage errors first
5. **Rebuild** — Run `dotnet build` again, compare error count
6. **Evaluate** — Zero errors: run `dotnet test` as a sanity check, report
   success. Fewer errors: continue. Same errors: STUCK — exit and re-plan.
   More errors: revert the iteration, report REGRESSION.

### Variant: Test-Fix Loop (max 5; 3 if it follows a build-fix pass)

Same loop, same guards, with `dotnet test --no-build` as the runner and one
critical extra step — **diagnose before fixing**:

1. Read the test — understand the assertion and setup
2. Read the production code — understand the actual behavior
3. Decide where the bug lives: wrong expectation → fix the test; production
   bug → fix the code; incomplete setup → fix the setup; contract changed →
   update the test to match
4. **Never weaken an assertion to make a test pass.**
   BAD: `Assert.Equal(expected, actual)` → `Assert.NotNull(actual)`.
   GOOD: fix the production code so the original assertion passes.

### Fail-Safe Guards (immediate exit)

- **STUCK** — same errors/failures after a fix, or count oscillates
  (3 → 2 → 3): report what could not be fixed and what a human should check
- **REGRESSION** — an iteration introduced more errors than it fixed: revert
  its changes, report
- **Cascading failures** — fixing one error spawns 3+ new ones twice in a
  row: the approach is wrong, stop
- **Critical error** — wrong SDK, missing project file, corrupted solution,
  or the test runner itself fails: human intervention needed, stop
- **User interruption** — finish the current iteration, report progress, ask
  how to proceed

### Other Loops

The same discipline — bounded iterations, progress detection, fail-safe
guards — governs refactor passes (`/de-sloppify`: verify build + tests after
each target, revert on failure) and scaffolding (`/scaffold`: generate, then
run nested build-fix and test-fix loops). Nested loops get a smaller budget
(parent 5 → nested 3), max nesting depth 2, total budget 15.

## Invokes

| Type  | Name                   | Purpose                                    |
|-------|------------------------|--------------------------------------------|
| Skill | `instinct-system` | Capture fix patterns in MEMORY.md          |
| Agent | `dotnet-architect`     | Consulted for architectural error patterns |

### MCP Tools Used

- `get_diagnostics` — Compiler errors/warnings scoped to file or project
- `find_symbol` — Locate moved or renamed types
- `find_references` — Assess blast radius of a broken API change
- `get_project_graph` — Dependency order for fixing reference errors

## Example

```
User: /build-fix

Claude: Running dotnet build...
  Found 12 errors across 4 files.

  Iteration 1/5:
  - 6 errors: Missing namespace (CS0246) — adding using directives
  - 3 errors: Type mismatch (CS1503) — updating method signatures
  - 3 errors: Missing member (CS0117) — package API changed in v9.0

  Rebuilding... 3 errors remaining.

  Iteration 2/5:
  - 3 errors: CS0117 in MassTransit 9.0
    - ConsumeContext.Publish renamed to Send for mandatory routing
    - Fixing 3 call sites

  Rebuilding... 0 errors. Build is green.

  Running dotnet test... All 47 tests passed.
  Added to Memory > Packages: "MassTransit 9.0 renamed Publish to Send"
```

## Related

- `/verify` — Full verification pass (build + test + format + diagnostics)
- `/tdd` — Red-green-refactor when building new features test-first
- `/de-sloppify` — Clean up code quality issues after the build is green
