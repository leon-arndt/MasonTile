#!/usr/bin/env python3
"""Unity Agent Bench: measure how well an agent completes creator tasks with a package's [AgentTool]s.

  replay    Tier 1. Run every task's reference solution in the Editor and check it. No model, a few seconds.
  run       Tier 2. Give each task to Claude Code (headless) with only the suite's tools, then check the result.
  report    Summarise a results file.
  compare   Compare two results files (e.g. main vs. your change) task by task.
  list      List suites and tasks.

Needs a running Editor with the project open and the Pipeline server up. Standard library only.
"""

import argparse
import glob
import hashlib
import json
import os
import shutil
import statistics
import subprocess
import sys
import threading
import time
from datetime import datetime, timezone
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.dont_write_bytecode = True  # keep __pycache__ out of the package folder
sys.path.insert(0, str(HERE))
from editor_client import Editor, EditorError, EditorUnreachable, find_project  # noqa: E402

MCP_SERVER_NAME = "bench"


# ---------------------------------------------------------------- helpers

def find_claude(explicit: str | None) -> str:
    candidates = [explicit, os.environ.get("AGENTBENCH_CLAUDE"), shutil.which("claude")]
    vscode = sorted(glob.glob(os.path.expanduser("~/.vscode/extensions/anthropic.claude-code-*/resources/native-binary/claude")))
    candidates += reversed(vscode)
    for c in candidates:
        if c and Path(c).is_file() and os.access(c, os.X_OK):
            return c
    raise SystemExit("Claude Code not found. Put `claude` on PATH, set AGENTBENCH_CLAUDE, or pass --claude.")


def git_info(project: Path) -> dict:
    def git(*a):
        r = subprocess.run(["git", *a], cwd=project, capture_output=True, text=True)
        return r.stdout.strip() if r.returncode == 0 else None

    diff = git("diff", "HEAD", "--", "Packages") or ""
    return {
        "commit": git("rev-parse", "--short", "HEAD"),
        "branch": git("rev-parse", "--abbrev-ref", "HEAD"),
        "packagesDirty": bool(diff),
        "packagesDiffSha": hashlib.sha256(diff.encode()).hexdigest()[:12] if diff else None,
    }


def tools_fingerprint(tools: list) -> str:
    return hashlib.sha256(json.dumps(tools, sort_keys=True).encode()).hexdigest()[:12]


def select_tasks(editor: Editor, suite: str, wanted: list[str] | None, tags: list[str] | None) -> list[dict]:
    suites = {s["suite"]: s for s in editor.exec("agentbench list")}
    if suite not in suites:
        raise SystemExit(f"No suite '{suite}'. Suites: {', '.join(suites)}")
    tasks = suites[suite]["tasks"]
    if wanted:
        ids = {w if "/" in w else f"{suite}/{w}" for w in wanted}
        missing = ids - {t["id"] for t in tasks}
        if missing:
            raise SystemExit(f"Unknown task(s): {', '.join(sorted(missing))}")
        tasks = [t for t in tasks if t["id"] in ids]
    if tags:
        tasks = [t for t in tasks if set(tags) & set(t["tags"])]
    return tasks


def parse_transcript(path: Path) -> dict:
    """Pulls the init status and the final result event out of Claude Code's stream-json output."""
    init, result, last_text = None, None, None
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
        try:
            ev = json.loads(line)
        except ValueError:
            continue
        if ev.get("type") == "system" and ev.get("subtype") == "init":
            init = ev
        elif ev.get("type") == "result":
            result = ev
        elif ev.get("type") == "assistant":
            for block in ev.get("message", {}).get("content", []):
                if block.get("type") == "text":
                    last_text = block["text"]
    return {"init": init, "result": result, "last_text": last_text}


def read_calls(path: Path) -> list[dict]:
    if not path.exists():
        return []
    return [json.loads(l) for l in path.read_text(encoding="utf-8").splitlines() if l.strip()]


# ---------------------------------------------------------------- commands

def cmd_list(args, editor):
    for s in editor.exec("agentbench list"):
        print(f"{s['suite']}  (tools: {s['toolPrefix']}*)")
        for t in s["tasks"]:
            print(f"  {t['id']:<34} [{', '.join(t['tags'])}]")
            if args.prompts:
                print("      " + t["prompt"].replace("\n", "\n      "))


