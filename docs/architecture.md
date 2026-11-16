# raccoonwatch — Architecture and Technical Design (Phase 0)

Version: 0.1 (draft for review)
Status: Pending approval

## 1. Architectural overview

A layered, single-process desktop application. Windows Forms supplies the tray icon
and forms; all behavior that can be tested without UI lives behind interfaces in a
service layer.

```
┌──────────────────────────────────────────────────────────────┐
│ Presentation (WinForms, UI thread)                            │
│  TrayApplicationContext · ManageProcessesForm ·               │
│  ProcessEditForm · ProcessOutputForm · SettingsForm           │
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
│  WindowsRunRegistry · FileLogger · SingleInstanceGuard        │
└──────────────────────────────────────────────────────────────┘
```

Dependencies point inward: presentation may call services and subscribe to events;
services never reference WinForms types. This keeps the core testable with xUnit.

## 2. Proposed repository layout

```
D:\Projects\raccoonwatch\
├── PLAN.md
├── README.md
├── docs\                       # this specification set
├── assets\
│   └── raccoonwatch.ico        # generated from provided PNGs (Phase 1)
├── scripts\
│   └── publish.ps1             # Phase 9
├── src\
│   └── raccoonwatch\
│       ├── raccoonwatch.csproj # net10.0-windows, UseWindowsForms
│       ├── Program.cs          # entry point, DI, single instance
│       ├── ApplicationContext.cs
│       ├── Models\             # ProcessDefinition, AppSettings, enums, runtime state
│       ├── Services\           # interfaces + implementations
│       ├── Infrastructure\     # process launcher, JSON store, registry, logging
│       ├── UI\                 # forms and tray menu builder
│       └── Properties\         # assembly info / manifest (DPI awareness)
└── tests\
    └── raccoonwatch.Tests\     # xUnit
```

## 3. Components and responsibilities

| Component | Responsibility |
|---|---|
| `Program` | Configure DI, logging, high-DPI mode, single-instance guard; start `ApplicationContext`; start autostart processes. |
| `SingleInstanceGuard` | Named `Mutex` (`Local\raccoonwatch.singleinstance`). Second instance signals the first via a named pipe message `SHOW_MANAGE`, then exits. |
| `TrayApplicationContext` | Owns `NotifyIcon`; builds context menu from settings and runtime states; handles double-click; coordinates showing/hiding forms; clean shutdown. |
| `ISettingsService` | Load/save `AppSettings`; validate and normalize definitions; raise `SettingsChanged`; atomic writes with debounce. |
| `JsonSettingsStore` | Schema-versioned serialize/deserialize with `System.Text.Json`; atomic temp-file + `File.Move(overwrite)`; creates defaults and backups on corrupt file. |
| `IProcessManager` | Authoritative runtime state for definitions: start, stop, restart, start/stop all; periodic polling; emits `StateChanged`. |
| `ProcessLauncher` | Builds `ProcessStartInfo` per `ProcessKind`; creates `Process` objects with redirected stdout/stderr; returns a handle used by the manager. |
| `IOutputStore` | Bounded per-process ring buffers (5,000 lines default); thread-safe append with `lock`/`Channel`; emits batched `OutputReceived` events; provides snapshot for late viewers. |
| `IAutostartService` | Reads/writes the HKCU Run value; reports path mismatch; no elevation. |
| `IAppController` | Orchestration used by UI: initialize, start flagged processes in order, exit behavior (optionally stop tracked processes). |
| Forms | `ManageProcessesForm` (DataGridView + Add/Edit/Delete), `ProcessEditForm`, `ProcessOutputForm`, `SettingsForm`. All UI-thread only. |

## 4. Startup sequence

1. `Main` enables per-monitor DPI awareness, configures logging.
2. `SingleInstanceGuard.TryAcquire()`; if false, send `SHOW_MANAGE` to first instance and return.
3. Build service provider; `ISettingsService.Load()` (defaults on missing/corrupt file, with a warning log).
4. Create `TrayApplicationContext` and `NotifyIcon`; set initial icon/tooltip and menu.
5. Call `IAppController.StartupAsync()`:
   - query `IAutostartService` state (for Settings display only),
   - iterate definitions with `AutoStartOnLaunch == true` ordered by `StartOrder`,
   - start each via `IProcessManager.StartAsync`, logging/notification on failure.
6. `Application.Run(context)`; on exit, persist settings and optionally stop tracked processes.

## 5. Threading and concurrency

- WinForms requires an STA UI thread. All form and `NotifyIcon` access happens on that thread.
- `IProcessManager` runs a `PeriodicTimer` loop (default 2 s) on a background task. It
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
- `SettingsChanged` triggers a debounced save (e.g. 300 ms) so batch edits result in
  one write.
- Absolute paths are stored (no environment variables expanded at save time beyond
  trimming); `%APPDATA%` is resolved via `Environment.GetFolderPath`.

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

## 11. Testing strategy

- **Unit (xUnit):** settings round-trip/corrupt-file recovery, argument quoting and
  command construction, `ProcessKind` detection, status transitions, output ring
  buffer bounds, autostart value formatting (with an injectable registry abstraction).
- **Integration/manual:** launch sample `.exe`, `.cmd`, `.ps1`; start/stop/restart from
  tray; kill a process externally and observe `Exited`; open output viewer; toggle both
  autostart mechanisms; verify single instance and DPI scaling. The checklist will live
  in `docs/test-checklist.md` (Phase 8).
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
