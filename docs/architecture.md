# raccoonwatch — Architecture and Technical Design (Phase 0)

Version: 1.0
Status: Approved — reflects the implemented application.

## 1. Architectural overview

A layered, single-process desktop application. Windows Forms supplies the tray icon
and forms; all behavior that can be tested without UI lives behind interfaces in a
service layer.

```
┌──────────────────────────────────────────────────────────────┐
│ Presentation (WinForms, UI thread)                            │
│  TrayApplicationContext · ManageProcessesForm ·               │
│  ProcessEditForm · ProcessOutputForm · SettingsForm ·          │
│  AboutForm · AnsiTextParser · TrayGlyph · StatusIcons         │
└───────────────┬──────────────────────────────────────────────┘
                │ events / commands (marshalled to UI thread)
┌───────────────▼──────────────────────────────────────────────┐
│ Application services (interfaces, async)                      │
│  IProcessManager · IOutputStore · ISettingsService ·          │
│  IAutostartService · IAppController                           │
└───────────────┬──────────────────────────────────────────────┘
                │
┌───────────────▼──────────────────────────────────────────────┐
│ Domain / Infrastructure                                       │
│  Models · ProcessLauncher · JsonSettingsStore ·               │
│  WindowsRunRegistry · FileLogger · SingleInstanceGuard ·       │
│  AppIcon (embedded resources)                                  │
└──────────────────────────────────────────────────────────────┘
```

Dependencies point inward: presentation may call services and subscribe to events;
services never reference WinForms types. This keeps the core testable with xUnit.

## 2. Repository layout

```
D:\Projects\raccoonwatch\
├── LICENSE                     # GNU GPL v3.0
├── PLAN.md
├── README.md
├── raccoonwatch.slnx
├── docs\                       # specification, design, data model, UI, QA checklist
├── assets\
│   ├── raccoonwatch.ico        # generated from the PNGs below
│   ├── raccoonwatch_icon_64x64.png
│   ├── raccoonwatch_icon_1254x1254.png
│   ├── github-mark.png         # GitHub icon used in the About window
│   └── status-*.png            # 16x16 status icons used in tray menu items
├── scripts\
│   ├── generate-icon.ps1
│   └── publish.ps1
├── src\
│   ├── raccoonwatch\
│   │   ├── raccoonwatch.csproj # net10.0-windows, UseWindowsForms, embedded icons
│   │   ├── Program.cs          # entry point, DI, logging, single instance
│   │   ├── TrayApplicationContext.cs
│   │   ├── Api\                # request handler + update command runner (host side)
│   │   ├── Models\             # ProcessDefinition, AppSettings, enums, runtime state
│   │   ├── Services\           # interfaces + implementations
│   │   ├── Infrastructure\     # launcher, JSON store, registry, logging, AppIcon
│   │   └── UI\                 # forms, ANSI parser, tray glyphs, status icons
│   ├── raccoonwatch.Api\       # shared wire protocol, named pipe server/client (net10.0)
│   └── raccoonwatch.Cli\       # companion CLI executable (net10.0)
└── tests\
    └── raccoonwatch.Tests\     # xUnit
```

## 3. Components and responsibilities

