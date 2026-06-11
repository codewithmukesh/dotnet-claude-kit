---
name: build-fix
description: >
  Iteratively fix dotnet build errors. Runs an autonomous build-fix loop that
  parses compiler errors, categorizes them, applies fixes, and rebuilds until
  the build is green or the iteration limit is reached. Invoke when the build
  is broken, after a major refactor, after dependency updates, or when the user
  says "fix the build", "build is broken", "make it compile".
---

# /build-fix

## What

Autonomous build-fix loop that drives a broken `dotnet build` to green. The
command builds the solution, parses every compiler error, categorizes each error
by root cause, applies targeted fixes, and rebuilds. It repeats this cycle until
the build succeeds or the maximum iteration count (5) is reached.

This is not a single-pass fix. It handles cascading errors where fixing one issue
reveals the next -- the same way an experienced developer would work through a
wall of red.

## When

- The build is broken and there are multiple compiler errors
- After a major refactor that touched type names, namespaces, or method signatures
- After updating NuGet packages (especially major version bumps)
- After merging a branch with conflicts resolved but not compiled
- After scaffolding or code generation that needs manual adjustments
- User says: "fix the build", "make it compile", "build is broken"

## How

### Loop (max 5 iterations)

1. **Build** -- Run `dotnet build` and capture the full error output
2. **Parse** -- Extract every `error CS####` with file path, line number, and message
3. **Categorize** -- Group errors by root cause:
   - **Missing reference** -- Unresolved type or namespace (CS0246, CS0234)
   - **Type mismatch** -- Wrong type, missing cast, signature change (CS0029, CS1503)
   - **API change** -- Removed or renamed member after package update (CS0117, CS7036)
   - **Nullability** -- Nullable reference type violations (CS8600, CS8602, CS8604)
   - **Ambiguity** -- Ambiguous calls, duplicate definitions (CS0121, CS0111)
   - **Missing implementation** -- Interface or abstract members not implemented (CS0535)
4. **Fix** -- Apply targeted fixes per category, prioritizing root-cause errors first
   (a missing `using` directive causes downstream errors that vanish once added)
5. **Rebuild** -- Run `dotnet build` again and check the error count
6. **Evaluate** -- If zero errors, report success. If errors decreased, continue.
   If errors did not decrease, re-analyze with fresh context and try a different approach.
   If iteration limit reached, report remaining errors with analysis.

### Exit Conditions

- **Green build** -- All errors resolved. Run `dotnet test` as a final sanity check.
- **Iteration limit** -- Report which errors remain, what was tried, and suggest
  manual intervention points.
- **Stuck** -- If two consecutive iterations produce the same error count, break
  out early and report the blocking issues.

## Invokes

| Type   | Name                   | Purpose                                    |
|--------|------------------------|--------------------------------------------|
| Skill  | `self-correction-loop` | Capture fix patterns in MEMORY.md          |
| Agent  | `dotnet-architect`     | Consulted for architectural error patterns |

### MCP Tools Used

- `get_diagnostics` -- Get compiler errors and warnings scoped to file or project
- `find_symbol` -- Locate moved or renamed types
- `find_references` -- Assess blast radius of a broken API change
- `get_project_graph` -- Understand dependency order for fixing reference errors

## Example

```
User: /build-fix

Claude: Running dotnet build...
  Found 12 errors across 4 files.

  Iteration 1/5:
  - 6 errors: Missing namespace (CS0246) -- Adding `using` directives
  - 3 errors: Type mismatch (CS1503) -- Updating method signatures
  - 3 errors: Missing member (CS0117) -- Package API changed in v9.0

  Rebuilding... 3 errors remaining.

  Iteration 2/5:
  - 3 errors: Missing member (CS0117) in MassTransit 9.0
    - `ConsumeContext.Publish` renamed to `ConsumeContext.Send` for mandatory routing
    - Fixing 3 call sites

  Rebuilding... 0 errors. Build is green.

  Running dotnet test... All 47 tests passed.
  Added to Memory > Packages: "MassTransit 9.0 renamed Publish to Send for mandatory routing"
```

## Related

- `/verify` -- Full verification pass (build + test + format + diagnostics)
- `/de-sloppify` -- Clean up code quality issues after the build is green
- `/health-check` -- Assess overall project health including build status
