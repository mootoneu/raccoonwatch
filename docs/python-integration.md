# Integrating a Python 3 service with raccoonwatch

This guide is for a separate Python 3 application (for example one built on
`http.server.ThreadingHTTPServer`) that needs to query and control the processes managed by a
running raccoonwatch instance.

The integration surface is the companion CLI, **`raccoonwatch-cli.exe`**. It is published next to
`raccoonwatch.exe` by `scripts/publish.ps1`. The CLI talks to the tray app over a local named pipe;
your Python code only needs to spawn a short-lived subprocess and parse JSON. You do not need to
reimplement the pipe protocol, and you do not need any third-party Python packages.

> **Security model:** raccoonwatch itself exposes no network listener. The CLI can only reach the
> running instance from the same Windows session/user, and it cannot create or edit process
> definitions — `update` only runs the updater command the user already configured in the UI.
> Your Python service is responsible for its own HTTP authentication and for binding to a safe
> interface (see [Deployment notes](#deployment-notes)).

## Contents

- [CLI reference](#cli-reference)
- [The JSON response envelope](#the-json-response-envelope)
- [Reusable Python client](#reusable-python-client)
- [ThreadingHTTPServer example](#threadinghttpserver-example)
- [Error handling](#error-handling)
- [Deployment notes](#deployment-notes)
- [Prompt for an AI assistant](#prompt-for-an-ai-assistant)

---

## CLI reference

Always pass `--json-output` so the CLI prints one machine-readable JSON line to stdout. Invoke it
with an **argument list**, never with `shell=True`, so paths and IDs cannot be reinterpreted by a
shell.

| CLI invocation | Purpose |
| --- | --- |
| `raccoonwatch-cli ping --json-output` | Check that the local API is reachable. |
| `raccoonwatch-cli list --json-output` | List all configured processes (status, uptime, PID, id). |
| `raccoonwatch-cli status --id <guid> --json-output` | Status and uptime for one process. |
| `raccoonwatch-cli start --id <guid> --json-output` | Start a process. |
| `raccoonwatch-cli stop --id <guid> --json-output` | Stop a process. |
| `raccoonwatch-cli restart --id <guid> --json-output` | Restart a process. |
| `raccoonwatch-cli update --id <guid> --json-output` | Run the configured updater command. |

Options: `--id` / `-i` selects the process, `--json-output` requests JSON, `-h` / `--help` prints
usage. `status` without `--id` behaves like `list`.

**Exit codes:** `0` on success, `1` on failure. On failure the CLI writes `error: <message>` to
stderr (stdout stays empty). With `--json-output`, a successful call writes the full JSON envelope
to stdout.

> `update` waits for the configured updater to exit, so give that call a much longer timeout than
> the other commands.

## The JSON response envelope

```json
{ "success": true, "data": [ { "id": "…", "displayName": "API server", "status": "Running", "uptimeSeconds": 125 } ] }
```

```json
{ "success": false, "error": "Process '…' was not found." }
```

`data` is an array for `list` and an object for `status` / `start` / `stop` / `restart` / `update`
(the process snapshot after the action). Process fields:

| Field | Type | Meaning |
| --- | --- | --- |
| `id` | string (GUID) | Stable id used with `--id`. |
| `displayName`, `kind` | string | Configured name and `Executable` / `Cmd` / `PowerShell`. |
| `status` | string | `Unknown`, `Stopped`, `Starting`, `Running`, `Stopping`, `Exited`, `Failed`. |
| `processId` | number or null | PID while running. |
| `startedAt`, `exitedAt` | ISO-8601 string or null | Timestamps. |
| `uptimeSeconds` | number or null | Computed uptime in seconds. |
| `exitCode`, `lastError`, `restartCount` | number / string / number | Diagnostics. |
| `commandPath`, `arguments`, `workingDirectory` | string | Read-only configuration details. |
| `updateCommandPath`, `updateArguments` | string | Configured updater. |
| `startOrder`, `autoStartOnLaunch`, `notes` | number / bool / string | Other configuration. |

Treat every field as untrusted data. Never pass a value from this payload into a shell.

## Reusable Python client

Save as `raccoonwatch_client.py`. Standard library only; works on Windows and is harmless
elsewhere (the `CREATE_NO_WINDOW` flag is simply ignored if unavailable).

```python
from __future__ import annotations

import json
import os
import subprocess
import uuid
from pathlib import Path
from typing import Any


class RaccoonwatchError(RuntimeError):
    """raccoonwatch CLI returned a failure or could not be reached."""


class RaccoonwatchClient:
    def __init__(
        self,
        cli_path: str | os.PathLike[str] | None = None,
        *,
        timeout: float = 10.0,
        update_timeout: float = 300.0,
        pipe_name: str | None = None,
    ) -> None:
        default = os.environ.get("RACCOONWATCH_CLI", "raccoonwatch-cli.exe")
        self.cli_path = Path(cli_path) if cli_path else Path(default)
        self.timeout = timeout
        self.update_timeout = update_timeout
        self.env = os.environ.copy()
        if pipe_name:
            self.env["RACCOONWATCH_PIPE"] = pipe_name

    def _run(self, *args: str, timeout: float | None = None) -> Any:
        cmd = [str(self.cli_path), *args, "--json-output"]
        no_window = getattr(subprocess, "CREATE_NO_WINDOW", 0)

        try:
            result = subprocess.run(
                cmd,
                capture_output=True,
                text=True,
                encoding="utf-8",
                errors="replace",
                timeout=timeout or self.timeout,
                creationflags=no_window,
                env=self.env,
                check=False,
            )
        except FileNotFoundError as exc:
            raise RaccoonwatchError(f"CLI not found: {self.cli_path}") from exc
        except subprocess.TimeoutExpired as exc:
            raise RaccoonwatchError(f"CLI timed out: {' '.join(cmd)}") from exc

        if result.returncode != 0:
            detail = (result.stderr or result.stdout or "CLI failed").strip()
            raise RaccoonwatchError(detail.removeprefix("error: ").strip())

        try:
            envelope = json.loads(result.stdout)
        except json.JSONDecodeError as exc:
            raise RaccoonwatchError(f"Invalid CLI response: {result.stdout!r}") from exc

        if not envelope.get("success"):
            raise RaccoonwatchError(str(envelope.get("error") or "CLI reported a failure"))
        return envelope.get("data")

    @staticmethod
    def _check_id(process_id: str) -> str:
        try:
            return str(uuid.UUID(process_id))
        except (ValueError, AttributeError, TypeError) as exc:
            raise ValueError(f"Invalid process id: {process_id!r}") from exc

    def ping(self) -> Any:
        return self._run("ping")

    def list_processes(self) -> list[dict[str, Any]]:
        return self._run("list") or []

    def get_status(self, process_id: str) -> dict[str, Any]:
        return self._run("status", "--id", self._check_id(process_id))

    def start(self, process_id: str) -> dict[str, Any]:
        return self._run("start", "--id", self._check_id(process_id))

    def stop(self, process_id: str) -> dict[str, Any]:
        return self._run("stop", "--id", self._check_id(process_id))

    def restart(self, process_id: str) -> dict[str, Any]:
        return self._run("restart", "--id", self._check_id(process_id))

    def update(self, process_id: str) -> dict[str, Any]:
        return self._run(
            "update", "--id", self._check_id(process_id), timeout=self.update_timeout
        )
```

Configure the executable with `RACCOONWATCH_CLI` (an absolute path is recommended when the
service's working directory is not the publish folder):

```powershell
$env:RACCOONWATCH_CLI = 'C:\apps\raccoonwatch\raccoonwatch-cli.exe'
```

## ThreadingHTTPServer example

Save as `bridge.py` beside `raccoonwatch_client.py`. It exposes a loopback-only JSON API that maps
one-to-one onto the CLI commands. `ThreadingHTTPServer` gives each request its own thread, so a
slow `update` call does not block status queries; the CLI client itself is stateless and safe to
use concurrently.

```python
from __future__ import annotations

import json
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import unquote, urlparse

from raccoonwatch_client import RaccoonwatchClient, RaccoonwatchError

client = RaccoonwatchClient()
ACTIONS = {"start", "stop", "restart", "update"}


class Handler(BaseHTTPRequestHandler):
    server_version = "RaccoonwatchBridge/1.0"

    def _send(self, status: int, payload: object) -> None:
        body = json.dumps(payload).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def _dispatch(self, method: str) -> None:
        parts = [p for p in unquote(urlparse(self.path).path).split("/") if p]

        try:
            if method == "GET" and parts == ["health"]:
                client.ping()
                return self._send(200, {"ok": True})

            if method == "GET" and parts == ["processes"]:
                return self._send(200, client.list_processes())

            if method == "GET" and len(parts) == 2 and parts[0] == "processes":
                return self._send(200, client.get_status(parts[1]))

            if (
                method == "POST"
                and len(parts) == 3
                and parts[0] == "processes"
                and parts[2] in ACTIONS
            ):
                result = getattr(client, parts[2])(parts[1])
                return self._send(200, result)

            return self._send(404, {"error": "Not found"})
        except ValueError as exc:
            return self._send(400, {"error": str(exc)})
        except RaccoonwatchError as exc:
            message = str(exc)
            lowered = message.lower()
            if "not running" in lowered:
                return self._send(503, {"error": message})
            if "was not found" in lowered:
                return self._send(404, {"error": message})
            return self._send(502, {"error": message})

    def do_GET(self) -> None:
        self._dispatch("GET")

    def do_POST(self) -> None:
        self._dispatch("POST")


if __name__ == "__main__":
    server = ThreadingHTTPServer(("127.0.0.1", 8765), Handler)
    server.daemon_threads = True
    print("Listening on http://127.0.0.1:8765")
    server.serve_forever()
```

Run and exercise it:

```powershell
python bridge.py
```

```powershell
Invoke-RestMethod http://127.0.0.1:8765/health
Invoke-RestMethod http://127.0.0.1:8765/processes
Invoke-RestMethod -Method Post http://127.0.0.1:8765/processes/<guid>/restart
```

## Error handling

| Situation | CLI behavior | Suggested HTTP status |
| --- | --- | --- |
| raccoonwatch not running | exit 1, `raccoonwatch is not running (…)` | 503 |
| malformed or unknown `--id` | exit 1, `A valid process id is required` | 400 |
| stale id | exit 1, `Process '…' was not found` | 404 |
| process already running / updater missing | exit 1 with the reason | 502 |
| CLI executable missing | `FileNotFoundError` from Python | 500 (deployment error) |
| call exceeds timeout | `subprocess.TimeoutExpired` | 504 |

If you need to unit-test your integration, patch `subprocess.run` (or inject a fake client) and
return a `CompletedProcess` with stdout such as
`'{"success":true,"data":[{"id":"…","status":"Running","uptimeSeconds":125}]}'`.

## Deployment notes

- Bind your HTTP server to `127.0.0.1` (as in the example) unless you add authentication and TLS.
  raccoonwatch's own API is local-only by design; do not bridge it to the network unsecured.
- Run the Python service as the **same Windows user** as raccoonwatch. The named pipe is protected
  by the default ACL for that user; a different user (including a different service account)
  cannot connect.
- Use an absolute path for the CLI or set `RACCOONWATCH_CLI`. Single-file publishes place the CLI
  in `artifacts/publish/win-x64/` together with `raccoonwatch.exe`.
- Always pass arguments as a list and never use `shell=True`; this avoids quoting problems on
  Windows and removes any command-injection risk from HTTP input.
- `CREATE_NO_WINDOW` prevents a console window flashing when the CLI runs from a GUI/service
  process. On non-Windows platforms the flag is ignored.
- If a custom named pipe is used, set `RACCOONWATCH_PIPE` for the child process via the `pipe_name`
  argument shown above.
- Status polling inside raccoonwatch accelerates automatically to 250 ms for two minutes after a
  start/stop/restart, so a brief wait before reading status after an action is usually enough.

## Prompt for an AI assistant

Paste the following when asking an assistant to write or adapt the integration:

```text
Write a Python 3 integration for the raccoonwatch Windows tray application. Requirements:

- Use only the Python standard library (no pip packages). The host process runs a tiny HTTP
  server with http.server.ThreadingHTTPServer bound to 127.0.0.1.
- Integrate through the companion CLI executable "raccoonwatch-cli.exe", which is published next
  to raccoonwatch.exe. Do not implement the named-pipe protocol yourself.
- Always call the CLI with an argument list (never shell=True) and add "--json-output" so stdout
  contains one JSON envelope: {"success": bool, "data": ..., "error": str|null}.
  "list" returns an array of process objects; other commands return one object. Exit code 1 means
  failure, with "error: <message>" on stderr.
- Expose these operations: ping, list, get status by id, start, stop, restart, and update by id.
  "update" runs the user-configured updater command; it cannot change process definitions and
  may take a long time, so allow a much longer subprocess timeout for it than for the others.
- Validate ids as UUIDs before passing them to the CLI. Map failures to sensible HTTP statuses:
  400 invalid id, 404 unknown process, 503 raccoonwatch not running, 502 other CLI errors, 504
  timeout.
- Use subprocess.CREATE_NO_WINDOW on Windows to avoid a console flash, allow the CLI path to be
  configured with the RACCOONWATCH_CLI environment variable, and support RACCOONWATCH_PIPE.
- Include a short README section with run instructions and a note that both raccoonwatch and the
  Python service must run as the same Windows user, and that the HTTP server must stay on
  loopback unless authentication is added.

Return a self-contained module with a reusable client class plus a ThreadingHTTPServer example.
```