def cmd_replay(args, editor):
    r = editor.exec("agentbench replay", {"task": args.task}, timeout=600)
    print(f"Tier 1 replay: {r['passed']}/{r['total']} reference solutions pass\n")
    print(f"{'task':<34} {'result':<6} {'calls':>5} {'resp chars':>10}")
    for t in r["tasks"]:
        print(f"{t['task']:<34} {'PASS' if t['passed'] else 'FAIL':<6} {t['calls']:>5} {t['responseChars']:>10}")
        for f in t["failures"]:
            print(f"    - {f}")
    if args.json:
        Path(args.json).write_text(json.dumps(r, indent=2))
    return 0 if r["passed"] == r["total"] else 1


def forward_transcript(stream, out, editor):
    """Copies Claude Code's stream-json to the transcript file and shows each thing the agent says in the
    Agent Bench window (best effort: a missed note never affects the trial)."""
    for line in stream:
        out.write(line)
        out.flush()
        try:
            ev = json.loads(line)
        except ValueError:
            continue
        if ev.get("type") != "assistant":
            continue
        for block in ev.get("message", {}).get("content", []):
            text = (block.get("text") or "").strip() if block.get("type") == "text" else ""
            if text:
                try:
                    editor.exec("agentbench note", {"text": text[:400]}, timeout=5, retries=1)
                except (EditorError, EditorUnreachable):
                    pass


def run_trial(args, editor, claude, task, trial, tools_file, trial_dir, system_prompt) -> dict:
    trial_dir.mkdir(parents=True, exist_ok=True)
    workdir = trial_dir / "cwd"  # empty: no project CLAUDE.md, settings or files for the agent to find
    workdir.mkdir(exist_ok=True)
    calls_log = trial_dir / "calls.jsonl"
    transcript = trial_dir / "transcript.jsonl"
    mcp_config = trial_dir / "mcp.json"
    mcp_config.write_text(json.dumps({"mcpServers": {MCP_SERVER_NAME: {
        "type": "stdio",
        "command": sys.executable,
        "args": [str(HERE / "mcp_server.py"), "--project", str(args.project), "--tools-file", str(tools_file), "--log", str(calls_log)],
    }}}, indent=2))

    record = {"task": task["id"], "trial": trial, "status": "ok", "dir": str(trial_dir)}
    started = time.time()
    try:
        # The previous trial's scene is replaced here, so the user's scene only comes back at the end of the run.
        setup = editor.exec("agentbench setup", {"task": task["id"], "agent": f"Claude Code · {args.model}"})
        cmd = [
            claude, "-p", setup["prompt"],
            "--model", args.model,
            "--output-format", "stream-json", "--verbose",
            "--system-prompt", system_prompt or setup["systemPrompt"],
            "--tools", "",                                  # no built-in tools: only the suite's MCP tools
            "--strict-mcp-config", "--mcp-config", str(mcp_config),
            "--allowedTools", f"mcp__{MCP_SERVER_NAME}",    # pre-approve them (no one is there to click Allow)
            "--setting-sources", "project",                 # the empty cwd has none, so user hooks/plugins stay out
            "--no-session-persistence",
        ]
        if args.max_budget_usd:
            cmd += ["--max-budget-usd", str(args.max_budget_usd)]
        with open(transcript, "w", encoding="utf-8") as out, open(trial_dir / "stderr.txt", "w") as err:
            proc = subprocess.Popen(cmd, cwd=workdir, stdout=subprocess.PIPE, stderr=err, stdin=subprocess.DEVNULL, text=True, encoding="utf-8")
            reader = threading.Thread(target=forward_transcript, args=(proc.stdout, out, editor), daemon=True)
            reader.start()
            try:
                record["exitCode"] = proc.wait(timeout=args.timeout)
            except subprocess.TimeoutExpired:
                proc.kill()
                proc.wait()
                record["status"] = "timeout"
            reader.join(timeout=10)

        check = editor.exec("agentbench check", {"task": task["id"]})
        record.update(passed=check["Passed"], score=check["Score"],
                      failures=[f"{i['Expectation']}: {i['Detail']}" for i in check["Items"] if not i["Passed"]])
        record["seconds"] = round(time.time() - started, 1)  # before --hold, so watching doesn't skew timings
        if args.hold:
            time.sleep(args.hold)  # leave the result on screen (Agent Bench window, Scene view) before the next task
    except (EditorError, EditorUnreachable) as e:
        record.update(status="error", error=str(e), passed=False, score=0.0, failures=[])

    t = parse_transcript(transcript) if transcript.exists() else {"init": None, "result": None, "last_text": None}
    if t["init"]:
        servers = {s.get("name"): s.get("status") for s in t["init"].get("mcp_servers", [])}
        if servers.get(MCP_SERVER_NAME) != "connected":
            record.update(status="error", error=f"MCP server not connected: {servers}")
    res = t["result"] or {}
    usage = res.get("usage") or {}
    calls = read_calls(calls_log)
    if any(c.get("infra") for c in calls):
        record.update(status="error", error="a tool call could not reach the Editor")
    record.update(
        toolCalls=len(calls),
        toolErrors=sum(1 for c in calls if not c["ok"]),
        responseChars=sum(c["chars"] for c in calls),
        toolsUsed=[c["tool"] for c in calls],
        turns=res.get("num_turns"),
        inputTokens=(usage.get("input_tokens") or 0) + (usage.get("cache_read_input_tokens") or 0) + (usage.get("cache_creation_input_tokens") or 0),
        outputTokens=usage.get("output_tokens"),
        costUsd=res.get("total_cost_usd"),
        agentError=res.get("is_error"),
        stopReason=res.get("subtype"),
        finalMessage=(res.get("result") or t["last_text"] or "")[:500],
        seconds=record.get("seconds") or round(time.time() - started, 1),
    )
    if not res and record["status"] == "ok":
        record.update(status="error", error="Claude Code produced no result; see stderr.txt")
    return record


