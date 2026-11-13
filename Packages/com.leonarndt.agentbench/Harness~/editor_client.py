"""Minimal client for the Unity Pipeline server of a running Editor (stdlib only).

Reads the port and bearer token from <project>/Library/Pipeline/.unity-pipeline-port on every request,
because the token can rotate and the port can change across Editor restarts.
"""

import json
import time
import urllib.error
import urllib.request
from pathlib import Path


class EditorError(RuntimeError):
    """The Editor answered, and the command failed."""


class EditorUnreachable(RuntimeError):
    """No Editor answered (not running, reloading scripts, or a modal dialog is open)."""


class Editor:
    def __init__(self, project: Path):
        self.project = Path(project)
        self.descriptor_path = self.project / "Library" / "Pipeline" / ".unity-pipeline-port"

    def _descriptor(self):
        try:
            return json.loads(self.descriptor_path.read_text())
        except FileNotFoundError:
            raise EditorUnreachable(
                f"No Pipeline server descriptor at {self.descriptor_path}. Open the project in Unity "
                "(with com.unity.pipeline installed) and wait until it is ready."
            ) from None

    def exec(self, command: str, parameters: dict | None = None, timeout: float = 120, retries: int = 30):
        """Runs `unity command <command>` and returns its result. Retries while the Editor is busy or reloading."""
        body = json.dumps({"command": command, "parameters": parameters or {}}).encode()
        last_error = None
        for attempt in range(retries):
            try:
                d = self._descriptor()
                req = urllib.request.Request(
                    f"http://127.0.0.1:{d['port']}/api/exec",
                    data=body,
                    headers={"Authorization": f"Bearer {d['evalToken']}", "Content-Type": "application/json"},
                )
                with urllib.request.urlopen(req, timeout=timeout) as resp:
                    envelope = json.loads(resp.read())
            except urllib.error.HTTPError as e:
                payload = e.read()
                try:
                    envelope = json.loads(payload)
                except ValueError:
                    envelope = {"success": False, "error": payload.decode(errors="replace")}
                if e.code in (502, 503, 429):  # settling, busy, dialog: retry
                    last_error = envelope.get("errorDetails") or envelope.get("error") or f"HTTP {e.code}"
                    time.sleep(min(2 + attempt, 10))
                    continue
            except (urllib.error.URLError, ConnectionError, TimeoutError, EditorUnreachable) as e:
                last_error = str(e)
                time.sleep(min(2 + attempt, 10))
                continue

            if not envelope.get("success"):
                raise EditorError(envelope.get("error") or envelope.get("errorDetails") or json.dumps(envelope))
            return envelope.get("result")

        raise EditorUnreachable(f"'{command}' got no answer from the Editor after {retries} attempts: {last_error}")


def find_project(start: Path) -> Path:
    """The nearest folder at or above `start` that contains ProjectSettings/ and Assets/."""
    for p in [start, *start.parents]:
        if (p / "ProjectSettings").is_dir() and (p / "Assets").is_dir():
            return p
    raise SystemExit(f"No Unity project at or above {start}; pass --project.")
