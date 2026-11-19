# raccoonwatch — Software Specification (Phase 0)

Version: 1.0
Status: Approved — implemented in Phases 1–9, with later additions documented below.

## 1. Purpose and scope

`raccoonwatch` is a lightweight Windows tray application for users who run a small
set of long-lived local processes (services, dev servers, bots, scripts) and want a
single place to control and observe them. It is **not** a Windows service manager: it
runs in the user session, requires no administrator privileges, and manages only the
processes it started itself.

### 1.1 In scope

- A tray icon with a dynamic context menu showing every configured process and status.
- CRUD management of process definitions for `.exe`, `.cmd`/`.bat` and `.ps1`.
- Start / stop / restart of individual processes and bulk start/stop.
- Runtime status and metadata tracking (PID, start time, exit code, uptime, errors).
- A terminal-like output window per running process.
- Autostart of raccoonwatch with Windows.
- Autostart of marked processes when raccoonwatch starts.
- Persistent settings, single-instance behavior, basic logging and error reporting.

### 1.2 Out of scope (for the first release)

- Administrator/elevated processes and Windows services.
- Remote machines, process dependency graphs, scheduling, health-check HTTP probes.
- Automatic crash-restart policies beyond a manual restart (may be a later enhancement).
- Input/stdin sending to processes; output viewing is read-only.
- Multi-user/roaming settings.

## 2. Personas

- **Power user / developer (primary):** runs several local scripts or tools, wants
  quick tray access and reliable restart/stop without opening Task Manager.
- **Occasional user (secondary):** configures raccoonwatch once and expects the right
  processes to be running after login without interacting with the app.

## 3. Functional requirements

### FR-1 — Tray presence and basic interaction

- FR-1.1 The application starts minimized to the notification area and shows no main
  window by default; a single tray icon uses the raccoon asset.
- FR-1.2 The tray tooltip shows a summary, e.g. `raccoonwatch — 2 running / 5 configured`.
- FR-1.3 Left double-click on the icon opens the Manage Processes window.
- FR-1.4 Right-click shows the context menu defined in FR-5.
- FR-1.5 Closing any window hides it; it does not exit the application. Exit is an
  explicit menu action.
- FR-1.6 Only one instance may run; launching a second instance brings the first
  instance's Manage Processes window to the foreground.

### FR-2 — Process definitions (CRUD)

- FR-2.1 A process definition has: unique id, display name, kind (Executable, Cmd,
  PowerShell), command path, optional arguments, optional working directory, start
  order, "start with raccoonwatch" flag, optional notes.
- FR-2.2 The user can add, edit and delete definitions through the Manage Processes
  window (see UC-1, UC-8).
- FR-2.3 Validation rules:
  - Display name required, unique (case-insensitive), max 80 characters.
  - Command path required; must exist on disk at edit time (warning allowed if it is on
    a removable drive); for Executable kind it must be `.exe` (`.bat`/`.cmd` should be
    classified as Cmd).
  - Cmd kind accepts `.cmd`/`.bat`; PowerShell kind accepts `.ps1`.
  - Working directory, when supplied, must exist.
  - Start order is an integer used only when auto-starting processes.
- FR-2.4 Definitions are persisted immediately on save (FR-9).
- FR-2.5 Deleting a definition that is currently running asks for confirmation and
  stops the process before removing it.

### FR-3 — Process control

- FR-3.1 Start a single process; if it is already running, the action is disabled/no-op.
- FR-3.2 Stop a single process: attempt graceful close of its main window, then
  terminate the process tree after a fixed 5-second timeout.
- FR-3.3 Restart a single process = stop followed by start, preserving the same
  definition and output history (with a separator line marking the restart).
- FR-3.4 Start All starts every definition in ascending start order, skipping ones
  already running.
- FR-3.5 Stop All stops all running processes in descending start order.
- FR-3.6 A start attempt that fails (missing file, launch exception, immediate exit)
  is recorded as `Failed` with an error message and surfaces a notification.

### FR-4 — Status tracking

- FR-4.1 Statuses: `Unknown`, `Stopped`, `Starting`, `Running`, `Exited`, `Stopping`,
  `Failed` (see [data-model.md](data-model.md) for the state machine).
- FR-4.2 The application polls tracked processes at a configurable interval
  (default 2 s) and updates status when a process exits.
