"""Real A/B: Claude Code answering the bench tasks with and without the navigator.

Each task runs in a fresh headless session (`claude -p`, stream-json) in the corpus
directory, once per arm:

- without: built-in Grep, Glob, and Read only (Claude Code's default code-search route).
- with:    the same three tools plus the navigator over MCP, and a one-paragraph system
           note telling the agent to prefer it (what the kit's rules do).

Bash, edits, web, and subagents are off in both arms. The navigator is started with the
session, and the question is sent once its log says every compilation is warm, because startup is measured
separately by the main harness. Wall time runs from sending the question to the result.

Run with an isolated CLAUDE_CONFIG_DIR (credentials only) so personal hooks, plugins,
CLAUDE.md, and memory never reach either arm. See README.md next to this file.
"""
import argparse
import json
import os
import subprocess
import sys
import threading
import time
from pathlib import Path

NAV_NOTE = (
    "This is a .NET codebase with the cwm-roslyn-navigator MCP server connected. For code "
    "navigation (finding symbols, usages, implementations, callers, type hierarchies, file "
    "outlines, the project graph, DI registrations, endpoints, stack traces), use the "
    "cwm-roslyn-navigator tools before Grep or Read: they return focused, token-efficient answers."
)

SUFFIX = " Answer concisely."


def tasks_for(targets):
    t = targets
    return [
        ("locate-type", f"Where is the {t['TypeName']} type defined? Give the file path and line."),
        ("interface-usages", f"Find all usages of {t['InterfaceName']} in this codebase. List each file and line."),
        ("implementations", f"Which classes implement {t['InterfaceName']}? List each with its file."),
        ("outline-file", f"Give me an outline of {t['LargeFile']}: the types and members it declares, with line numbers. No method bodies."),
        ("read-method", f"Show me the source code of {t['MethodType']}.{t['MethodName']}."),
        ("find-callers", f"Which methods call {t['MethodType']}.{t['MethodName']}? List each caller with its file and line."),
        ("type-hierarchy", f"Show the type hierarchy of {t['BaseClassName']}: its base types, interfaces, and every type that derives from it."),
        ("project-graph", "Describe the project dependency graph of this solution: for each project, which projects it references."),
        ("di-registrations", "List the dependency-injection service registrations in this codebase with their lifetimes."),
        ("endpoint-map", "List the HTTP endpoints this codebase exposes, with HTTP method, route, and file."),
        ("stack-trace", "Map this stack trace to the source code in this repository: for each frame in our code, give the file and line.\n\n" + t["StackTrace"]),
    ]


def run_session(args, corpus, mcp_config, arm, prompt, transcript_path):
    env = dict(os.environ, CLAUDE_CONFIG_DIR=args.config_dir)
    cmd = ["claude", "-p", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose",
           "--model", args.model, "--strict-mcp-config", "--setting-sources", "project",
           "--permission-mode", "bypassPermissions", "--no-session-persistence",
           "--max-turns", str(args.max_turns)]
    log_path = transcript_path.with_suffix(".server.log")
    log_path.unlink(missing_ok=True)  # a log left by an earlier attempt would read as already warm
    if arm == "with":
        cmd += ["--mcp-config", logged_mcp_config(mcp_config, log_path, transcript_path),
                "--tools", "Read,Grep,Glob,mcp__cwm-roslyn-navigator__*", "--append-system-prompt", NAV_NOTE]
    else:
        cmd += ["--mcp-config", args.empty_mcp, "--tools", "Read,Grep,Glob"]

    proc = subprocess.Popen(cmd, cwd=corpus["dir"], env=env, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                            stderr=subprocess.DEVNULL, text=True, encoding="utf-8", shell=(os.name == "nt"))
    lines = []
    done = threading.Event()

    def reader():
        for line in proc.stdout:
            lines.append(line)
            if '"type":"result"' in line:
                done.set()
        done.set()

    threading.Thread(target=reader, daemon=True).start()
    warm_wait = None
    if arm == "with":
        # Startup is measured by the main harness. Send the question only once the server
        # has logged that every compilation is warm, so no answer is a "loading" reply.
        waited = time.time()
        while time.time() - waited < args.warm_timeout_s:
            if log_path.exists() and "All compilations warmed" in log_path.read_text(encoding="utf-8", errors="ignore"):
                break
            time.sleep(0.5)
        warm_wait = round(time.time() - waited, 1)

    message = {"type": "user", "message": {"role": "user", "content": prompt + SUFFIX}}
    start = time.time()
    proc.stdin.write(json.dumps(message) + "\n")
    proc.stdin.flush()
    finished = done.wait(timeout=args.timeout_s)
    wall = time.time() - start
    try:
        proc.stdin.close()
    except OSError:
        pass
    if not finished:
        kill_tree(proc)
    try:
        proc.wait(timeout=60)
    except subprocess.TimeoutExpired:
        kill_tree(proc)

    transcript_path.write_text("".join(lines), encoding="utf-8")
    row = summarize(lines, wall, finished)
    if warm_wait is not None:
        row["warm_wait_s"] = warm_wait
        row["navigator_loading_replies"] = "".join(lines).count('\\"State\\":\\"Loading\\"')
    return row


