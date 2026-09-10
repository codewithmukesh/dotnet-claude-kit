#!/usr/bin/env bash
# Pre-Bash Guard — Block destructive operations
# Inspects the Bash tool command before execution. Reads CLAUDE_TOOL_INPUT when set,
# otherwise the PreToolUse payload on stdin — current Claude Code passes hook data on
# stdin as JSON, not via CLAUDE_TOOL_INPUT, so without the stdin path the guard would
# see an empty command and never block. The command field is parsed with jq so escaped
# quotes inside the command don't truncate it; when jq is unavailable, a sed extraction
# pulls the command field instead of matching against the whole payload (grepping the
# raw JSON blocked on matches in *other* fields, e.g. a description mentioning
# "git reset --hard"). Only if both extractions fail does the raw payload get used,
# which over-blocks rather than under-blocks — the safe direction here.
# Exit 2 = block the command, exit 0 = allow. Block reasons go to stderr — on exit 2
# Claude Code feeds stderr (not stdout) back to Claude.

COMMAND="${CLAUDE_TOOL_INPUT:-}"

if [[ -z "$COMMAND" ]] && [[ ! -t 0 ]]; then
  STDIN=$(cat)
  if command -v jq >/dev/null 2>&1; then
    COMMAND=$(printf '%s' "$STDIN" | jq -r '.tool_input.command // empty' 2>/dev/null) || true
  fi
  if [[ -z "$COMMAND" ]]; then
    # No jq: extract the "command" value with sed. Stops at the first unescaped
    # quote, so escaped quotes truncate the tail — acceptable, since the guard
    # patterns match on prefixes and truncation only risks under-matching the
    # tail of unusual commands, not blocking on unrelated payload fields.
    COMMAND=$(printf '%s' "$STDIN" | sed -n 's/.*"command"[[:space:]]*:[[:space:]]*"\(\(\\.\|[^"\\]\)*\)".*/\1/p' | head -1) || true
    # Undo the JSON string escapes jq would have undone. Without this a
    # multi-line command arrives as one line carrying literal \n, and anything
    # that reads the command line by line — heredoc bodies, below — cannot see
    # its shape. Backslash last, so an escaped backslash is not re-expanded.
    COMMAND=$(printf '%s' "$COMMAND" | sed -e 's/\\n/\
/g' -e 's/\\t/\t/g' -e 's/\\"/"/g' -e 's/\\\\/\\/g')
  fi
  [[ -z "$COMMAND" ]] && COMMAND="$STDIN"
fi

# ── Where a guarded verb has to appear ──────────────────────────────
#
# Extracting the command field stopped the guard firing on *other* JSON fields,
# but not on the guarded words appearing as data inside the command itself.
# Real false positives, all from one session:
#
#   grep -n 'Remove-Item\|rm -rf\|BLOCKED' hooks/pre-bash-guard.sh
#   grep -rn 'git reset --hard' docs/
#   git commit -m "document why rm -rf is guarded"
#   confirm -rf task          # "confirm" ends in the letters r and m
#
# None of them delete anything. The first is reading this very file. A guard
# that blocks reading itself, or writing about itself, teaches the reader to
# route around it — which is worse than not guarding.
#
# Two passes fix it.

