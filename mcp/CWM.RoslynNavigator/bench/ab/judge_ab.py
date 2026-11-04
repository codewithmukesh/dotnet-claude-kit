"""Blinded accuracy check for the A/B answers.

For every task, a fresh `claude -p` session (no tools) sees the question, a reference, and
the two answers labelled A and B in a random order. The reference is the navigator's raw
tool output from the with-navigator transcript: Roslyn's compiler-backed answer, which
can be capped, so the judge is told not to penalise correct items it lacks.
"""
import argparse
import json
import os
import random
import re
import subprocess
from pathlib import Path

JUDGE = """You are grading two answers to a question about a .NET codebase.

Question:
{question}

Reference (raw output of a compiler-backed code-navigation tool; it can be capped at a
result limit, so an answer may correctly list items the reference lacks):
<reference>
{reference}
</reference>

<answer_a>
{a}
</answer_a>

<answer_b>
{b}
</answer_b>

Score each answer from 0 to 100 for how correct and complete it is as an answer to the
question. Wrong files, lines, or members cost points; missing items cost points in
proportion to how much of the answer they are. Formatting and length do not matter.
Reply with JSON only: {{"a": <score>, "b": <score>, "reason": "<one sentence>"}}"""


def reference_from(transcript):
    parts = []
    for line in transcript.read_text(encoding="utf-8").splitlines():
        try:
            event = json.loads(line)
        except json.JSONDecodeError:
            continue
        if event.get("type") != "user":
            continue
        for block in event["message"].get("content", []) if isinstance(event["message"].get("content"), list) else []:
            if block.get("type") == "tool_result":
                content = block.get("content")
                text = content if isinstance(content, str) else "".join(c.get("text", "") for c in content or [])
                parts.append(text)
    return "\n---\n".join(parts)[:40000]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--results", required=True)
    ap.add_argument("--transcripts", required=True)
    ap.add_argument("--config-dir", required=True)
    ap.add_argument("--model", default="sonnet")
    args = ap.parse_args()

    results = json.loads(Path(args.results).read_text(encoding="utf-8"))
    by_key = {(r["corpus"], r["task"], r["arm"]): r for r in results}
    env = dict(os.environ, CLAUDE_CONFIG_DIR=args.config_dir)
    rng = random.Random(42)

    for (corpus, task, arm), row in sorted(by_key.items()):
        if arm != "with" or "score" in row:
            continue
        other = by_key.get((corpus, task, "without"))
        if not other:
            continue
        reference = reference_from(Path(args.transcripts) / corpus / f"{task}-with.jsonl") or "(none: the tool was not called)"
        flip = rng.random() < 0.5
        a, b = (other, row) if flip else (row, other)
        prompt = JUDGE.format(question=row["prompt"], reference=reference, a=a.get("answer", ""), b=b.get("answer", ""))
        verdict = None
        for _ in range(3):
            verdict = ask(args, env, prompt)
            if verdict:
                break
        if not verdict:
            print(f"[{corpus}] {task}: judge gave no usable JSON after 3 tries")
            continue
        score_with, score_without = (verdict["b"], verdict["a"]) if flip else (verdict["a"], verdict["b"])
        row["score"], other["score"] = score_with, score_without
        row["judge_reason"] = other["judge_reason"] = verdict.get("reason", "")
        Path(args.results).write_text(json.dumps(results, indent=1), encoding="utf-8")
        print(f"[{corpus}] {task:17} with={score_with:3} without={score_without:3}  {verdict.get('reason', '')}", flush=True)


def ask(args, env, prompt):
    out = subprocess.run(["claude", "-p", "--model", args.model, "--tools", "", "--strict-mcp-config",
                          "--setting-sources", "project", "--no-session-persistence"],
                         input=prompt, capture_output=True, text=True, encoding="utf-8", env=env,
                         shell=(os.name == "nt"), timeout=600)
    match = re.search(r"\{.*\}", out.stdout, re.S)
    if not match:
        return None
    try:
        verdict = json.loads(match.group(0))
    except json.JSONDecodeError:
        return None
    return verdict if isinstance(verdict.get("a"), (int, float)) and isinstance(verdict.get("b"), (int, float)) else None


if __name__ == "__main__":
    main()
