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
#
# The command itself is then parsed into segments and matched on each segment's
# COMMAND WORD, never on the raw text: a dangerous string sitting in a quoted
# argument, a comment, or a heredoc body is data, not an instruction. Rationale
# and the process for adding a rule: knowledge/decisions/007-command-word-guard.md
#
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
    # quote, so escaped quotes truncate the tail — acceptable, since truncation
    # only risks under-matching the tail of unusual commands, not blocking on
    # unrelated payload fields.
    COMMAND=$(printf '%s' "$STDIN" | sed -n 's/.*"command"[[:space:]]*:[[:space:]]*"\(\(\\.\|[^"\\]\)*\)".*/\1/p' | head -1) || true
  fi
  [[ -z "$COMMAND" ]] && COMMAND="$STDIN"
fi

# ── Parsing ─────────────────────────────────────────────────────────
# Characters that end an ordinary run of text. Everything else is copied
# into the current word verbatim, so the scan jumps between metacharacters
# instead of walking the string one character at a time.
META=$'[\\\\ \t\n\'"&|;<>#$`(){}]'
RS=$'\x1e'          # marks a segment boundary in the WORDS stream
MAX_DEPTH=4         # recursion limit for sh -c and $(...) bodies

# Drop heredoc bodies. `python3 - <<'PY' ... PY` feeds a script to a command;
# those lines are data and must never be read as commands.
HEREDOC_STRIPPED=""
strip_heredocs() {
  local text=$1 line trimmed delim=""
  local hd_re='<<-?[[:space:]]*["'\'']?([A-Za-z_][A-Za-z0-9_]*)'
  HEREDOC_STRIPPED=""
  while IFS= read -r line || [[ -n "$line" ]]; do
    if [[ -n "$delim" ]]; then
      trimmed=${line#"${line%%[![:space:]]*}"}
      [[ "$line" == "$delim" || "$trimmed" == "$delim" ]] && delim=""
      continue
    fi
    HEREDOC_STRIPPED+="$line"$'\n'
    [[ "$line" =~ $hd_re ]] && delim=${BASH_REMATCH[1]}
  done <<<"$text"
}

# Split a command string into words grouped into segments. Honors single
# quotes, double quotes, backslash escapes and comments; discards redirection
# operators and their targets; records $(...) and `...` bodies in SUBCOMMANDS
# so they can be scanned as commands in their own right.
#
# The string is split on whitespace first and only then scanned character by
# character. Consuming a whole command one character at a time re-slices the
# remainder on every step, which is quadratic — a 24KB command took ~7s. The
# shell does the whitespace split in C, so the character scan only ever runs
# over one short piece, and quoted state carries across pieces in _quote.
WORDS=()
SUBCOMMANDS=()
_word=""
_have=0
_drop=0
_quote=""
_skip_line=0
_subst=0     # open $( ) nesting depth
_subst_buf="" # body of the substitution being collected
_tick=0      # inside a `...` substitution

_end_word() {
  ((_have)) || return 0
  if ((_drop)); then _drop=0; else WORDS+=("$_word"); fi
  _word=""
  _have=0
}

