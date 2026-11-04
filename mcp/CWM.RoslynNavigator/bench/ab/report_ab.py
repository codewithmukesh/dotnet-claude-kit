"""Renders results.json from run_ab.py (and judge_ab.py) as a markdown report."""
import argparse
import json
from pathlib import Path
from statistics import median

ORDER = ["clean-architecture", "eshop", "jellyfin", "orchardcore", "abp"]


def total(rows, key):
    return sum(r.get(key) or 0 for r in rows)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--results", required=True)
    ap.add_argument("--out", required=True)
    args = ap.parse_args()
    results = json.loads(Path(args.results).read_text(encoding="utf-8"))
    corpora = [c for c in ORDER if any(r["corpus"] == c for r in results)]
    model = results[0].get("model", "?") if results else "?"

    md = ["# Claude Code with and without the navigator: real A/B", "",
          f"Every task ran as a fresh headless Claude Code session (`claude -p`, model `{model}`), once per arm, "
          "with the same prompt. **without** = built-in Grep, Glob, and Read. **with** = the same tools plus the "
          "navigator and a one-paragraph note to prefer it. Bash, edits, web, and subagents were off in both arms, "
          "and personal hooks, plugins, CLAUDE.md, and memory were isolated away. Wall time runs from sending the "
          "question to the final answer and includes every model round trip. Navigator startup is excluded (it "
          "warmed before the question was sent) and is reported by the main benchmark. Input tokens are what the "
          "API processed, including cache reads, tool definitions, and the system prompt. Accuracy is a blinded "
          "0–100 score from a separate judge session against the navigator's raw output.", "",
          "## Summary (all tasks per solution)", "",
          "| Tier | Solution | Arm | Wall time (sum) | Median per task | Tool calls | Input tokens | Output tokens | Cost (USD) | Avg accuracy | Failed |",
          "|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|"]
    for c in corpora:
        for arm in ("without", "with"):
            rows = [r for r in results if r["corpus"] == c and r["arm"] == arm]
            scores = [r["score"] for r in rows if "score" in r]
            failed = sum(1 for r in rows if r.get("subtype") != "success")
            md.append(f"| {rows[0]['tier']} | {c} | {arm} | {total(rows, 'wall_s'):,.0f} s | {median(r['wall_s'] for r in rows):.1f} s | "
                      f"{total(rows, 'tool_calls'):,} | {total(rows, 'total_input_tokens'):,} | {total(rows, 'output_tokens'):,} | "
                      f"{total(rows, 'cost_usd'):.2f} | {(sum(scores) / len(scores)) if scores else float('nan'):.0f} | {failed} |")

    for c in corpora:
        md += ["", f"## {c}", "",
               "| Task | Wall s (without → with) | Tool calls | Input tokens | Cost USD | Accuracy | Navigator tools used |",
               "|---|---:|---:|---:|---:|---:|---|"]
        tasks = [r["task"] for r in results if r["corpus"] == c and r["arm"] == "with"]
        for t in tasks:
            w = next((r for r in results if (r["corpus"], r["task"], r["arm"]) == (c, t, "with")), {})
            o = next((r for r in results if (r["corpus"], r["task"], r["arm"]) == (c, t, "without")), {})
            nav = ", ".join(f"{k.split('__')[-1]}×{v}" for k, v in (w.get("tools") or {}).items() if k.startswith("mcp__")) or "none"
            md.append(f"| {t} | {o.get('wall_s', 0):.1f} → {w.get('wall_s', 0):.1f} | {o.get('tool_calls', 0)} → {w.get('tool_calls', 0)} | "
                      f"{o.get('total_input_tokens', 0):,} → {w.get('total_input_tokens', 0):,} | {o.get('cost_usd', 0):.3f} → {w.get('cost_usd', 0):.3f} | "
                      f"{o.get('score', '–')} → {w.get('score', '–')} | {nav} |")

    Path(args.out).write_text("\n".join(md) + "\n", encoding="utf-8")
    print("\n".join(md[:8 + 2 * len(corpora) + 3]))


if __name__ == "__main__":
    main()
