---
alwaysApply: true
description: >
  Enforces correct interaction with pre-commit, post-edit, and post-test hooks.
  Never bypass hooks; investigate failures instead.
---

# Hook Rules

## Format Hooks

- **DO** auto-accept post-edit format hooks. They enforce consistent style automatically.
  Rationale: Format hooks maintain codebase consistency. Fighting them creates churn.

- **DON'T** revert or undo formatting changes applied by hooks.
  Rationale: The hook output is the canonical style. Manual overrides cause style drift.

## Pre-Commit Hooks

- **DON'T** skip pre-commit hooks with `--no-verify`. Ever.
  Rationale: Pre-commit hooks catch real issues (build errors, lint failures, format violations). Bypassing them pushes broken code.

- **DO** investigate and fix the root cause when a hook blocks a commit.
  Rationale: The hook is telling you something is wrong. Silencing it hides the problem.

## Post-Test Analysis

- **DO** pipe test output through `hooks/post-test-analyze.sh` when running test workflows (`dotnet test 2>&1 | bash hooks/post-test-analyze.sh`), and act on its summary.
  Rationale: The structured summary surfaces failures and next steps that raw test output buries.

## Hook Infrastructure

- **DON'T** interfere with hook configuration. Claude Code hooks (pre-bash-guard, post-edit-format, post-scaffold-restore) run automatically via `hooks/hooks.json`; pre-commit scripts run via git. See `hooks/README.md` for the full map.
  Rationale: Hooks are configured intentionally. Ad-hoc changes break the automated workflow.

- **DO** wait for post-scaffold-restore to complete after `.csproj` changes before building.
  Rationale: NuGet restore must finish before the build can resolve dependencies. Building too early produces false errors.

## Quick Reference

| Hook | Correct Response |
|---|---|
| Post-edit format | Accept the changes |
| Pre-commit failure | Fix the issue, commit again |
| Post-test-analyze (manual pipe) | Read and act on insights |
| Post-scaffold-restore | Wait for completion before building |
