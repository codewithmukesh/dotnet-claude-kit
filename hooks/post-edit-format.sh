#!/usr/bin/env bash
# Post-edit hook: auto-format changed .cs files
# Runs dotnet format on specific files after Claude edits them.
#
# Usage:
#   Called automatically by Claude Code PostToolUse hook after Edit/Write on .cs files.
#   Accepts file path via:
#     1. First argument ($1)
#     2. CLAUDE_EDITED_FILE env var
#     3. PostToolUse stdin JSON ({"tool_input":{"file_path":"..."}})

set -euo pipefail

FILE="${1:-${CLAUDE_EDITED_FILE:-}}"

# Fallback: parse file_path from PostToolUse stdin JSON. Prefer jq (handles JSON
# escapes like \\ in Windows paths correctly); fall back to a sed extraction.
if [[ -z "$FILE" ]] && [[ ! -t 0 ]]; then
    STDIN=$(cat)
    if command -v jq >/dev/null 2>&1; then
        FILE=$(printf '%s' "$STDIN" | jq -r '.tool_input.file_path // empty' 2>/dev/null) || true
    fi
    if [[ -z "$FILE" ]]; then
        FILE=$(printf '%s' "$STDIN" | sed -n 's/.*"file_path"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' | head -1) || true
        # sed sees raw JSON escapes — collapse \\ to \ so the path is usable
        FILE="${FILE//\\\\/\\}"
    fi
fi

if [[ -z "$FILE" ]]; then
    exit 0
fi

# Normalize Windows backslash paths (C:\Users\... → C:/Users/...) so the
# directory walk below works under Git Bash
FILE="${FILE//\\//}"

# Only format C# files
if [[ "$FILE" != *.cs ]]; then
    exit 0
fi

# Skip if file doesn't exist (deleted)
if [[ ! -f "$FILE" ]]; then
    exit 0
fi

# Find the nearest .csproj or .sln to scope the format
DIR=$(dirname "$FILE")
PROJECT=""
while [[ "$DIR" != "/" && "$DIR" != "." ]]; do
    # Check for .csproj first (more specific)
    CSPROJ=$(find "$DIR" -maxdepth 1 -name "*.csproj" -print -quit 2>/dev/null || true)
    if [[ -n "$CSPROJ" ]]; then
        PROJECT="$CSPROJ"
        break
    fi
    # Check for .sln
    SLN=$(find "$DIR" -maxdepth 1 -name "*.sln" -print -quit 2>/dev/null || true)
    if [[ -n "$SLN" ]]; then
        PROJECT="$SLN"
        break
    fi
    # Fixed-point guard: on Windows drive roots dirname "C:" returns "C:",
    # which never equals "/" or "." — without this break the walk loops forever
    PARENT=$(dirname "$DIR")
    if [[ "$PARENT" == "$DIR" ]]; then
        break
    fi
    DIR="$PARENT"
done

if [[ -n "$PROJECT" ]]; then
    # `dotnet format --include` takes paths relative to the current directory;
    # an absolute path matches no file. The project argument in turn must be a
    # native path — the C:/... form this script builds for the directory walk
    # also makes --include match nothing. Either mistake formats nothing and
    # still exits 0, which `2>/dev/null || true` then hides.
    CWD=$(pwd)
    if command -v cygpath >/dev/null 2>&1; then
        # Git Bash: pwd is /c/..., while FILE was normalized above to C:/...
        CWD=$(cygpath -m "$CWD" 2>/dev/null || echo "$CWD")
        PROJECT=$(cygpath -w "$PROJECT" 2>/dev/null || echo "$PROJECT")
    fi
    INCLUDE="$FILE"
    case "$FILE" in
        "$CWD"/*) INCLUDE="${FILE#"$CWD"/}" ;;
    esac
    dotnet format "$PROJECT" --include "$INCLUDE" --no-restore 2>/dev/null || true
else
    echo "No .csproj or .sln found for $FILE, skipping format"
fi
