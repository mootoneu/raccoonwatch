# raccoonwatch

`raccoonwatch` is a Windows system-tray application (Windows 10/11, modern C#/.NET 10)
that starts, restarts, stops and monitors a user-managed list of processes. Processes
can be executables (`.exe`), `cmd`/`.bat` scripts or PowerShell (`.ps1`) scripts. The
tray context menu is the main dashboard: each process is shown with its live status.
A terminal-like window lets the user inspect a process's output, and both the app and
individual processes can be configured to start automatically with Windows / with
raccoonwatch.

> **Status:** Phase 0 (Documentation and Specification) is complete and awaiting review.
> No implementation code has been written yet. See [`PLAN.md`](PLAN.md) for the full
> roadmap and [`docs/specification.md`](docs/specification.md) for the requirements.

## Features (planned)

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

## Documentation

- [Implementation plan](PLAN.md)
- [Functional and non-functional specification](docs/specification.md)
- [Architecture and technical design](docs/architecture.md)
- [Data model, settings schema and state machine](docs/data-model.md)
- [UI design](docs/ui-design.md)

## Build and run (planned, Phase 1+)

```powershell
dotnet restore
dotnet build
dotnet run --project src/raccoonwatch/raccoonwatch.csproj
dotnet test
```

Publishing (planned, Phase 9):

```powershell
./scripts/publish.ps1
```

## Assets

The tray and form icon will be generated from `raccoonwatch_icon_64x64.png` and
`raccoonwatch_icon_1254x1254.png` (provided in the repository root) into
`assets/raccoonwatch.ico` during Phase 1.

## Review checklist / open questions for Phase 0

Please confirm or adjust the following during review:

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