- FR-4.2a For `FastPollWindowMs` (default 120 s) after a Start, Stop or Restart the
  polling interval becomes `FastPollIntervalMs` (default 250 ms). Each further action
  restarts the window; afterwards polling returns to `PollIntervalMs`.
- FR-4.3 For every process, the runtime state stores PID, start time, exit code/time,
  uptime (computed), last error message and a count of restarts in this session.
- FR-4.4 `Exited` is distinct from `Stopped`: `Exited` means the process terminated on
  its own (an exit code is recorded), `Stopped` means raccoonwatch initiated the stop
  or the process was never started.
- FR-4.5 Status changes are reflected in the tray menu and tooltips without requiring
  the user to reopen a window.

### FR-5 — Tray context menu

- FR-5.1 The menu lists one entry per configured process in start order:
  `<status glyph> <name> — <status>` (e.g. `● API server — Running`,
  `○ Backup job — Stopped`).
- FR-5.2 Each process entry has a submenu with: Start, Restart, Stop, View Output,
  and a status line (PID / uptime / exit code when exited).
- FR-5.3 Menu items are enabled/disabled based on current status (e.g. Stop disabled
  when stopped, View Output enabled when output exists).
- FR-5.4 Below the process list: `Start All`, `Stop All`, separator,
  `Manage Processes…`, `Settings…`, `About…`, separator, `Exit`.
- FR-5.5 Status glyphs/colors: green = Running, grey = Stopped, amber = Starting/
  Stopping, red = Exited/Failed. Tooltip on the icon summarizes counts.

### FR-6 — Process output viewer

