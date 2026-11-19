# Local Control API & Companion CLI

raccoonwatch exposes a small **local-only control API** that lets scripts and other programs on
the same Windows computer query and control the running instance. The companion CLI,
`raccoonwatch-cli`, is the reference client.

The API is a **named-pipe JSON protocol**. There is no TCP port, no HTTP server, and nothing
listens on the network. The pipe is created with the default security descriptor, so only the
interactive user (and administrators) can connect. This matches the existing single-instance
IPC (`raccoonwatch.ipc`) already used by the tray application.

## Contents

- [Architecture](#architecture)
- [Availability and lifecycle](#availability-and-lifecycle)
- [Using the CLI](#using-the-cli)
- [Commands](#commands)
- [JSON wire protocol](#json-wire-protocol)
- [Security](#security)
- [Troubleshooting](#troubleshooting)

---

## Architecture

| Project | Role |
| --- | --- |
| `src/raccoonwatch.Api` | Shared protocol (request/response DTOs, JSON helpers), `ApiClient`, `NamedPipeApiServer`, and the `IApiRequestHandler` contract. |
| `src/raccoonwatch` | Hosts `NamedPipeApiServer` on startup. `ApiRequestHandler` dispatches calls to the existing `ISettingsService`, `IProcessManager`, and `IUpdateCommandRunner` services. |
| `src/raccoonwatch.Cli` | Console front end (`raccoonwatch-cli`) that connects with `ApiClient`. |
| `tests/raccoonwatch.Tests` | Unit tests (`ApiTests.cs`) covering the protocol, pipe server/client, handler, and CLI helpers. |

The handler only produces plain DTOs, never WinForms types, so it can be unit-tested without a UI.
Server and client run on independent threads; a slow or malformed client cannot block the tray UI.

## Availability and lifecycle

- The API server starts right after settings are loaded on application startup and is hosted for
  the lifetime of the tray process. It stops when the application exits.
- Because single-instance enforcement is active, there is always at most one API server.
- If raccoonwatch is not running, connecting fails quickly (the CLI prints a friendly error and
  exits with code `1`).

## Using the CLI

```powershell
raccoonwatch-cli --help
raccoonwatch-cli ping
raccoonwatch-cli list
```

By default the CLI connects to the pipe named `raccoonwatch.api`. To override it (for
 troubleshooting or side-by-side instances), set the environment variable:

```powershell
$env:RACCOONWATCH_PIPE = 'raccoonwatch.api'
```

Output is human-readable by default; pass `--json-output` to print the raw JSON response for
scripting:

```powershell
raccoonwatch-cli list --json-output | ConvertFrom-Json
```

## Commands

All commands that act on a single process take `--id <guid>`. Get the ids from `list`.

| Command | Description |
| --- | --- |
| `ping` | Checks that the local API server is reachable. |
| `list` | Lists every configured process with status, uptime, PID, and id. |
| `status [--id <guid>]` | Shows one process, or all processes when `--id` is omitted. |
| `start --id <guid>` | Starts a process. |
| `stop --id <guid>` | Stops a process. |
| `restart --id <guid>` | Restarts a process. |
| `update --id <guid>` | Runs the configured one-off update command for a process. |

### Examples (PowerShell)

```powershell
# See what is configured and running
raccoonwatch-cli list

# Start, stop, restart
raccoonwatch-cli start  --id 11111111-2222-3333-4444-555555555555
raccoonwatch-cli stop   --id 11111111-2222-3333-4444-555555555555
raccoonwatch-cli restart --id 11111111-2222-3333-4444-555555555555

# Run the configured updater command
raccoonwatch-cli update --id 11111111-2222-3333-4444-555555555555
```

Process configuration can only be changed from the tray application's Manage Processes window.
The API deliberately offers no command to edit or replace a process definition: allowing a remote
caller to set an executable path would permit arbitrary command execution.

## JSON wire protocol

Each client connection sends exactly one UTF-8 JSON **request line** (`\n`-terminated) and reads
one JSON **response line**.

Request:

```json
{ "method": "start", "id": "11111111-2222-3333-4444-555555555555" }
```

Successful response (data shape depends on the method):

```json
{ "success": true, "data": { "id": "...", "displayName": "Demo", "status": "Running", "uptimeSeconds": 125 } }
```

Failure response:

```json
{ "success": false, "error": "Process '...' was not found." }
```

Methods: `ping`, `list`, `get_status`, `start`, `stop`, `restart`, `update`.

You can use any language that can open a Windows named pipe. For example, in PowerShell:

```powershell
$pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'raccoonwatch.api', [System.IO.Pipes.PipeDirection]::InOut)
$pipe.Connect(3000)
$writer = New-Object System.IO.StreamWriter($pipe)
$writer.AutoFlush = $true
$reader = New-Object System.IO.StreamReader($pipe)
$writer.WriteLine('{"method":"ping"}')
$reader.ReadLine()
$pipe.Dispose()
```

## Security

- The pipe is **local-only** (`\\.\pipe\`); it is not reachable over the network.
- Default pipe ACLs restrict access to the creating user and administrators.
- The API intentionally exposes no remote/network listener and no elevation: it can only control
  processes raccoonwatch already manages in the current user session.
- Callers must be running as the same user as the raccoonwatch instance.
- The API cannot create or edit process definitions; only commands already configured by the
  user in the UI can be launched by `update`.

## Troubleshooting

| Symptom | Cause / fix |
| --- | --- |
| `raccoonwatch is not running` | Start the raccoonwatch tray app, then retry. |
| `A valid process id is required` | Supply `--id` with a GUID from `list`. |
| `Process '...' was not found` | The id is stale; run `list` to refresh. |
| `No update command is configured` | Set `updateCommandPath` on the process in the UI first. |
| `Unreadable request` | The client sent malformed JSON (protocol clients only). |
