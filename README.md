# raccoonwatch

`raccoonwatch` is a Windows system-tray application (Windows 10/11, modern C#/.NET 10)
that starts, restarts, stops and monitors a user-managed list of processes. Processes
can be executables (`.exe`), `cmd`/`.bat` scripts or PowerShell (`.ps1`) scripts. The
tray context menu is the main dashboard: each process is shown with its live status.
A terminal-like window lets the user inspect a process's output, and both the app and
individual processes can be configured to start automatically with Windows / with
raccoonwatch.

> **Status:** Phase 0 approved. Phases 1–8 are implemented; release packaging remains.
> See [`PLAN.md`](PLAN.md) for the roadmap and
> [`docs/specification.md`](docs/specification.md) for the requirements.

## Features

- Lives in the notification area; no main window unless requested.
- Right-click menu lists every configured process with status (Running, Stopped,
  Starting, Stopping, Exited, Failed) and per-process Start / Stop / Restart actions.
- Add, edit and delete process definitions (`.exe`, `.cmd`/`.bat`, `.ps1`) with
  arguments, working directory and start order.
- Track PID, start time, exit code, uptime and last error for every process.
- Terminal-like output viewer with stdout/stderr coloring, ANSI colors, follow-tail,
  pause, clear and save-to-file.
- "Start raccoonwatch with Windows" toggle (per-user HKCU Run registry entry).
- "Start with raccoonwatch" flag per process, honoring the configured start order.
- Settings persisted as JSON in `%APPDATA%\raccoonwatch\settings.json`.
- Single instance; double-clicking the tray icon opens the process manager.

## What works now (Phases 1–4)

- Tray icon with dynamic right-click menu listing every configured process and status.
- Start / Stop / Restart / Start All / Stop All from the tray.
- Live status tracking (Running, Stopped, Starting, Stopping, Exited, Failed), PID,
  uptime, exit code, last error and balloon notifications on failure.
- JSON settings persistence with atomic writes and corrupt-file recovery.
- Process engine for `.exe`, `.cmd`/`.bat` and `.ps1` with redirected stdout/stderr.
- Full process dashboard with Add/Edit/Delete, validation, reordering and stop-on-delete.
- Terminal-style output viewer with ANSI colors, font sizing, pause/follow and save-to-file.
- Settings dialog with Windows-autostart toggle, path-mismatch warning and one-click repair.
- Per-process autostart with ordered startup, single-instance enforcement and rolling logs.

A manual QA checklist is available in [`docs/test-checklist.md`](docs/test-checklist.md).
Still to come: single-file publishing (Phase 9).

## Documentation

- [Implementation plan](PLAN.md)
- [Functional and non-functional specification](docs/specification.md)
- [Architecture and technical design](docs/architecture.md)
- [Data model, settings schema and state machine](docs/data-model.md)
- [UI design](docs/ui-design.md)

## Build and run

```powershell
dotnet restore
dotnet build
dotnet run --project src/raccoonwatch/raccoonwatch.csproj
dotnet test
```

Publishing:

```powershell
./scripts/publish.ps1                              # framework-dependent single file
./scripts/publish.ps1 -SelfContained -Version 1.0.0 # self-contained single file + zip
```

Output goes to `artifacts/publish/<runtime>/raccoonwatch.exe`; a zip is written to
`artifacts/raccoonwatch-<version>-<runtime>.zip` unless `-SkipZip` is used.

## Assets

The tray and form icon will be generated from `raccoonwatch_icon_64x64.png` and
`raccoonwatch_icon_1254x1254.png` (provided in the repository root) into
`assets/raccoonwatch.ico` during Phase 1.

## Phase 0 decisions (approved)

The following design decisions were reviewed and approved:

1. Windows Forms is the proposed UI framework (native tray support). Acceptable, or
   prefer WPF / another toolkit?
2. PowerShell scripts will be launched with
   `powershell.exe -NoProfile -ExecutionPolicy Bypass -File ...` to avoid execution
   policy failures. Acceptable for this tool?
3. App autostart uses the per-user HKCU Run registry key (no admin rights required).
   Acceptable, or prefer a Startup-folder shortcut?
4. Stop behavior: try graceful `CloseMainWindow()` first, then force-kill the process
   tree after a short timeout. Acceptable?
5. Settings/log locations: `%APPDATA%\raccoonwatch\` and
   `%LOCALAPPDATA%\raccoonwatch\logs\`. Acceptable?
6. On raccoonwatch exit, should tracked processes be stopped by default? Current
   proposal: configurable, default **do not stop**.
