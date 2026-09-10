#!/usr/bin/env bash
# CI behavior tests for hooks/*.sh under Git Bash (runs on windows-latest).
#
# Exercises realistic Claude Code PreToolUse/PostToolUse JSON payloads —
# including Windows backslash paths — and asserts the hooks terminate
# promptly with the intended exit codes:
#
#   post-edit-format.sh  : must not hang on Windows paths (drive-root walk guard)
#   pre-bash-guard.sh    : benign command  -> exit 0
#                          force push      -> exit 2, reason on stderr
#                          payload that merely MENTIONS "reset --hard" in a
#                          string field    -> exit 0 (regression test for the
#                          no-jq raw-payload over-blocking bug)
set -u

cd "$(dirname "$0")/../.." || exit 1

HOOK_TIMEOUT="${HOOK_TIMEOUT:-30}"
FAILURES=0
WORKDIR=$(mktemp -d)
trap 'rm -rf "$WORKDIR"' EXIT

pass() { echo "PASS: $1"; }
fail() { echo "FAIL: $1"; FAILURES=$((FAILURES + 1)); }

# Portable timeout: prefer coreutils timeout (shipped with Git for Windows),
# fall back to a background watchdog. Returns 124+ when the command was killed.
run_with_timeout() {
  local limit="$1"
  shift
  if command -v timeout >/dev/null 2>&1; then
    timeout --kill-after=5 "$limit" "$@"
    return $?
  fi
  "$@" &
  local pid=$!
  (
    sleep "$limit"
    kill -9 "$pid" 2>/dev/null
  ) &
  local watchdog=$!
  wait "$pid" 2>/dev/null
  local rc=$?
  kill "$watchdog" 2>/dev/null || true
  wait "$watchdog" 2>/dev/null || true
  return "$rc"
}

# Run a hook with a JSON payload on stdin, isolated from env-var shortcuts.
# Usage: run_hook <timeout> <script> <payload-file> <stdout-file> <stderr-file>
run_hook() {
  local limit="$1" script="$2" payload="$3" out="$4" err="$5"
  run_with_timeout "$limit" \
    env -u CLAUDE_TOOL_INPUT -u CLAUDE_EDITED_FILE \
    bash "$script" <"$payload" >"$out" 2>"$err"
}

echo "=== post-edit-format.sh ==="

# 1. PostToolUse payload with a Windows backslash path to a nonexistent file.
#    Must terminate (no drive-root infinite loop) and exit 0.
cat >"$WORKDIR/p1.json" <<'EOF'
{"session_id":"ci","transcript_path":"C:\\Users\\x\\.claude\\t.jsonl","cwd":"C:\\Users\\x\\repo","hook_event_name":"PostToolUse","tool_name":"Edit","tool_input":{"file_path":"C:\\Users\\x\\repo\\Foo.cs","old_string":"a","new_string":"b"}}
EOF
run_hook "$HOOK_TIMEOUT" hooks/post-edit-format.sh "$WORKDIR/p1.json" "$WORKDIR/o1" "$WORKDIR/e1"
rc=$?
if [ "$rc" -ge 124 ]; then
  fail "post-edit-format hung (>${HOOK_TIMEOUT}s) on Windows path payload"
elif [ "$rc" -ne 0 ]; then
  fail "post-edit-format exited $rc (expected 0) on Windows path payload"
else
  pass "post-edit-format terminated cleanly on Windows backslash path"
fi

# 2. Existing .cs file in a project-less temp directory — exercises the
#    directory walk all the way to the drive root (the historic hang scenario).
mkdir -p "$WORKDIR/walk/src"
printf 'class Foo { }\n' >"$WORKDIR/walk/src/Foo.cs"
if command -v cygpath >/dev/null 2>&1; then
  winpath=$(cygpath -w "$WORKDIR/walk/src/Foo.cs")
else
  winpath="$WORKDIR/walk/src/Foo.cs"
