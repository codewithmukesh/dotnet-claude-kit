#!/usr/bin/env bash
# Post-edit hook: auto-format changed .cs files
# Runs dotnet format on specific files after Claude edits them, or CSharpier
# when the repository uses it.
#
# Usage:
#   Called automatically by Claude Code PostToolUse hook after Edit/Write on .cs files.
#   Accepts file path via:
#     1. First argument ($1)
#     2. CLAUDE_EDITED_FILE env var
#     3. PostToolUse stdin JSON ({"tool_input":{"file_path":"..."}})
#
#   CWM_POST_EDIT_FORMAT=off disables the hook. Claude Code cannot turn off a
#   single plugin hook, so without this the only way out is disabling the plugin.

set -euo pipefail

if [[ "${CWM_POST_EDIT_FORMAT:-}" == "off" ]]; then
    exit 0
fi

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

# A repository that uses CSharpier gets CSharpier, never dotnet format: the two
# disagree on indentation in places, so running both makes each undo the other
# and `dotnet csharpier check` fails on files only this hook touched. CSharpier
# is detected the way it resolves its own config and local tool, walking up
# from the edited file.
uses_csharpier() {
    local dir
    dir=$(dirname "$FILE")
    while true; do
        if [[ -f "$dir/.csharpierrc" || -f "$dir/.csharpierrc.json" \
            || -f "$dir/.csharpierrc.yaml" || -f "$dir/.csharpierrc.yml" ]]; then
            return 0
        fi
        # .NET 10 writes new tool manifests to the root, older SDKs to .config/
        local manifest
        for manifest in "$dir/dotnet-tools.json" "$dir/.config/dotnet-tools.json"; do
            if [[ -f "$manifest" ]] && grep -qi '"csharpier"' "$manifest" 2>/dev/null; then
                return 0
            fi
        done
        # Same drive-root fixed-point guard as the project walk below
        local parent
        parent=$(dirname "$dir")
        if [[ "$parent" == "$dir" || "$dir" == "." ]]; then
            return 1
        fi
        dir="$parent"
    done
}

if uses_csharpier; then
    # Run from the file's directory so `dotnet csharpier` resolves the
    # repository's local tool manifest even when the hook's cwd is elsewhere.
    # Exit 0 on failure for the same reason as dotnet format below.
    if ! FORMAT_OUTPUT=$(cd "$(dirname "$FILE")" && dotnet csharpier format "$FILE" 2>&1); then
        echo "post-edit-format: dotnet csharpier format failed for $FILE (file left unformatted)"
        echo "$FORMAT_OUTPUT"
    fi
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
    # Report a failed format instead of discarding it — that is what kept the
    # path bug above invisible. Still exit 0: mid-edit files routinely fail to
    # compile, and a hook that fails there would interrupt an ordinary edit.
    if ! FORMAT_OUTPUT=$(dotnet format "$PROJECT" --include "$INCLUDE" --no-restore 2>&1); then
        echo "post-edit-format: dotnet format failed for $INCLUDE (file left unformatted)"
        echo "$FORMAT_OUTPUT"
    fi
else
    echo "No .csproj or .sln found for $FILE, skipping format"
fi
