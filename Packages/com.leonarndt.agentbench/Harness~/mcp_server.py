"""stdio MCP server that exposes a bench suite's [AgentTool]s to an agent and runs them in the Unity Editor.

Started by Claude Code (via --mcp-config) for one trial. Tool names are the AI Assistant ids with '.' replaced by
'_' (MCP names allow only [A-Za-z0-9_-]); descriptions and schemas are exactly what the Editor reported.
Every call is appended to a JSONL log, which is the harness's ground truth for tool-call metrics.

Usage: mcp_server.py --project <unity project> --tools-file <tools.json> --log <calls.jsonl>
"""

import argparse
import json
import sys
import time
from pathlib import Path

sys.dont_write_bytecode = True  # keep __pycache__ out of the package folder
sys.path.insert(0, str(Path(__file__).resolve().parent))
from editor_client import Editor, EditorError, EditorUnreachable  # noqa: E402

PROTOCOL_VERSION = "2025-06-18"


def mcp_name(tool_id: str) -> str:
    return tool_id.replace(".", "_")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--project", required=True)
    ap.add_argument("--tools-file", required=True)
    ap.add_argument("--log", required=True)
    args = ap.parse_args()

    editor = Editor(Path(args.project))
    tools = json.loads(Path(args.tools_file).read_text())["tools"]
    by_name = {mcp_name(t["id"]): t for t in tools}
    log = open(args.log, "a", encoding="utf-8")

    def reply(msg_id, result=None, error=None):
        msg = {"jsonrpc": "2.0", "id": msg_id}
        if error is not None:
            msg["error"] = error
        else:
            msg["result"] = result
        sys.stdout.write(json.dumps(msg) + "\n")
        sys.stdout.flush()

    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            msg = json.loads(line)
        except ValueError:
            continue
        method, msg_id, params = msg.get("method"), msg.get("id"), msg.get("params") or {}

        if method == "initialize":
            reply(msg_id, {
                "protocolVersion": params.get("protocolVersion", PROTOCOL_VERSION),
                "capabilities": {"tools": {}},
                "serverInfo": {"name": "agentbench", "version": "0.1.0"},
            })
        elif method == "tools/list":
            reply(msg_id, {"tools": [
                {"name": mcp_name(t["id"]), "description": t["description"], "inputSchema": t["inputSchema"]} for t in tools
            ]})
        elif method == "tools/call":
            name = params.get("name", "")
            arguments = params.get("arguments") or {}
            tool = by_name.get(name)
            started = time.time()
            if tool is None:
                outcome = {"tool": name, "ok": False, "response": f"Unknown tool '{name}'."}
            else:
                try:
                    r = editor.exec("agentbench call", {"tool": tool["id"], "args": json.dumps(arguments)})
                    outcome = {"tool": tool["id"], "ok": r["Ok"], "response": r["Response"] or "",
                               "permissionChecks": r.get("PermissionChecks", 0), "editorMs": r.get("Milliseconds")}
                except (EditorError, EditorUnreachable) as e:
                    outcome = {"tool": tool["id"], "ok": False, "response": f"Bench error: {e}", "infra": True}

            log.write(json.dumps({
                "t": started, "tool": outcome["tool"], "args": arguments, "ok": outcome["ok"],
                "chars": len(outcome["response"]), "ms": round((time.time() - started) * 1000, 1),
                "error": None if outcome["ok"] else outcome["response"][:500], "infra": outcome.get("infra", False),
            }) + "\n")
            log.flush()
            reply(msg_id, {"content": [{"type": "text", "text": outcome["response"]}], "isError": not outcome["ok"]})
        elif method == "ping":
            reply(msg_id, {})
        elif msg_id is not None:
            reply(msg_id, error={"code": -32601, "message": f"Method not found: {method}"})
        # Notifications (no id), e.g. notifications/initialized, need no answer.


if __name__ == "__main__":
    main()
