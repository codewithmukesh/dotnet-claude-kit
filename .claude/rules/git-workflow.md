---
alwaysApply: true
description: >
  Enforces conventional commits, branch naming conventions, atomic commits,
  and PR verification workflow for .NET projects.
---

# Git Workflow Rules

## Commit Messages

- **DO** use conventional commit prefixes: `feat:`, `fix:`, `refactor:`, `test:`, `docs:`, `chore:`.
  Rationale: Enables automated changelogs, semantic versioning, and scannable git history.

- **DO** write the commit body to explain "why", not "what". The diff shows what changed.
  Rationale: Future readers need motivation and context, not a narration of the code changes.

- **DON'T** write vague messages like "fix bug" or "update code".
  Rationale: Useless in git log. Every commit message should be greppable and meaningful.

## Branch Naming

- **DO** use prefixed branch names: `feature/`, `fix/`, `refactor/`.
  Rationale: Prefixes make branch purpose obvious in listings and CI pipeline rules.

- **DON'T** use personal or opaque branch names like `my-branch` or `wip`.
  Rationale: Branch names should communicate intent to the whole team.

## Atomic Commits

- **DO** make one logical change per commit. A feature and its tests belong together.
  Rationale: Atomic commits enable clean reverts, bisects, and cherry-picks.

- **DO** include test changes in the same commit as the feature or fix they cover.
  Rationale: Tests and implementation are one logical unit. Separating them breaks bisectability.

- **DON'T** bundle unrelated changes in a single commit.
  Rationale: Mixed commits make code review harder and reverts dangerous.

## Branch Safety

- **DON'T** force-push to main or master. Ever.
  Rationale: Force-push rewrites shared history and can destroy other contributors' work.

- **DON'T** skip pre-commit hooks with `--no-verify`.
  Rationale: Hooks catch real issues. Bypassing them pushes broken code upstream.

## PR Process

- **DO** run verification (`/verify` or `dotnet build` + `dotnet test`) before creating a PR.
  Rationale: Broken PRs waste reviewer time and block CI pipelines.

- **DO** keep PRs focused on a single concern. Split large changes into stacked PRs.
  Rationale: Smaller PRs get faster, higher-quality reviews.

## Quick Reference

| Action | Convention |
|---|---|
| New feature | `feat: add order export endpoint` |
| Bug fix | `fix: prevent duplicate payments on retry` |
| Refactor | `refactor: extract pricing calculator from OrderService` |
| Tests only | `test: add edge cases for discount calculation` |
| Branch for feature | `feature/order-export` |
| Branch for fix | `fix/duplicate-payment` |
