# ADR-007: The Bash Guard Matches Command Words, Not Command Text

## Status

Accepted

## Context

`hooks/pre-bash-guard.sh` is a PreToolUse hook on the Bash tool. It decides
whether a command is destructive enough to stop, and it had been deciding by
grepping the whole command string:

```bash
if echo "$COMMAND" | grep -qE 'rm\s+-[a-zA-Z]*r[a-zA-Z]*f'; then
  echo "BLOCKED: rm -rf detected ..." >&2
  exit 2
fi
```

A regex over the raw string cannot tell a command from a mention of one. All of
these were blocked in real sessions, and none of them deletes anything:

| Command | Why it was blocked |
|---|---|
| `grep -rn 'rm -rf' docs/` | the search pattern is the dangerous string |
| `python3 build.py  # the old cleanup used rm -rf` | it appears in a comment |
| `git log --grep='git reset --hard'` | it appears in a `--grep` value |
| `echo "never run git push --force on main"` | it appears in a quoted argument |
| `python3 - <<'PY' … PY` | it appears in a heredoc script body |

This is the same defect ADR-006 addressed one tier down: a text scan that fires
on text that is data rather than instruction. It has the same cost, too. A guard
that blocks work an agent legitimately needs teaches everyone to route around
it, and a guard people route around protects nothing.

The prior fix in [#11] had already established the direction — that PR stopped
the guard grepping the whole JSON payload, because a `description` field
mentioning `git reset --hard` was blocking the command it described. The command
field was the right place to look; matching its text was still the wrong way to
look at it.

### Evaluation Criteria

1. **Precision.** Does a mention of a destructive command stay unblocked?
2. **Coverage.** Does the real command stay blocked, however it is spelled?
3. **Reachability.** Can it run in a bare PreToolUse hook, on Windows Git Bash?
4. **Latency.** It runs before *every* Bash call, so its cost is paid constantly.

### Alternatives Considered

**Keep the regexes, add negative lookarounds for quotes and comments.** Cheaper
to write, and wrong for the reason regular expressions are always wrong about
nesting: quoting in shell is recursive. `bash -c "grep 'rm -rf' x"` has a quoted
string inside a quoted string, and no amount of lookaround decides it.

**Shell out to a real parser (`bash -n`, `shfmt`, a Python `shlex` pass).**
Precise, and not reachable. `bash -n` reports syntax validity, not structure.
`shfmt` is not installed on a stock machine. Adding a Python dependency to a hook
that gates every Bash call trades one failure mode for a worse one — the guard
must work with nothing but bash and the tools already in `$PATH`.

**Ask the model to classify the command.** An LLM call before every Bash
invocation, to decide a question that is decidable syntactically. Latency and
cost both fail criterion 4.

## Decision

The guard parses the command and matches the **command word** of each segment.

A command string is split on `;`, `&&`, `||`, `|`, newlines and grouping
parentheses into segments. For each segment, token 0 is taken after stripping
leading `VAR=value` assignments and the wrappers that run something else
(`sudo`, `env`, `command`, `nohup`, `exec`), then reduced to its base name so
`/bin/rm` and `C:\tools\rm.exe` both read as `rm`. Rules match that word and its
arguments — never the surrounding text.

Anything that is data stays data:

- quoted strings, single and double, including quotes spanning whitespace
- `#` comments, recognised only where a word starts
- heredoc bodies, dropped before parsing

Anything that is a command gets scanned as one, to the same standard:

- `sh -c` / `bash -c` string arguments, matching any short-flag cluster
  containing `c` (`-lc`, `-cx`, `-ec`) rather than an exact `-c` token — an
  exact match is one letter away from being a bypass
- `$(...)` and `` `...` `` substitution bodies
- `xargs`, `timeout`, and `find -exec` argument lists, which build a command out
  of their own arguments

Recursion is capped at `MAX_DEPTH` (4).

### Implementation note: why the tokenizer splits on whitespace first

The parser scans characters, and bash has no cursor into a string — advancing
means re-slicing the remainder, which is O(n) per step and O(n²) overall. A
24KB command took ~7s, on a hook that runs before every Bash call. Splitting on
whitespace first (`read -ra`, done in C) bounds the character scan to one short
piece, with quote and substitution state carried across pieces in globals. The
same command now takes ~250ms, and a typical one ~15ms — faster than the six
`echo | grep` subprocesses it replaced.

### Adding a rule

Add a `check_<command>` function, dispatch to it from the `case` in
`check_segment`, and add both directions to `.github/scripts/test-hooks.sh`: the
command blocked, and a benign command that merely mentions it allowed. A rule
without the second test is the defect this ADR exists to prevent.

## Consequences

### Positive

- Mentions of destructive commands — in searches, comments, commit messages,
  documentation, and heredoc scripts — no longer block work.
- Coverage went up rather than down. `sudo rm -rf`, `/bin/rm -rf`,
  `xargs rm -rf`, `find -exec rm -rf`, and `bash -lc '…'` are now recognised
  as the commands they are.
- Rules read as rules. `check_git`, `check_rm` and friends work on words and
  flags, so the next rule is a `case` arm rather than another regex.
- Faster in the common case: parsing happens in-process, with no subprocesses.

### Negative

- The guard is longer and is now a parser, which is a thing that can be wrong in
  more ways than a regex can.
- It approximates shell syntax. A `)` inside a quoted string inside a `$(...)`
  body can still confuse the substitution scan.
- Deliberate obfuscation still defeats it — `eval`, base64, a variable holding
  the command name. This is a guardrail against accidents, not a sandbox.

### Mitigations

- `.github/scripts/test-hooks.sh` runs both directions of every rule on
  windows-latest Git Bash, and shellcheck runs at `--severity=warning`.
- Where the parse is uncertain the guard blocks rather than allows, keeping the
  direction [#11] chose: over-block, never under-block.

[#11]: https://github.com/codewithmukesh/dotnet-claude-kit/pull/11