def cmd_run(args, editor):
    claude = find_claude(args.claude)
    version = subprocess.run([claude, "--version"], capture_output=True, text=True).stdout.strip()
    tasks = select_tasks(editor, args.suite, args.task, args.tag)
    tools = editor.exec("agentbench tools", {"suite": args.suite})

    stamp = datetime.now(timezone.utc).strftime("%Y%m%d-%H%M%S")
    label = args.label or f"{git_info(args.project)['commit']}-{args.model}"
    run_dir = Path(args.out) / f"{stamp}-{label}"
    run_dir.mkdir(parents=True, exist_ok=True)
    tools_file = run_dir / "tools.json"
    tools_file.write_text(json.dumps(tools, indent=2))

    meta = {
        "label": label, "created": stamp, "suite": args.suite, "model": args.model, "claudeCode": version,
        "trialsPerTask": args.trials, "git": git_info(args.project), "toolsFingerprint": tools_fingerprint(tools["tools"]),
    }
    print(f"Run {run_dir.name}: {len(tasks)} task(s) x {args.trials} trial(s), model {args.model}, {version}")

    trials = []
    try:
        run_all(args, editor, claude, tasks, tools, tools_file, run_dir, meta, trials)
    finally:
        try:
            editor.exec("agentbench teardown")
        except (EditorError, EditorUnreachable) as e:
            print(f"warning: teardown failed: {e}", file=sys.stderr)

    print()
    summarise({"meta": meta, "trials": trials})
    print(f"\nResults: {run_dir / 'results.json'}")
    return 0


def run_all(args, editor, claude, tasks, tools, tools_file, run_dir, meta, trials):
    for task in tasks:
        for i in range(args.trials):
            r = run_trial(args, editor, claude, task, i, tools_file, run_dir / task["id"].replace("/", "__") / f"trial{i}", args.system_prompt)
            trials.append(r)
            mark = {"ok": "PASS" if r.get("passed") else "FAIL"}.get(r["status"], r["status"].upper())
            print(f"  {mark:<7} {task['id']:<34} calls {r['toolCalls']:>2} (err {r['toolErrors']}) "
                  f"turns {r['turns'] or '-':>2}  {r['seconds']:>5}s  ${r['costUsd'] or 0:.3f}"
                  + (f"  {r.get('error')}" if r['status'] == 'error' else ""))
            for f in r.get("failures", []):
                print(f"          - {f}")
            # Write after every trial so an interrupted run still leaves results.
            (run_dir / "results.json").write_text(json.dumps({"meta": meta, "tools": tools["tools"], "trials": trials}, indent=2))