- FR-6.1 A terminal-like window shows captured stdout and stderr of a chosen process.
- FR-6.2 Output is streamed while the process runs; the view auto-scrolls ("follow
  tail") unless the user scrolls up or presses Pause.
- FR-6.3 Stdout and stderr are visually distinguishable (default: light grey vs. red);
  common ANSI color escape sequences are interpreted.
- FR-6.4 Controls: Pause/Resume follow, Clear view, Save output to file, choose process
  (dropdown), and a monospaced font with adjustable size.
- FR-6.5 A bounded in-memory history (default 5,000 lines per process, configurable)
  prevents unbounded memory growth; older lines are discarded first.
- FR-6.6 Opening the viewer for a finished process shows its buffered output.

### FR-7 — Autostart raccoonwatch with Windows

- FR-7.1 A Settings toggle writes/removes a per-user autostart entry in
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` named `raccoonwatch`, pointing
  to the currently running executable with quoted path.
- FR-7.2 The Settings window shows the current state, including entries left by an
  old install path, and allows repair/removal.
- FR-7.3 No administrator rights are required.

### FR-8 — Autostart processes with raccoonwatch

- FR-8.1 A process definition can be flagged "Start with raccoonwatch".
- FR-8.2 On application startup, flagged definitions are started automatically in
  ascending start order, after the tray icon is available.
- FR-8.3 If a flagged process fails to start, the app continues starting the remaining
  ones, marks the failure in the tray menu and logs/notifies it.
- FR-8.4 The flag is independent of the Windows autostart setting in FR-7.

### FR-9 — Settings and persistence

- FR-9.1 Settings are stored as JSON at `%APPDATA%\raccoonwatch\settings.json` and
  include: process definitions, normal polling interval, fast polling interval and
  fast polling window, output history size, notification preference, log level,
  "stop tracked processes on exit" flag, and the app autostart preference mirror.
- FR-9.2 Writes are atomic (write temporary file, then replace) so a crash cannot
  corrupt the file. A missing/corrupt file is recreated with defaults and reported.
- FR-9.3 Settings are saved immediately when the user confirms a dialog; there is no
  background polling of the settings file.

### FR-10 — Error handling and logging

- FR-10.1 Launch/stop/registry/IO errors are caught, logged, and shown to the user via
  a non-blocking notification or inline form error; they never crash the app.
- FR-10.2 Logs are written as rolling text files under
  `%LOCALAPPDATA%\raccoonwatch\logs\`, with configurable level.
- FR-10.3 An "Open log folder" action is available in Settings.

### FR-11 — About window (added after the original plan)

- FR-11.1 The tray menu includes an `About…` action.
- FR-11.2 The window displays the title `Raccoon Watch`, the subtitle
  `Manage your processes easily`, the raccoon artwork at a fixed 400 px width, and a
  clickable link to `https://github.com/mootoneu/raccoonwatch.git` with a GitHub icon.
- FR-11.3 Clicking the link opens the repository in the default browser. All artwork is
  embedded so the window works in single-file publishes.

## 4. Non-functional requirements

- **NFR-1 Platform:** Windows 10 (1809+) / Windows 11, x64; .NET 10 Windows Desktop.
- **NFR-2 Footprint:** idle CPU ~0%, working set kept modest (< ~150 MB typical);
  output buffers bounded.
- **NFR-3 Responsiveness:** tray menu opens immediately; status changes appear within
  one polling interval (250 ms during the short fast-polling window that follows a
  Start/Stop/Restart); UI never blocks on process I/O.
- **NFR-4 Security/permissions:** no elevation; only HKCU registry writes; PowerShell
  launched with `-NoProfile -ExecutionPolicy Bypass` for the target script only; no
  secrets stored in settings.
- **NFR-5 Reliability:** single instance; atomic settings writes; graceful handling of
  missing scripts; no orphaned redirected-stream readers.
- **NFR-6 Usability:** consistent Win32 conventions, keyboard navigation for all forms,
  clear status text and glyphs, DPI-aware layout.
- **NFR-7 Maintainability:** layered architecture, services behind interfaces,
  unit-testable core logic, code comments only where intent is non-obvious.
- **NFR-8 Testability:** xUnit tests for settings, command construction and state
  transitions; documented manual test checklist for tray behavior.

## 5. Use cases and acceptance criteria

### UC-1 — Add an executable process

1. User opens Manage Processes and clicks Add, selects kind Executable.
2. User enters name `API server`, command `C:\tools\api.exe`, arguments `--port 5000`,
   working directory `C:\tools`, start order `1`, checks "Start with raccoonwatch".
3. User clicks Save.

**Acceptance:** new row appears; JSON contains the definition; if raccoonwatch is
restarted, the process auto-starts and the tray shows `● API server — Running`.

### UC-2 — Add a cmd script

1. User adds kind Cmd with command `C:\scripts\backup.cmd`.
2. App validates the `.cmd` extension and stores it.

**Acceptance:** starting it launches `cmd.exe /c "C:\scripts\backup.cmd"` with
redirected output; stderr lines appear in red in the output viewer.

### UC-3 — Add a PowerShell script

1. User adds kind PowerShell with command `C:\scripts\worker.ps1`.
2. App stores the definition and launches it as
   `powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\scripts\worker.ps1"`.

**Acceptance:** script runs despite a restrictive machine execution policy; output is
captured; failure to launch is reported as `Failed`.

### UC-4 — Restart a process from the tray

1. User right-clicks the tray icon, expands `API server — Running`, clicks Restart.

**Acceptance:** process is terminated (including children), relaunched, status returns
to Running within the polling interval, output viewer shows a restart separator.

### UC-5 — View process output

1. User opens the tray menu, chooses View Output for a running process.

**Acceptance:** a terminal-like window streams new lines, follows the tail, supports
Pause/Clear/Save, and shows buffered output if the process has already exited.

### UC-6 — Enable autostart with Windows

1. User opens Settings, toggles "Start raccoonwatch with Windows", clicks Apply.

**Acceptance:** HKCU Run contains a quoted path to `raccoonwatch.exe`; toggling off
removes it; no admin prompt appears; the setting survives reboot.

### UC-7 — Mark a process to start with raccoonwatch

1. User checks the flag on two processes with start orders 1 and 2 and restarts
   raccoonwatch.

**Acceptance:** both start in the configured order and the tray menu reports their
status; a failure of the first does not prevent the second from starting.

### UC-8 — Edit and delete a process

1. User renames a process and saves; later deletes a running process.

**Acceptance:** rename is reflected in the tray menu and JSON; delete prompts for
confirmation, stops the process if running, and removes it from menu and JSON.

### UC-9 — View the About window

1. User right-clicks the tray icon and chooses About.

**Acceptance:** the title, subtitle, 400 px artwork and GitHub link with icon are shown;
clicking the link opens the repository in the default browser.

## 6. Assumptions and constraints

- Scripts are local files; no remote execution is supported.
- Redirected output requires console-style processes; GUI-only apps will simply
  produce no output.
- A process that detaches itself from the launched process tree cannot be reliably
  tracked or stopped; this limitation is documented in this specification.
- The HKCU Run entry points to the executable in place; moving/deleting the executable
  requires re-running the toggle to repair the path.
- No self-update mechanism in v1.