def logged_mcp_config(mcp_config, log_path, transcript_path):
    """The corpus MCP config, with the server's stderr log redirected to a file we can watch."""
    config = json.loads(Path(mcp_config).read_text(encoding="utf-8"))
    server = config["mcpServers"]["cwm-roslyn-navigator"]
    wrapper = str(Path(__file__).with_name("stderr_to_file.py"))
    server["args"] = [wrapper, str(log_path), server["command"], *server["args"]]
    server["command"] = sys.executable
    path = transcript_path.with_suffix(".mcp.json")
    path.write_text(json.dumps(config), encoding="utf-8")
    return str(path)


def kill_tree(proc):
    if os.name == "nt":
        subprocess.run(["taskkill", "/F", "/T", "/PID", str(proc.pid)], capture_output=True)
    else:
        proc.kill()


def summarize(lines, wall, finished):
    tools = {}
    result = None
    for line in lines:
        try:
            event = json.loads(line)
        except json.JSONDecodeError:
            continue
        if event.get("type") == "assistant":
            for block in event["message"].get("content", []):
                if block.get("type") == "tool_use":
                    tools[block["name"]] = tools.get(block["name"], 0) + 1
        elif event.get("type") == "result":
            result = event

    row = {"wall_s": round(wall, 1), "finished": finished, "tool_calls": sum(tools.values()), "tools": tools}
    if result:
        usage = result.get("usage") or {}
        row.update({
            # An API or auth failure still arrives as subtype "success", with is_error set.
            "subtype": "error" if result.get("is_error") else result.get("subtype"),
            "num_turns": result.get("num_turns"),
            "duration_api_s": round((result.get("duration_api_ms") or 0) / 1000, 1),
            "cost_usd": round(result.get("total_cost_usd") or 0, 4),
            "input_tokens": usage.get("input_tokens", 0),
            "cache_creation_tokens": usage.get("cache_creation_input_tokens", 0),
            "cache_read_tokens": usage.get("cache_read_input_tokens", 0),
            "output_tokens": usage.get("output_tokens", 0),
            "answer": result.get("result", ""),
        })
        row["total_input_tokens"] = row["input_tokens"] + row["cache_creation_tokens"] + row["cache_read_tokens"]
    else:
        row["subtype"] = "no-result"
    return row


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--corpus-root", required=True)
    ap.add_argument("--corpus-defs", required=True, help="bench/corpus directory")
    ap.add_argument("--corpus", action="append", required=True, help="corpus name, e.g. jellyfin")
    ap.add_argument("--mcp-dir", required=True, help="directory with mcp-<CloneDirectory>.json files")
    ap.add_argument("--config-dir", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--transcripts", required=True)
    ap.add_argument("--model", default="sonnet")
    ap.add_argument("--max-turns", type=int, default=80)
    ap.add_argument("--timeout-s", type=int, default=900)
    ap.add_argument("--only-task", action="append")
    ap.add_argument("--warm-timeout-s", type=int, default=600)
    ap.add_argument("--only-arm", choices=["with", "without"])
    args = ap.parse_args()
    args.empty_mcp = str(Path(args.mcp_dir) / "mcp-none.json")

    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    results_path = out / "results.json"
    results = json.loads(results_path.read_text(encoding="utf-8")) if results_path.exists() else []
    results = [r for r in results if r.get("subtype") == "success"]
    done = {(r["corpus"], r["task"], r["arm"]) for r in results}

    for spec in args.corpus:
        name = spec
        definition = json.loads((Path(args.corpus_defs) / f"{name}.json").read_text(encoding="utf-8"))
        clone = definition["CloneDirectory"]
        corpus = {"dir": str(Path(args.corpus_root) / clone)}
        mcp_config = str(Path(args.mcp_dir) / f"mcp-{clone}.json")
        transcripts = Path(args.transcripts) / name
        transcripts.mkdir(parents=True, exist_ok=True)

        # Warm the prompt cache for both arms so neither pays the first-session cache write.
        for arm in ("without", "with"):
            run_session(args, corpus, mcp_config, arm, "Reply with the single word ready.", transcripts / f"_warmup-{arm}.jsonl")

        for index, (task, prompt) in enumerate(tasks_for(definition["Targets"])):
            if args.only_task and task not in args.only_task:
                continue
            arms = ("without", "with") if index % 2 == 0 else ("with", "without")
            for arm in arms:
                if args.only_arm and arm != args.only_arm:
                    continue
                if (name, task, arm) in done:
                    continue
                row = run_session(args, corpus, mcp_config, arm, prompt, transcripts / f"{task}-{arm}.jsonl")
                row.update({"corpus": name, "tier": definition["Tier"], "task": task, "arm": arm, "prompt": prompt, "model": args.model})
                results = [r for r in results if (r["corpus"], r["task"], r["arm"]) != (name, task, arm)] + [row]
                results_path.write_text(json.dumps(results, indent=1), encoding="utf-8")
                print(f"[{name}] {task:17} {arm:7} {row.get('subtype')!s:18} {row['wall_s']:7.1f}s "
                      f"calls={row['tool_calls']:3} in={row.get('total_input_tokens', 0):>9,} ${row.get('cost_usd', 0)}", flush=True)


if __name__ == "__main__":
    sys.exit(main())