# 1. Heredoc bodies are data. A commit message written with `cat >msg <<'EOF'`
#    is prose, and prose about a guard quotes the thing it guards. Each body
#    line collapses to a placeholder; the delimiter lines stay so the shape of
#    the command is still readable to the checks below.
SCAN_RAW="$COMMAND"
NO_HEREDOC=$(printf '%s' "$COMMAND" | awk -v sq="'" -v dq='"' '
  inhd {
    line = $0
    sub(/^[ \t]+/, "", line)
    if (line == marker) { inhd = 0; print } else { print "Q" }
    next
  }
  {
    if (match($0, "<<-?[ \t]*[" sq dq "]?[A-Za-z_][A-Za-z0-9_]*")) {
      m = substr($0, RSTART, RLENGTH)
      sub(/^<<-?[ \t]*/, "", m)
      gsub(sq, "", m); gsub(dq, "", m)
      marker = m; inhd = 1
    }
    print
  }
')

# 2. Quoted spans are data too. That is what separates
#    `git commit -m "... rm -rf ..."` from `rm -rf`: one is an argument
#    somebody is writing, the other is a command. It also collapses a grep
#    alternation, escapes and all, into a single harmless token.
MASKED=$(printf '%s' "$NO_HEREDOC" | sed -e "s/'[^']*'/Q/g" -e 's/"[^"]*"/Q/g')
SCAN="$MASKED"

# 3. Unless a shell wrapper hands that quoted text straight back to a shell, in
#    which case it is a command again and has to be read as one. The wrapper
#    itself is looked for in the *masked* text and in command position, so that
#    prose merely naming one — this comment, or a commit message about it —
#    does not switch the scan back on.
if printf '%s' "$MASKED" | grep -qE '(^|[[:space:];&|(){}])((ba|z|k|da)?sh[[:space:]]+(-[a-zA-Z]+[[:space:]]+)*-c|eval)([[:space:]]|$)'; then
  SCAN="$NO_HEREDOC"
fi

# 4. A verb only counts in *command position*: the start of the command, or
#    straight after something that begins one. This is what stops "confirm -rf"
#    — the letters r and m at the end of an ordinary word are not a command.
SEP=$'(^|[[:space:];&|(){}<>"\'`])[[:space:]]*'

# Whether a guarded verb appears in command position. Takes an ERE for the verb.
in_command_position() {
  printf '%s' "$SCAN" | grep -qE "${SEP}$1"
}

# ── Destructive git operations ──────────────────────────────────────

if in_command_position 'git[[:space:]]+push[[:space:]]+.*--force' \
  || in_command_position 'git[[:space:]]+push[[:space:]]+-f\b'; then
  echo "BLOCKED: Force push detected. Use regular push or discuss with the user first." >&2
  exit 2
fi

if in_command_position 'git[[:space:]]+reset[[:space:]]+--hard'; then
  echo "BLOCKED: git reset --hard will discard all uncommitted changes. Discuss with the user first." >&2
  exit 2
fi

if in_command_position 'git[[:space:]]+clean[[:space:]]+-[a-zA-Z]*f'; then
  echo "BLOCKED: git clean -f will permanently delete untracked files. Discuss with the user first." >&2
  exit 2
fi

if in_command_position 'git[[:space:]]+checkout[[:space:]]+\.'; then
  echo "BLOCKED: git checkout . will discard all unstaged changes. Discuss with the user first." >&2
  exit 2
fi

# ── Dangerous file operations ───────────────────────────────────────

if in_command_position 'rm[[:space:]]+-[a-zA-Z]*r[a-zA-Z]*f' \
  || in_command_position 'rm[[:space:]]+-[a-zA-Z]*f[a-zA-Z]*r'; then
  # Allow recursive-force rm on safe targets (node_modules, bin, obj, TestResults,
  # .vs, /tmp). Matches any flag spelling the detector matches (-rf, -fr, -rfv, ...)
  # and allows ./-prefixed or relative-path-prefixed targets (e.g. ./bin,
  # src/MyApp/obj). Absolute paths stay blocked (so /bin is NOT allowed) except /tmp.
  # A quoted target is allowed too — "node_modules" is the same target as
  # node_modules, and quoting it is what anybody does once a path has a space.
  # Read against the unmasked text: masking replaced the quoted target with a
  # placeholder, and the allowlist is the one check that needs the path itself.
  if echo "$SCAN_RAW" | grep -qE 'rm[[:space:]]+-[a-zA-Z]+[[:space:]]+["'"'"']?(\./)?([^ /"'"'"'][^ "'"'"']*/)?(node_modules|bin|obj|TestResults|\.vs)/?["'"'"']?([[:space:]]|$)|rm[[:space:]]+-[a-zA-Z]+[[:space:]]+["'"'"']?/tmp(/[^ "'"'"']*)?["'"'"']?([[:space:]]|$)'; then
    : # safe, fall through to exit 0
  else
    echo "BLOCKED: rm -rf detected in a project directory. Verify the target path is intentional." >&2
    exit 2
  fi
fi

# ── dotnet run without launch settings check ────────────────────────

if in_command_position 'dotnet[[:space:]]+run\b'; then
  echo "WARNING: dotnet run detected. Ensure launchSettings.json exists and the correct profile is selected."
  # Allow but warn — exit 0 so it proceeds
fi

exit 0