fi
jsonpath=${winpath//\\/\\\\}
printf '{"hook_event_name":"PostToolUse","tool_name":"Write","tool_input":{"file_path":"%s"}}' "$jsonpath" >"$WORKDIR/p2.json"
run_hook "$HOOK_TIMEOUT" hooks/post-edit-format.sh "$WORKDIR/p2.json" "$WORKDIR/o2" "$WORKDIR/e2"
rc=$?
if [ "$rc" -ge 124 ]; then
  fail "post-edit-format hung walking directories above $winpath"
elif [ "$rc" -ne 0 ]; then
  fail "post-edit-format exited $rc (expected 0) for existing .cs file outside any project"
else
  pass "post-edit-format completed the directory walk for a real .cs file"
fi

echo "=== pre-bash-guard.sh ==="

# 3. Benign command must be allowed.
cat >"$WORKDIR/p3.json" <<'EOF'
{"session_id":"ci","hook_event_name":"PreToolUse","tool_name":"Bash","tool_input":{"command":"git status","description":"Show working tree status"}}
EOF
run_hook 10 hooks/pre-bash-guard.sh "$WORKDIR/p3.json" "$WORKDIR/o3" "$WORKDIR/e3"
rc=$?
if [ "$rc" -eq 0 ]; then
  pass "pre-bash-guard allows benign command (exit 0)"
else
  fail "pre-bash-guard exited $rc (expected 0) for benign 'git status'"
fi

# 4. Force push must be blocked with exit 2 and the reason printed to stderr.
cat >"$WORKDIR/p4.json" <<'EOF'
{"session_id":"ci","hook_event_name":"PreToolUse","tool_name":"Bash","tool_input":{"command":"git push --force origin main","description":"Force push to main"}}
EOF
run_hook 10 hooks/pre-bash-guard.sh "$WORKDIR/p4.json" "$WORKDIR/o4" "$WORKDIR/e4"
rc=$?
if [ "$rc" -ne 2 ]; then
  fail "pre-bash-guard exited $rc (expected 2) for 'git push --force origin main'"
else
  pass "pre-bash-guard blocks force push (exit 2)"
fi
if [ -s "$WORKDIR/e4" ] && grep -qi 'force' "$WORKDIR/e4"; then
  pass "pre-bash-guard prints the block reason to stderr"
else
  echo "  stderr was: [$(cat "$WORKDIR/e4")]"
  echo "  stdout was: [$(cat "$WORKDIR/o4")]"
  fail "pre-bash-guard must print the block reason to stderr (Claude Code surfaces stderr on exit 2)"
fi

# 5. Regression: a benign command whose JSON payload merely MENTIONS
#    "reset --hard" in string fields must NOT be blocked. Without proper
#    jq extraction of .tool_input.command, the raw payload scan matches
#    "git reset --hard" in the description field and over-blocks.
cat >"$WORKDIR/p5.json" <<'EOF'
{"session_id":"ci","hook_event_name":"PreToolUse","tool_name":"Bash","tool_input":{"command":"git commit -m \"docs: add warning about reset --hard\"","description":"Commit docs warning about git reset --hard"}}
EOF
run_hook 10 hooks/pre-bash-guard.sh "$WORKDIR/p5.json" "$WORKDIR/o5" "$WORKDIR/e5"
rc=$?
if [ "$rc" -eq 0 ]; then
  pass "pre-bash-guard allows benign commit whose payload mentions 'reset --hard' (no over-blocking)"
else
  echo "  output was: [$(cat "$WORKDIR/o5" "$WORKDIR/e5")]"
  fail "pre-bash-guard exited $rc (expected 0) — over-blocking regression: payload text matched instead of the parsed command"
fi

# 6-9. Regression: a guarded phrase appearing as DATA inside the command must
#    not block. Test 5 covers the phrase in another JSON field; these cover it
#    in the command itself — a grep pattern, a commit message, and a word that
#    merely ends in the letters "rm". None of them delete or discard anything,
#    and the first is a command that reads the guard's own source.
allow_command() {
  local label="$1" command_json="$2" n="$3"
  printf '{"session_id":"ci","hook_event_name":"PreToolUse","tool_name":"Bash","tool_input":{"command":"%s"}}' \
    "$command_json" >"$WORKDIR/p$n.json"
  run_hook 10 hooks/pre-bash-guard.sh "$WORKDIR/p$n.json" "$WORKDIR/o$n" "$WORKDIR/e$n"
  local rc=$?
  if [ "$rc" -eq 0 ]; then
    pass "pre-bash-guard allows $label"
  else
    echo "  output was: [$(cat "$WORKDIR/o$n" "$WORKDIR/e$n")]"
    fail "pre-bash-guard exited $rc (expected 0) — over-blocking: $label"
  fi
}

allow_command "a grep pattern containing the guarded phrase" \
  "grep -n 'Remove-Item\\\\|rm -rf\\\\|BLOCKED' hooks/pre-bash-guard.sh" 6
allow_command "a commit message documenting the guard" \
  "git commit -m \\\"docs: explain why rm -rf is blocked\\\"" 7
allow_command "a word that merely ends in r-m" \
  "confirm -rf task" 8
allow_command "grepping the docs for a guarded git phrase" \
  "grep -rn 'git reset --hard' docs/" 9
allow_command "a commit message that merely names a shell wrapper" \
  "git commit -m \\\"use bash -c to run it, never rm -rf /tmp/x\\\"" 12
allow_command "a heredoc commit message about the guard" \
  "cat > /tmp/m.txt <<'EOF'\\ndocs: explain why rm -rf is blocked\\nand why bash -c is treated as a command\\nEOF\\ngit commit -F /tmp/m.txt" 13

# 10-11. The other half of the same change: masking quoted text must not let a
#    real command through. A shell wrapper hands its quoted argument back to a
#    shell, so that argument is a command again and still has to be blocked.
block_command() {
  local label="$1" command_json="$2" n="$3"
  printf '{"session_id":"ci","hook_event_name":"PreToolUse","tool_name":"Bash","tool_input":{"command":"%s"}}' \
    "$command_json" >"$WORKDIR/p$n.json"
  run_hook 10 hooks/pre-bash-guard.sh "$WORKDIR/p$n.json" "$WORKDIR/o$n" "$WORKDIR/e$n"
  local rc=$?
  if [ "$rc" -eq 2 ]; then
    pass "pre-bash-guard still blocks $label"
  else
    fail "pre-bash-guard exited $rc (expected 2) — under-blocking: $label"
  fi
}

block_command "a recursive force delete of a project directory" \
  "rm -rf src/MyApp/Controllers" 10
block_command "a recursive force delete wrapped in bash -c" \
  "bash -c \\\"rm -rf /\\\"" 11

echo ""
if [ "$FAILURES" -gt 0 ]; then
  echo "$FAILURES hook test(s) failed"
  exit 1
fi
echo "All hook tests passed"