| Component | Responsibility |
|---|---|
| `Program` | Configure DI, logging, high-DPI mode, single-instance guard; start `ApplicationContext`; start autostart processes. |
| `SingleInstanceGuard` | Named `Mutex` (`Local\raccoonwatch.singleinstance`). Second instance signals the first via a named pipe message `SHOW_MANAGE`, then exits. |
| `TrayApplicationContext` | Owns `NotifyIcon`; builds context menu from settings and runtime states; handles double-click; coordinates showing/hiding forms; clean shutdown. |
| `ISettingsService` | Load/save `AppSettings`; validate and normalize definitions; raise `Changed`; atomic writes on explicit save. |
| `JsonSettingsStore` | Schema-versioned serialize/deserialize with `System.Text.Json`; atomic temp-file + `File.Move(overwrite)`; creates defaults and backups on corrupt file. |
| `IProcessManager` | Authoritative runtime state for definitions: start, stop, restart, start/stop all; periodic polling; emits `StateChanged`. |
| `ProcessLauncher` | Builds `ProcessStartInfo` per `ProcessKind`; creates `Process` objects with redirected stdout/stderr; returns a handle used by the manager. |
| `IOutputStore` | Bounded per-process ring buffers (5,000 lines default); thread-safe lock-based append; emits batched `OutputReceived` events every ~100 ms; provides snapshot for late viewers. |
| `IAutostartService` | Reads/writes the HKCU Run value; reports path mismatch; no elevation. |
| `IAppController` | Orchestration used by UI: initialize, start flagged processes in order, exit behavior (optionally stop tracked processes). |
| Forms | `ManageProcessesForm` (DataGridView + Add/Edit/Delete/reorder), `ProcessEditForm`, `ProcessOutputForm` (ANSI colors, save), `SettingsForm`, `AboutForm`. All UI-thread only. |
| `NamedPipeApiServer` / `ApiClient` | Local-only control API hosted on named pipe `raccoonwatch.api` (see `docs/control-api.md`). One JSON request/response line per connection; multi-instance pipe for concurrent callers. |
| `ApiRequestHandler` | Maps API methods onto `ISettingsService`, `IProcessManager` and `IUpdateCommandRunner`; returns plain DTOs so it is UI-independent. |
| `raccoonwatch-cli` | Console front end using `ApiClient`; supports human-readable and `--json-output` modes. |

## 4. Startup sequence

1. `Main` enables per-monitor DPI awareness, configures logging.
2. Construct `SingleInstanceGuard`; if it is not the first instance, signal `SHOW_MANAGE`
   to the running instance and return.
3. Build service provider; `ISettingsService.Load()` (defaults on missing/corrupt file, with a warning log).
4. Create `TrayApplicationContext` and `NotifyIcon`; set initial icon/tooltip and menu.
5. Call `IAppController.StartupAsync()`:
   - query `IAutostartService` state (for Settings display only),
   - iterate definitions with `AutoStartOnLaunch == true` ordered by `StartOrder`,
   - start each via `IProcessManager.StartAsync`, logging/notification on failure.
6. `Application.Run(context)`; on exit, persist settings and optionally stop tracked processes.

## 5. Threading and concurrency

- WinForms requires an STA UI thread. All form and `NotifyIcon` access happens on that thread.
- `IProcessManager` runs a `Task.Delay`-based polling loop (default 2 s, re-read from
  settings each iteration) on a background task. It
  only touches `Process` objects and runtime state; it raises events with immutable
  snapshots.