def aggregate(trials: list[dict]) -> dict:
    """Per-task stats. Trials with status 'error' (harness or Editor problems) are left out and counted."""
    by_task = {}
    for t in trials:
        by_task.setdefault(t["task"], []).append(t)
    out = {}
    for task, ts in by_task.items():
        valid = [t for t in ts if t["status"] != "error"]
        mean = lambda k: statistics.fmean([t[k] or 0 for t in valid]) if valid else None  # noqa: E731
        out[task] = {
            "n": len(valid), "errors": len(ts) - len(valid),
            "passRate": statistics.fmean([1.0 if t["passed"] else 0.0 for t in valid]) if valid else None,
            "score": mean("score"), "toolCalls": mean("toolCalls"), "toolErrors": mean("toolErrors"),
            "responseChars": mean("responseChars"), "inputTokens": mean("inputTokens"),
            "outputTokens": mean("outputTokens"), "costUsd": mean("costUsd"), "seconds": mean("seconds"),
        }
    return out


def fmt(v, spec=".2f"):
    return "-" if v is None else format(v, spec)


def summarise(results: dict):
    agg = aggregate(results["trials"])
    m = results["meta"]
    print(f"{m['label']}  model={m['model']}  commit={m['git']['commit']}{' +dirty' if m['git']['packagesDirty'] else ''}  tools={m['toolsFingerprint']}")
    print(f"{'task':<34} {'pass':>6} {'score':>6} {'calls':>6} {'errs':>5} {'in tok':>8} {'out tok':>8} {'cost $':>7}")
    for task, a in agg.items():
        n = f"{round((a['passRate'] or 0) * a['n'])}/{a['n']}" + ("!" if a["errors"] else "")
        print(f"{task:<34} {n:>6} {fmt(a['score'])} {fmt(a['toolCalls'], '6.1f')} {fmt(a['toolErrors'], '5.1f')} "
              f"{fmt(a['inputTokens'], '8.0f')} {fmt(a['outputTokens'], '8.0f')} {fmt(a['costUsd'], '7.3f')}")
    valid = [t for t in results["trials"] if t["status"] != "error"]
    if valid:
        passed = f"{sum(t['passed'] for t in valid)}/{len(valid)}"
        print(f"{'overall':<34} {passed:>6} "
              f"{fmt(statistics.fmean(t['score'] for t in valid))} "
              f"{fmt(statistics.fmean(t['toolCalls'] for t in valid), '6.1f')} "
              f"{fmt(statistics.fmean(t['toolErrors'] for t in valid), '5.1f')}  total ${sum(t['costUsd'] or 0 for t in valid):.2f}")
    errored = len(results["trials"]) - len(valid)
    if errored:
        print(f"! {errored} trial(s) errored (harness or Editor) and are excluded; see 'error' in results.json")


def load(path: str) -> dict:
    p = Path(path)
    return json.loads((p / "results.json" if p.is_dir() else p).read_text())


def cmd_report(args, _editor):
    summarise(load(args.results))
    return 0


