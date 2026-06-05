#!/usr/bin/env bash
# Post-test hook: analyze test results and output actionable summary
# Parses dotnet test output for failures and provides a structured report.
#
# Usage: Pipe dotnet test output or pass log file as argument.
# Called automatically after test runs to summarize results.

set -euo pipefail

LOG_FILE="${1:-}"
TEST_OUTPUT=""

if [[ -n "$LOG_FILE" && -f "$LOG_FILE" ]]; then
    TEST_OUTPUT=$(cat "$LOG_FILE")
else
    # Read from stdin if available
    if [[ ! -t 0 ]]; then
        TEST_OUTPUT=$(cat)
    else
        echo "Usage: dotnet test 2>&1 | bash hooks/post-test-analyze.sh"
        echo "   or: bash hooks/post-test-analyze.sh <test-output.log>"
        exit 0
    fi
fi

# Count results by summing the numeric Passed/Failed/Skipped values from each test
# project's summary line, e.g.:
#   Passed!  - Failed:     0, Passed:    86, Skipped:     0, Total:    86, Duration: ...
# The previous approach counted occurrences of the words "Passed!"/"Failed!", which only
# counts summary lines (≈ test projects), not tests — and "grep -c ... || echo 0"
# double-appended a value under this script's 'set -euo pipefail'.
sum_metric() {
    printf '%s\n' "$TEST_OUTPUT" \
        | { grep -oE "$1:[[:space:]]*[0-9]+" || true; } \
        | { grep -oE '[0-9]+' || true; } \
        | awk '{ s += $1 } END { print s + 0 }'
}
PASSED=$(sum_metric Passed)
FAILED=$(sum_metric Failed)
SKIPPED=$(sum_metric Skipped)

# Extract failure details
FAILURES=$(echo "$TEST_OUTPUT" | grep -A 5 'Failed ' 2>/dev/null || true)

echo ""
echo "═══════════════════════════════════"
echo "  Test Results Summary"
echo "═══════════════════════════════════"
echo ""

if [[ "$FAILED" -gt 0 ]]; then
    echo "  🔴 FAILED: $FAILED"
    echo "  ✅ Passed: $PASSED"
    echo "  ⏭️  Skipped: $SKIPPED"
    echo ""
    echo "  Failed Tests:"
    echo "  ─────────────"
    echo "$FAILURES" | head -50
    echo ""
    echo "  Next Steps:"
    echo "  1. Fix the failing tests above"
    echo "  2. Run 'dotnet test' to verify fixes"
    echo "  3. Check test output for root cause details"
else
    echo "  ✅ All $PASSED test(s) passed"
    if [[ "$SKIPPED" -gt 0 ]]; then
        echo "  ⏭️  $SKIPPED test(s) skipped"
    fi
fi

echo ""
echo "═══════════════════════════════════"
