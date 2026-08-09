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
#                          a destructive string quoted, commented or fed to a
#                          heredoc        -> exit 0 (it is data, not a command)
#                          the same command word reached through sudo, env,
#                          sh -c, $(...), xargs or find -exec -> exit 2
#
# Nothing here executes a destructive command: every case is a command STRING
# handed to the guard inside a payload.
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
  # <&0 is load-bearing: without an explicit redirection bash gives an
  # asynchronous command /dev/null for stdin, so the hook under test would read
  # an empty payload, block nothing, and every case would "pass".
  "$@" <&0 &
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

echo ""
echo "=== pre-bash-guard.sh: command word vs command text ==="

# The guard must judge what a command RUNS, not what its text contains. These
# cases pair each destructive command with a benign one that merely mentions it.
json_escape() {
  local s=$1
  s=${s//\\/\\\\}
  s=${s//\"/\\\"}
  s=${s//$'\n'/\\n}
  s=${s//$'\t'/\\t}
  printf '%s' "$s"
}

# guard_case <expected-exit> <label> <command string>
guard_case() {
  local expect="$1" label="$2" cmd="$3"
  printf '{"session_id":"ci","hook_event_name":"PreToolUse","tool_name":"Bash","tool_input":{"command":"%s","description":"ci case"}}' \
    "$(json_escape "$cmd")" >"$WORKDIR/g.json"
  run_hook 10 hooks/pre-bash-guard.sh "$WORKDIR/g.json" "$WORKDIR/go" "$WORKDIR/ge"
  local rc=$?
  if [ "$rc" -eq "$expect" ]; then
    pass "$label"
  else
    echo "  stdout: [$(cat "$WORKDIR/go")]"
    echo "  stderr: [$(cat "$WORKDIR/ge")]"
    fail "$label (exit $rc, expected $expect)"
  fi
}

# 6. A destructive string as DATA — quoted argument, comment, heredoc body.
#    Each of these was blocked before the guard parsed commands.
guard_case 0 "allows a grep whose search pattern is 'rm -rf'" \
  "grep -rn 'rm -rf' docs/"
guard_case 0 "allows a command with the phrase in a trailing # comment" \
  "python3 build.py  # the old cleanup used rm -rf and shutil.rmtree"
guard_case 0 "allows git log --grep for a destructive phrase" \
  "git log --grep='git reset --hard' --oneline"
guard_case 0 "allows a heredoc script body containing the phrase" \
  "python3 - <<'PY'
import shutil
# replaced the old rm -rf call
print(1)
PY"
guard_case 0 "allows echoing documentation about force push" \
  'echo "never run git push --force on main"'
guard_case 0 "allows a commit message naming a destructive command" \
  'git commit -m "docs: warn against rm -rf in scripts"'

# 7. Every pre-existing block must still block.
guard_case 2 "still blocks git reset --hard" "git reset --hard HEAD~1"
guard_case 2 "still blocks git clean -fd" "git clean -fd"
guard_case 2 "still blocks git checkout ." "git checkout ."
guard_case 2 "still blocks git push -f" "git push -f origin main"
guard_case 2 "still blocks rm -rf on a project path" "rm -rf /Users/dev/project/src"

# 8. Safe rm -rf targets must stay allowed.
guard_case 0 "allows rm -rf bin" "rm -rf bin"
guard_case 0 "allows rm -rf on a nested obj directory" "rm -rf ./src/App/obj"
guard_case 0 "allows rm -rf under /tmp" "rm -rf /tmp/scratch"
guard_case 0 "allows rm -rf bin with a redirect" "rm -rf bin > /dev/null"

# 9. Reaching the same command word indirectly must not evade the guard.
guard_case 2 "blocks rm -rf behind a leading path" "/bin/rm -rf /Users/dev/project"
guard_case 2 "blocks rm -rf behind env assignments and sudo" \
  "FOO=bar sudo rm -rf /Users/dev/project"
guard_case 2 "blocks rm -rf in a later segment" \
  "echo building && rm -rf /Users/dev/project"
guard_case 2 "blocks rm -rf inside sh -c" "sh -c 'rm -rf /Users/dev/project'"
guard_case 2 "blocks rm -rf inside a combined bash -lc flag" \
  "bash -lc 'rm -rf /Users/dev/project'"
guard_case 2 "blocks git reset --hard inside bash -cx" \
  "bash -cx 'git reset --hard HEAD~1'"
guard_case 2 "blocks rm -rf inside a command substitution" \
  'echo $(rm -rf /Users/dev/project)'
guard_case 2 "blocks rm -rf passed through xargs" \
  "find . -name obj | xargs rm -rf /Users/dev/project"
guard_case 2 "blocks rm -rf passed through find -exec" \
  "find . -name '*.tmp' -exec rm -rf /Users/dev/project {} ;"

echo ""
echo "=== pre-bash-guard.sh: shared machine resources ==="

# 10. Process kills that match by pattern reap whatever else is running.
guard_case 2 "blocks pkill by pattern" "pkill -f dotnet"
guard_case 2 "blocks killall by process name" "killall dotnet"
guard_case 2 "blocks killall reached by absolute path" "/usr/bin/killall node"
guard_case 2 "blocks kill against a pgrep substitution" 'kill -9 $(pgrep -f MyApp)'
guard_case 2 "blocks kill against a process name" "kill dotnet"
guard_case 0 "allows kill against a numeric PID" "kill -9 12345"
guard_case 0 "allows kill -s with a numeric PID" "kill -s TERM 4242"
guard_case 0 "allows kill against a PID held in a variable" 'kill $SERVER_PID'
guard_case 0 "allows pgrep, which only reads" "pgrep -f dotnet"
guard_case 0 "allows a command that merely mentions pkill" \
  'echo "run pkill -f dotnet if it hangs"'

# 11. Daemons and caches shared by every project on the machine.
guard_case 2 "blocks dotnet build-server shutdown" "dotnet build-server shutdown"
guard_case 0 "allows dotnet build" "dotnet build -c Release"
guard_case 2 "blocks recursive delete of the NuGet package cache" \
  "rm -rf ~/.nuget/packages"
guard_case 2 "blocks recursive delete of ~/.dotnet without -f" "rm -r ~/.dotnet"
guard_case 2 "blocks a cache path whose leaf looks like build output" \
  "rm -rf ~/.dotnet/bin"
guard_case 2 "blocks the cache path spelled with \$HOME" \
  'rm -rf $HOME/.nuget/packages'
guard_case 0 "allows rm -rf on project build output" "rm -rf src/App/obj"
guard_case 0 "allows a command that merely mentions the cache path" \
  'echo "clear ~/.nuget/packages if restore is wedged"'

echo ""
if [ "$FAILURES" -gt 0 ]; then
  echo "$FAILURES hook test(s) failed"
  exit 1
fi
echo "All hook tests passed"
