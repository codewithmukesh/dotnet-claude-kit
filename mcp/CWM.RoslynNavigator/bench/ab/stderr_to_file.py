"""Runs a command with stdin/stdout passed through and stderr written to a file.

Used as the MCP server command so the A/B runner can watch the navigator's log without
touching the stdio channel MCP uses: python stderr_to_file.py <log> <command> [args...]
"""
import subprocess
import sys

with open(sys.argv[1], "w", encoding="utf-8") as log:
    sys.exit(subprocess.call(sys.argv[2:], stderr=log))