_end_segment() {
  _end_word
  ((${#WORDS[@]} == 0)) && return 0
  [[ ${WORDS[${#WORDS[@]} - 1]} == "$RS" ]] || WORDS+=("$RS")
}

# Scan one whitespace-free piece, continuing any quote left open by the last.
_consume() {
  local s=$1 chunk c nx
  while [[ -n "$s" ]]; do
    # A substitution body is a command in its own right; collect it whole,
    # across whitespace and lines, and hand it back for a separate scan.
    if ((_subst > 0)); then
      chunk=${s%%[()]*}
      if [[ "$chunk" == "$s" ]]; then
        _subst_buf+="$s"
        s=""
      else
        _subst_buf+="$chunk"
        s=${s#"$chunk"}
        c=${s:0:1}
        s=${s:1}
        if [[ "$c" == '(' ]]; then
          _subst=$((_subst + 1))
          _subst_buf+='('
        else
          _subst=$((_subst - 1))
          if ((_subst == 0)); then
            SUBCOMMANDS+=("$_subst_buf")
            _subst_buf=""
            _word+='$(...)'
            _have=1
          else
            _subst_buf+=')'
          fi
        fi
      fi
      continue
    fi
    if ((_tick)); then
      if [[ "$s" == *'`'* ]]; then
        _subst_buf+=${s%%\`*}
        s=${s#*\`}
        SUBCOMMANDS+=("$_subst_buf")
        _subst_buf=""
        _tick=0
      else
        _subst_buf+="$s"
        s=""
      fi
      continue
    fi
    if [[ "$_quote" == "'" ]]; then
      if [[ "$s" == *"'"* ]]; then
        _word+=${s%%\'*}
        s=${s#*\'}
        _quote=""
      else
        _word+="$s"
        s=""
      fi
      _have=1
      continue
    fi
    if [[ "$_quote" == '"' ]]; then
      chunk=${s%%[\\\"]*}
      if [[ "$chunk" == "$s" ]]; then
        _word+="$s"
        s=""
      else
        _word+="$chunk"
        s=${s#"$chunk"}
        if [[ ${s:0:1} == '"' ]]; then
          s=${s:1}
          _quote=""
        else
          nx=${s:1:1}
          case $nx in
            '"' | '\' | '$' | '`') _word+="$nx" ;;
            *) _word+="\\$nx" ;;
          esac
          s=${s:2}
        fi
      fi
      _have=1
      continue
    fi

    chunk=${s%%$META*}
    if [[ "$chunk" == "$s" ]]; then
      _word+="$chunk"
      _have=1
      return 0
    fi
    if [[ -n "$chunk" ]]; then
      _word+="$chunk"
      _have=1
    fi
    s=${s#"$chunk"}
    c=${s:0:1}
    s=${s:1}

    case $c in
      "'" | '"')
        _quote=$c
        _have=1
        ;;
      '\')
        # A trailing backslash continues the line; otherwise the next
        # character is literal.
        if [[ -n "$s" ]]; then
          _word+="${s:0:1}"
          s=${s:1}
          _have=1
        fi
        ;;
      '#')
        # A comment only starts where a word starts.
        if ((_have)); then
          _word+='#'
        else
          _skip_line=1
          return 0
        fi
        ;;
      '$')
        if [[ ${s:0:1} == '(' ]]; then
          s=${s:1}
          _subst=1
          _subst_buf=""
        else
          _word+='$'
          _have=1
        fi
        ;;
      '`')
        _tick=1
        _subst_buf=""
        ;;
      '<' | '>')
        # An fd prefix (the 2 in 2>&1) belongs to the operator, not to a word.
        [[ "$_word" =~ ^[0-9]+$ ]] && { _word=""; _have=0; }
        _end_word
        while [[ ${s:0:1} == '<' || ${s:0:1} == '>' || ${s:0:1} == '&' ]]; do s=${s:1}; done
        _drop=1
        ;;
      ';' | '&' | '|')
        _end_segment
        ;;
      '(' | ')' | '{' | '}')
        # Grouping only where a command word would stand.
        if ((_have)); then
          _word+="$c"
        else
          _end_segment
        fi
        ;;
    esac
  done
}

tokenize() {
  local line piece first_line=1 first_piece
  local -a pieces=()
  WORDS=()
  SUBCOMMANDS=()
  _word=""
  _have=0
  _drop=0
  _quote=""
  _subst=0
  _subst_buf=""
  _tick=0

  while IFS= read -r line || [[ -n "$line" ]]; do
    if ((first_line)); then
      first_line=0
    elif ((_subst > 0 || _tick)); then
      _subst_buf+=$'\n'
    elif [[ -n "$_quote" ]]; then
      _word+=$'\n' # a newline inside quotes is text, not a separator
    else
      _end_segment
    fi
    _skip_line=0
    first_piece=1
    IFS=$' \t' read -ra pieces <<<"$line"
    for piece in "${pieces[@]}"; do
      if ((first_piece)); then
        first_piece=0
      elif ((_subst > 0 || _tick)); then
        _subst_buf+=" "
      elif [[ -n "$_quote" ]]; then
        _word+=" "
      else
        _end_word
      fi
      _consume "$piece"
      ((_skip_line)) && break
    done
  done <<<"$1"
  # An unterminated substitution is still a command worth scanning.
  [[ -n "$_subst_buf" ]] && SUBCOMMANDS+=("$_subst_buf")
  _end_word
}

# Reduce a command word to its base name, so /bin/rm and C:\tools\rm.exe both
# read as rm.
BASE=""
basecmd() {
  local w=${1//\\//}
  w=${w##*/}
  w=${w%.exe}
  BASE=${w%.cmd}
}

block() {
  echo "BLOCKED: $1" >&2
  exit 2
}

# ── Rules ───────────────────────────────────────────────────────────

# Recursive rm onto a package/tool cache shared by every project on the
# machine, whether or not -f was passed.
is_shared_cache() {
  local t=${1//\\//}
  t=${t%/}
  [[ -n "$HOME" ]] && t=${t/#"$HOME"/'~'}
  t=${t/#'$HOME'/'~'}
  t=${t/#'${HOME}'/'~'}
  local cache_re='^~/(\.nuget|\.dotnet|\.templateengine|\.local/share/NuGet)(/.*)?$'
  [[ "$t" =~ $cache_re ]]
}

# The targets rm -rf may have without a prompt: build output and scratch space.
# Relative and ./-prefixed paths are allowed (./bin, src/MyApp/obj); absolute
# paths are not, except /tmp.
is_safe_target() {
  local t=${1%/}
  local safe_re='^(\./)?([^/][^[:space:]]*/)?(node_modules|bin|obj|TestResults|\.vs)$'
  [[ "$t" =~ $safe_re ]] && return 0
  [[ "$t" == /tmp || "$t" == /tmp/* ]] && return 0
  return 1
}

check_git() {
  local -a a=("$@")
  local i=0 sub="" t
  while ((i < ${#a[@]})); do
    case ${a[i]} in
      -C | -c) i=$((i + 2)) ;;
      -*) i=$((i + 1)) ;;
      *) sub=${a[i]}; break ;;
    esac
  done
  local -a args=("${a[@]:i+1}")
  case $sub in
    push)
      for t in "${args[@]}"; do
        case $t in
          --force | --force-*) block "Force push detected. Use regular push or discuss with the user first." ;;
          --*) ;;
          -*f*) block "Force push detected. Use regular push or discuss with the user first." ;;
        esac
      done
      ;;
    reset)
      for t in "${args[@]}"; do
        [[ "$t" == --hard ]] && block "git reset --hard will discard all uncommitted changes. Discuss with the user first."
      done
      ;;
    clean)
      for t in "${args[@]}"; do
        case $t in
          --force) block "git clean -f will permanently delete untracked files. Discuss with the user first." ;;
          --*) ;;
          -*f*) block "git clean -f will permanently delete untracked files. Discuss with the user first." ;;
        esac
      done
      ;;
    checkout)
      for t in "${args[@]}"; do
        [[ "$t" == "." ]] && block "git checkout . will discard all unstaged changes. Discuss with the user first."
      done
      ;;
  esac
}

check_rm() {
  local rec=0 force=0 only_targets=0 t
  local -a targets=()
  for t in "$@"; do
    if ((only_targets)); then
      targets+=("$t")
      continue
    fi
    case $t in
      --) only_targets=1 ;;
      --recursive) rec=1 ;;
      --force) force=1 ;;
      --*) ;;
      -*)
        [[ "$t" == *[rR]* ]] && rec=1
        [[ "$t" == *f* ]] && force=1
        ;;
      *) targets+=("$t") ;;
    esac
  done
  ((rec)) || return 0
  for t in "${targets[@]}"; do
    is_shared_cache "$t" &&
      block "recursive delete of the shared package cache '$t'. Every project on this machine restores from it — clear a single package or use 'dotnet nuget locals' deliberately instead."
  done
  ((force)) || return 0
  for t in "${targets[@]}"; do
    is_safe_target "$t" ||
      block "rm -rf detected in a project directory. Verify the target path is intentional."
  done
}

check_dotnet() {
  local -a a=("$@")
  local i=0 sub="" t
  while ((i < ${#a[@]})); do
    case ${a[i]} in
      -*) i=$((i + 1)) ;;
      *) sub=${a[i]}; break ;;
    esac
  done
  case $sub in
    run)
      echo "WARNING: dotnet run detected. Ensure launchSettings.json exists and the correct profile is selected."
      ;;
    build-server)
      for t in "${a[@]:i+1}"; do
        [[ "$t" == shutdown ]] &&
          block "dotnet build-server shutdown kills the MSBuild, Razor and VB/C# compiler servers for every process on this machine, not just this build. Discuss with the user first."
      done
      ;;
  esac
}

# kill is fine against a PID the agent already holds. It is not fine against a
# process it has to go looking for — on a shared machine that reaps somebody
# else's build, test run or editor.
check_kill() {
  local -a a=("$@")
  local i=0 t
  local pid_re='^[0-9]+$'
  local var_re='^\$\{?[A-Za-z_][A-Za-z0-9_]*\}?$'
  while ((i < ${#a[@]})); do
    t=${a[i]}
    i=$((i + 1))
    case $t in
      -s | --signal | -n | -q) i=$((i + 1)); continue ;;
      -*) continue ;;
      %*) continue ;;
    esac
    [[ "$t" =~ $pid_re ]] && continue
    [[ "$t" =~ $var_re ]] && continue
    block "kill targets '$t' rather than a numeric PID. On a shared machine that can reap another agent's or developer's processes — resolve the PID first and kill that."
  done
}

# Wrappers that build a command out of their own arguments. Without these the
# guard would read `xargs rm -rf .` as a run of xargs and wave it through.
check_wrapper() {
  local cmd=$1 depth=$2
  shift 2
  local -a a=("$@")
  local i=0
  case $cmd in
    xargs)
      while ((i < ${#a[@]})); do
        case ${a[i]} in
          -0 | -r | -t | -p | --null | --no-run-if-empty | --verbose) i=$((i + 1)) ;;
          -[InPsLad] | --arg-file | --replace | --max-args | --max-procs | --delimiter) i=$((i + 2)) ;;
          -*) i=$((i + 1)) ;;
          *) break ;;
        esac
      done
      ;;
    timeout)
      while ((i < ${#a[@]})); do
        case ${a[i]} in
          -s | -k | --signal | --kill-after) i=$((i + 2)) ;;
          -*) i=$((i + 1)) ;;
          *) i=$((i + 1)); break ;; # the duration
        esac
      done
      ;;
    find)
      local -a nested=()
      while ((i < ${#a[@]})); do
        case ${a[i]} in
          -exec | -execdir | -ok | -okdir)
            i=$((i + 1))
            nested=()
            while ((i < ${#a[@]})) && [[ "${a[i]}" != ';' && "${a[i]}" != '+' ]]; do
              nested+=("${a[i]}")
              i=$((i + 1))
            done
            ((${#nested[@]})) && check_segment "$depth" "${nested[@]}"
            ;;
          *) i=$((i + 1)) ;;
        esac
      done
      return 0
      ;;
  esac
  ((i < ${#a[@]})) && check_segment "$depth" "${a[@]:i}"
  return 0
}

check_segment() {
  local depth=$1
  shift
  local -a a=("$@")
  local i=0 sub_re='^[A-Za-z_][A-Za-z0-9_]*='

  # Leading VAR=value assignments are not the command.
  while ((i < ${#a[@]})) && [[ "${a[i]}" =~ $sub_re ]]; do i=$((i + 1)); done

  # Neither are the wrappers that run one.
  while ((i < ${#a[@]})); do
    basecmd "${a[i]}"
    case $BASE in
      sudo | command | env | nohup | exec | doas)
        i=$((i + 1))
        while ((i < ${#a[@]})); do
          case ${a[i]} in
            -u | -g | --user | --group | --unset) i=$((i + 2)) ;;
            -*) i=$((i + 1)) ;;
            *)
              [[ "${a[i]}" =~ $sub_re ]] || break
              i=$((i + 1))
              ;;
          esac
        done
        ;;
      *) break ;;
    esac
  done

  ((i < ${#a[@]})) || return 0
  basecmd "${a[i]}"
  local cmd=$BASE
  local -a args=("${a[@]:i+1}")

  case $cmd in
    git) check_git "${args[@]}" ;;
    rm) check_rm "${args[@]}" ;;
    dotnet) check_dotnet "${args[@]}" ;;
    kill) check_kill "${args[@]}" ;;
    xargs | timeout | find) check_wrapper "$cmd" "$depth" "${args[@]}" ;;
    pkill | killall)
      block "$cmd matches processes by name or pattern, so it reaps every match on the machine — including another agent's or developer's. Resolve the PID you mean and kill that."
      ;;
    sh | bash | zsh | dash | ksh | busybox)
      # -c takes the next word as a command string. The flag is often combined
      # (-lc, -cx, -ec), so match any short cluster containing c rather than an
      # exact "-c" — otherwise the guard is one letter away from being bypassed.
      local j=0
      while ((j < ${#args[@]})); do
        case ${args[j]} in
          --) break ;;
          --command) scan_command "${args[j + 1]}" "$((depth + 1))"; break ;;
          --*) ;;
          -*c*) scan_command "${args[j + 1]}" "$((depth + 1))"; break ;;
        esac
        j=$((j + 1))
      done
      ;;
  esac
}

scan_command() {
  local cmdline=$1 depth=${2:-0}
  [[ -z "$cmdline" ]] && return 0
  ((depth > MAX_DEPTH)) && return 0

  strip_heredocs "$cmdline"
  tokenize "$HEREDOC_STRIPPED"
  local -a words=("${WORDS[@]}")
  local -a subs=("${SUBCOMMANDS[@]}")
  local -a seg=()
  local w

  for w in "${words[@]}" "$RS"; do
    if [[ "$w" == "$RS" ]]; then
      ((${#seg[@]})) && check_segment "$depth" "${seg[@]}"
      seg=()
    else
      seg+=("$w")
    fi
  done

  # A command substitution runs a command too.
  for w in "${subs[@]}"; do
    scan_command "$w" "$((depth + 1))"
  done
}

scan_command "$COMMAND" 0

exit 0