- Services marshal UI-bound events through `SynchronizationContext` captured at startup
  (or `BeginInvoke` on the context's control). No service ever calls a form directly.
- Output capture uses async line reads (`BeginOutputReadLine`/`OutputDataReceived`).
  The store appends under a lock and raises at most one batched event per ~100 ms to
  prevent UI flooding.
- `StopAsync` uses `WaitForExitAsync` with cancellation and a timeout; force-kill of the
  process tree is a fallback. Disposing the manager cancels the timer and drains output
  readers so no orphan callbacks remain.
- Status snapshot reads are lock-protected or use immutable records to avoid torn state.

## 6. Process launch and control strategy

### 6.1 Command construction

| Kind | `FileName` | `Arguments` (example) |
|---|---|---|
| Executable | `C:\tools\api.exe` | `--port 5000` |
| Cmd | `%COMSPEC%` (`cmd.exe`) | `/c "C:\scripts\backup.cmd" ...` |
| PowerShell | `powershell.exe` (or detected `pwsh.exe`) | `-NoProfile -ExecutionPolicy Bypass -File "C:\scripts\worker.ps1" ...` |

All launches use `UseShellExecute = false`, `RedirectStandardOutput = true`,
`RedirectStandardError = true`, `CreateNoWindow = true`, and the configured working
directory. Arguments are quoted/escaped by a helper and unit-tested; user arguments
are appended after the script path for script kinds.

### 6.2 Stop / restart

1. Mark status `Stopping`.
2. Try `CloseMainWindow()` for GUI processes; wait up to the configured timeout
   (default 5 s).
3. If still alive, `Kill(entireProcessTree: true)`.
4. Wait for exit, capture exit code/time, set status `Stopped`, clear PID.
5. Restart = the above followed by `StartAsync`; output history gets a
   `--- restarted at <timestamp> ---` separator.

### 6.3 Exit detection

The polling loop checks `HasExited`; when true it reads `ExitCode`, records
`ExitedAt`, and sets `Exited` (unless a stop was in progress, in which case `Stopped`).
A process that exits within the first poll after launch with a non-zero code is marked
`Failed` with its stderr tail as the error message.

## 7. Settings and persistence

- `JsonSettingsStore` writes to `settings.json.tmp`, then replaces the target. A
  corrupt file is moved to `settings.json.bad` and defaults are created.
- `ISettingsService.Changed` is raised after an explicit save (for example, OK/Apply
  in Settings or Add/Edit/Delete in the process manager); there is no background
  polling or debounce timer.
- Absolute paths are stored (no environment variables expanded at save time beyond
  trimming); `%APPDATA%` is resolved via `Environment.GetFolderPath`.
- The raccoon ICO, the 1254 px artwork, the GitHub mark and the 16×16 `status-*.png`
  icons are embedded as assembly resources and loaded through `AppIcon` / `AboutForm`
  / `StatusIcons`, so single-file publishes keep the tray icon, About imagery and
  status icons.

## 8. Autostart

- Registry path: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
- Value name: `raccoonwatch`; value data: `"<full path to raccoonwatch.exe>"`.
- Enabling writes the value; disabling deletes it. Settings shows state and a warning
  if the stored path no longer matches the running executable (offers repair).
- Per-process autostart is purely application-side (FR-8) and independent of this
  registry entry.

## 9. Error handling, logging, diagnostics

- Each service method returns a result or throws a domain-specific exception caught at
  the UI/orchestration boundary; errors are logged and surfaced once.
- Logging: `Microsoft.Extensions.Logging` with a simple rolling file provider writing
  `%LOCALAPPDATA%\raccoonwatch\logs\raccoonwatch-YYYYMMDD.log`; default level
  `Information`, configurable in Settings.
- A tray balloon notification (configurable) reports failed starts/stops; details are
  always available in the output viewer / status line / log.

## 10. Security considerations

- No elevation and no HKLM writes.
- `-ExecutionPolicy Bypass` applies only to the PowerShell instance raccoonwatch
  launches for a user-configured script; this is documented in the UI.
- Settings contain only paths/arguments; the user is warned not to store secrets there.
- All external input (paths, arguments) is validated before use; no dynamic code is
  evaluated by raccoonwatch itself.
- External output text is treated as untrusted data and never interpreted as commands
  or markup beyond ANSI color codes.
- The control API uses a local named pipe with default ACLs (same user / administrators)
  and no network listener. It deliberately exposes no operation that creates or edits a
  process definition, so a caller cannot set an executable path and execute arbitrary code.

## 11. Testing strategy

- **Unit (xUnit):** settings round-trip/corrupt-file recovery, argument quoting and
  command construction, `ProcessKind` detection, status transitions, output ring
  buffer bounds, autostart value formatting (with an injectable registry abstraction),
  and the control API (wire format, pipe client/server round trip, request dispatch, CLI
  option parsing and uptime formatting) in `tests/raccoonwatch.Tests/ApiTests.cs`.
- **Integration/manual:** launch sample `.exe`, `.cmd`, `.ps1`; start/stop/restart from
  tray; kill a process externally and observe `Exited`; open output viewer; toggle both
  autostart mechanisms; verify single instance and DPI scaling. The checklist is in
  [`docs/test-checklist.md`](test-checklist.md).
- **Release check:** `dotnet publish` single-file output launches on a clean Windows
  profile with the correct tray icon.

## 12. Risks and mitigations

| Risk | Mitigation |
|---|---|
| Tray icon APIs behave differently across Windows versions | Use WinForms `NotifyIcon` only; test on Windows 10 and 11. |
| High-frequency output floods UI | Batched events, bounded buffers, lock-protected store. |
| Processes that spawn children/spawn and exit | Track by tree where possible; `Kill(entireProcessTree: true)`; document detach limitation. |
| Corrupt settings | Atomic writes, backup, defaults + warning. |
| Autostart path breaks after move | Detect mismatch in Settings and offer repair. |
