<div align="center">

# Raccoon Watch

<img src="assets/raccoonwatch_icon_1254x1254.png" alt="REA launching its analysis bridge inside Hopper while inspecting a native binary" width="400" />

**raccoonwatch** is a Windows system-tray application 
that starts, restarts, stops and monitors a user-managed list of processes.

The application is made with Windows 10/11, modern C#/.NET 10. Processes
can be executables (`.exe`), `cmd`/`.bat` scripts or PowerShell (`.ps1`) scripts. The
tray context menu is the main dashboard: each process is shown with its live status.
A terminal-like window lets the user inspect a process's output, and both the app and
individual processes can be configured to start automatically with Windows / with
raccoonwatch.
</div>


> **Status:** All phases of [`PLAN.md`](PLAN.md) are implemented, tested and published.
> See [`docs/specification.md`](docs/specification.md) for the requirements.

> [!IMPORTANT]
> `raccoonwatch` was created collaboratively during an interactive development session with an AI coding assistant (OpenCode with Hy4 flash free). The repository owner supplied the idea, the raccoon artwork generated with ChatGPT and the requirements; reviewed and approved the specification and the phased plan; and directed the assistant phase by phase. The assistant produced the design documents, the C# implementation, the automated tests and the packaging scripts. The owner reviewed the resulting code and requested targeted fixes, such as the notification-area/window icon correction ; and requested new unplanned additions, such as the About window.

The GitHub mark used in the About window is GitHub's official icon, downloaded from
GitHub's public assets and used only to link to this project's repository.

This software is provided **"as is"**, without warranty of any kind. An automated test
suite and a manual [test checklist](docs/test-checklist.md) are included, but the code
has not been formally audited or certified. Review it and test it in your own
environment before relying on it to manage important processes. Note in particular
that PowerShell scripts are intentionally launched with `-ExecutionPolicy Bypass`; make
sure you understand that behavior before use.

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

## What works now

- Tray icon with dynamic right-click menu listing every configured process and status.
- Start / Stop / Restart / Start All / Stop All from the tray.
- Live status tracking (Running, Stopped, Starting, Stopping, Exited, Failed), PID,
  uptime, exit code, last error and balloon notifications on failure.
- JSON settings persistence with atomic writes and corrupt-file recovery.
- Process engine for `.exe`, `.cmd`/`.bat` and `.ps1` with redirected stdout/stderr.
- Full process dashboard with Add/Edit/Delete, validation, reordering and stop-on-delete.
- Terminal-style output viewer with ANSI colors, font sizing, pause/follow and save-to-file.
- Settings dialog with Windows-autostart toggle, path-mismatch warning and one-click repair.
- About window with the 400 px raccoon artwork, tagline and GitHub link (with icon).
- Per-process autostart with ordered startup, single-instance enforcement and rolling logs.

A manual QA checklist is available in [`docs/test-checklist.md`](docs/test-checklist.md).
Single-file publishing (framework-dependent and self-contained) is available through
`scripts/publish.ps1`.

## Documentation

- [Implementation plan](PLAN.md)
- [Functional and non-functional specification](docs/specification.md)
- [Architecture and technical design](docs/architecture.md)
- [Data model, settings schema and state machine](docs/data-model.md)
- [UI design](docs/ui-design.md)
- [Manual test checklist](docs/test-checklist.md)

## License

`raccoonwatch` is licensed under the **GNU General Public License v3.0**.
See [LICENSE](LICENSE) for the full license text.

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

The tray and form icon is generated from `assets/raccoonwatch_icon_64x64.png` and
`assets/raccoonwatch_icon_1254x1254.png` by `scripts/generate-icon.ps1` into
`assets/raccoonwatch.ico`. It uses 32-bpp BMP/DIB entries (not PNG-compressed entries)
so the Windows notification area and WinForms title bars render it correctly. The ICO
is also embedded as `Raccoonwatch.assets.raccoonwatch.ico` so single-file publishes
keep the icon.

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