def cmd_compare(args, _editor):
    base, cand = load(args.baseline), load(args.candidate)
    a, b = aggregate(base["trials"]), aggregate(cand["trials"])
    print(f"baseline : {base['meta']['label']} ({base['meta']['git']['commit']}, tools {base['meta']['toolsFingerprint']})")
    print(f"candidate: {cand['meta']['label']} ({cand['meta']['git']['commit']}, tools {cand['meta']['toolsFingerprint']})")

    if "tools" in base and "tools" in cand:
        bt, ct = {t["id"]: t for t in base["tools"]}, {t["id"]: t for t in cand["tools"]}
        changed = [i for i in sorted(set(bt) | set(ct)) if json.dumps(bt.get(i), sort_keys=True) != json.dumps(ct.get(i), sort_keys=True)]
        if changed:
            print("tool definitions changed: " + ", ".join(
                f"{i} ({'added' if i not in bt else 'removed' if i not in ct else 'edited'})" for i in changed))
    if base["meta"]["model"] != cand["meta"]["model"]:
        print(f"note: different models ({base['meta']['model']} vs {cand['meta']['model']})")

    print(f"\n{'task':<34} {'pass':>13} {'calls':>13} {'tool errs':>11} {'in tokens':>17}")
    regressions = []
    for task in sorted(set(a) | set(b)):
        x, y = a.get(task), b.get(task)
        if not x or not y or x["passRate"] is None or y["passRate"] is None:
            print(f"{task:<34} only in {'candidate' if not x else 'baseline'}")
            continue
        d = y["passRate"] - x["passRate"]
        flag = ""
        if d <= -args.threshold:
            flag = "  REGRESSION"
            regressions.append(task)
        elif d >= args.threshold:
            flag = "  improved"
        print(f"{task:<34} {x['passRate']:>5.0%} -> {y['passRate']:<5.0%} "
              f"{x['toolCalls']:>5.1f} -> {y['toolCalls']:<5.1f} {x['toolErrors']:>4.1f} -> {y['toolErrors']:<4.1f} "
              f"{x['inputTokens']:>7.0f} -> {y['inputTokens']:<7.0f}{flag}")

    small = min(min((v["n"] for v in a.values()), default=0), min((v["n"] for v in b.values()), default=0))
    if small < 3:
        print(f"\nnote: as few as {small} trial(s) per task; a single flaky trial moves the pass rate a lot. Use --trials 3 or more.")
    if regressions:
        print(f"\n{len(regressions)} regression(s): {', '.join(regressions)}")
        return 1 if args.fail_on_regression else 0
    print("\nno regressions")
    return 0


# ---------------------------------------------------------------- main

def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--project", type=Path, help="Unity project folder (default: found from the current directory)")
    sub = ap.add_subparsers(dest="command", required=True)

    p = sub.add_parser("list", help="list suites and tasks")
    p.add_argument("--prompts", action="store_true", help="also print each task's prompt")
    p.set_defaults(fn=cmd_list)

    p = sub.add_parser("replay", help="tier 1: replay reference solutions (no model)")
    p.add_argument("--task", default="all", help="'all', a suite name, or a qualified task id")
    p.add_argument("--json", help="also write the report to this file")
    p.set_defaults(fn=cmd_replay)

    p = sub.add_parser("run", help="tier 2: run Claude Code on the tasks")
    p.add_argument("--suite", default="masontile")
    p.add_argument("--task", action="append", help="task id (repeatable); default: all tasks in the suite")
    p.add_argument("--tag", action="append", help="only tasks with this tag (repeatable)")
    p.add_argument("--trials", type=int, default=3, help="trials per task (default 3)")
    p.add_argument("--model", default="sonnet", help="Claude Code --model value (default sonnet)")
    p.add_argument("--label", help="name for this run (default: <commit>-<model>)")
    p.add_argument("--out", default=None, help="results folder (default: <project>/AgentBenchRuns)")
    p.add_argument("--timeout", type=int, default=600, help="seconds per trial (default 600)")
    p.add_argument("--max-budget-usd", type=float, help="per-trial spend cap passed to Claude Code")
    p.add_argument("--system-prompt", help="override the suite's system prompt")
    p.add_argument("--claude", help="path to the claude binary")
    p.add_argument("--hold", type=float, default=0, help="seconds to keep each result in the Editor before the next task (for watching)")
    p.set_defaults(fn=cmd_run)

    p = sub.add_parser("report", help="summarise a results file or run folder")
    p.add_argument("results")
    p.set_defaults(fn=cmd_report)

    p = sub.add_parser("compare", help="compare two results files or run folders")
    p.add_argument("baseline")
    p.add_argument("candidate")
    p.add_argument("--threshold", type=float, default=0.34, help="pass-rate change that counts as a regression (default 0.34, one trial in three)")
    p.add_argument("--fail-on-regression", action="store_true", help="exit 1 if any task regressed")
    p.set_defaults(fn=cmd_compare)

    args = ap.parse_args()
    needs_editor = args.command in ("list", "replay", "run")
    if needs_editor or args.project:
        args.project = (args.project or find_project(Path.cwd())).resolve()
    if getattr(args, "out", None) is None and args.command == "run":
        args.out = str(args.project / "AgentBenchRuns")
    editor = Editor(args.project) if needs_editor else None
    try:
        sys.exit(args.fn(args, editor))
    except (EditorError, EditorUnreachable) as e:
        sys.exit(f"error: {e}")


if __name__ == "__main__":
    main()
