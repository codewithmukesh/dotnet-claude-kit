# Hooks

This directory contains three kinds of scripts. Only the first kind runs
automatically through Claude Code.

## Claude Code hooks (declared in `hooks.json`)

These receive the hook payload as JSON on stdin and run automatically while
Claude works:

| Script | Event | Purpose |
|---|---|---|
| `pre-bash-guard.sh` | PreToolUse (Bash) | Blocks destructive commands (force push, `git reset --hard`, unsafe `rm -rf`) |
| `post-edit-format.sh` | PostToolUse (Edit\|Write) | Runs `dotnet format` on edited `.cs` files |
| `post-scaffold-restore.sh` | PostToolUse (Edit\|Write) | Runs `dotnet restore` after `.csproj` changes |

## Git pre-commit hooks (install manually)

These are standard git hooks, not Claude Code hooks. Wire them into your
repo's pre-commit hook:

| Script | Purpose |
|---|---|
| `pre-commit-format.sh` | Fails the commit if `dotnet format --verify-no-changes` finds issues |
| `pre-commit-antipattern.sh` | Blocks commits containing `DateTime.Now`, `async void`, `new HttpClient()`, or sync-over-async in staged files |

```bash
# One-time setup per clone — .git/hooks/pre-commit
#!/usr/bin/env bash
bash hooks/pre-commit-format.sh && bash hooks/pre-commit-antipattern.sh
```

## Utility scripts (invoked by commands and workflows)

Run these directly or let kit commands (`/verify`, `/tdd`, `/health-check`)
invoke them:

| Script | Usage |
|---|---|
| `pre-build-validate.sh` | `bash hooks/pre-build-validate.sh [solution-dir]` — checks solution structure (sln file, Directory.Build.props, global.json, test projects) |
| `post-test-analyze.sh` | `dotnet test 2>&1 \| bash hooks/post-test-analyze.sh` — summarizes test results with actionable next steps |
