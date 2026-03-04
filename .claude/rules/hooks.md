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

- **DO** review post-test-analyze hook output. It contains actionable insights about test quality and coverage.
  Rationale: Ignoring analysis output means missing regressions and quality signals.

## Hook Infrastructure

- **DON'T** interfere with hook configuration. Hooks run automatically via plugin settings.
  Rationale: Hooks are configured intentionally. Ad-hoc changes break the automated workflow.

- **DO** wait for post-scaffold-restore to complete after `.csproj` changes before building.
  Rationale: NuGet restore must finish before the build can resolve dependencies. Building too early produces false errors.

## Quick Reference

| Hook | Correct Response |
|---|---|
| Post-edit format | Accept the changes |
| Pre-commit failure | Fix the issue, commit again |
| Post-test-analyze | Read and act on insights |
| Post-scaffold-restore | Wait for completion before building |
