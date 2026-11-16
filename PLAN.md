# raccoonwatch — Implementation Plan

## 1. Goal

Build `raccoonwatch`, a modern C# Windows application that lives in the system tray
(notification area) and lets the user start, restart, stop and monitor a configured
list of processes (`.exe`, `cmd` scripts and PowerShell scripts). The tray context
menu is the primary monitoring surface: every configured process appears as a menu
item with its live status. The user can also open a terminal-like window to view a
process output, manage the process list, and enable autostart both for raccoonwatch
itself and for individual processes.

Target folder: `D:\Projects\raccoonwatch\`
Existing assets: `raccoonwatch_icon_64x64.png`, `raccoonwatch_icon_1254x1254.png`
(to be converted into an `.ico` for the tray and forms).

## 2. Proposed technical choices

| Area | Choice | Rationale |
|---|---|---|
| Runtime / language | .NET 10 (`net10.0-windows`), C# with nullable reference types, implicit usings, file-scoped namespaces | "Modern C#", already installed SDK |
| UI framework | Windows Forms with `NotifyIcon` + `ApplicationContext` | Native, lightweight system-tray support; no extra UI dependency for menus |
| Styling | Flat/Segoe UI, dark-mode-aware palette, owner-drawn menu glyphs where needed | Modern look without a heavy theme library |
| Architecture | Thin WinForms UI + service layer, constructor injection via `Microsoft.Extensions.DependencyInjection` | Keeps state/monitoring logic testable and independent of the UI |
| Process control | `System.Diagnostics.Process`, redirected stdout/stderr, `PeriodicTimer` for polling | No external process-management library required |
| Persistence | JSON file at `%APPDATA%\raccoonwatch\settings.json` using `System.Text.Json` | Human-readable, no database, easy backup |
| App autostart | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` registry value | Standard per-user Windows startup registration |
| Logging | `Microsoft.Extensions.Logging` with a rolling file under `%LOCALAPPDATA%\raccoonwatch\logs\` | Diagnosability without a heavy logging framework |
| Packaging | Single-file `dotnet publish` for `win-x64`; app icon from the provided PNGs | Simple distribution |

Windows-only APIs are acceptable because the product is explicitly a Windows tray app.

## 3. Phases

### Phase 0 — Documentation and Specification (execute first, then pause for review)

Goal: freeze the requirements and design before writing implementation code.

Deliverables (committed together):
- `README.md` — project purpose, quick overview, build/run commands, status.
- `docs/specification.md` — personas, functional requirements (FR-xx), non-functional
  requirements (NFR-xx), detailed use cases and acceptance criteria for every feature
  requested (tray monitoring, process CRUD for exe/cmd/ps1, output viewer, both
  autostart mechanisms, status tracking).
- `docs/architecture.md` — component diagram (text), project/folder layout, service
  responsibilities, threading model, process launch/stop strategies, data flow,
  error-handling and single-instance approach.
- `docs/data-model.md` — `ProcessDefinition`, runtime status state machine, JSON
  settings schema, registry key, file paths.
- `docs/ui-design.md` — tray menu layout, forms (Manage Processes, Process Output,
  Settings), interactions, icon usage, keyboard/UX notes.

Exit criterion: the user reviews and approves (or requests changes to) these documents.
**The agent stops after this phase and waits for the user's review.**

### Phase 1 — Solution scaffolding and core infrastructure

- Create `raccoonwatch.sln` and `src/raccoonwatch/raccoonwatch.csproj` (`net10.0-windows`,
  `UseWindowsForms`, nullable, implicit usings, app icon).
- Convert the provided PNGs into `assets/raccoonwatch.ico` and embed it.
- Add `.gitignore`, `Directory.Build.props`, dependency injection bootstrap,
  logging, single-instance `Mutex` guard, `Program.cs` / `TrayApplicationContext`.
- Add xUnit test project `tests/raccoonwatch.Tests`.
- Commit.

### Phase 2 — Domain models and settings persistence

- Implement `ProcessDefinition`, `ProcessKind`, `ProcessStatus`, `RuntimeState`,
  `AppSettings` with validation.
- Implement `ISettingsService` with atomic JSON load/save, default creation, and
  schema versioning.
- Implement path/argument validation helpers for exe, `.cmd`/`.bat` and `.ps1`.
- Unit tests for serialization round-trip and validation.
- Commit.

### Phase 3 — Process management engine

- Implement `IProcessManager`: start, stop (graceful close then kill tree), restart,
  start/stop all, polling with `PeriodicTimer`, status transitions and events.
- Implement command construction per kind:
  - Executable: direct `ProcessStartInfo`.
  - Cmd/bat: `cmd.exe /c "<script>"`.
  - PowerShell: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File "<script>"`
    (with optional `pwsh` detection).
- Redirect stdout/stderr into per-process ring buffers; emit output events.
- Implement `IOutputStore` with bounded history and thread-safe subscribers.
- Unit tests for command-line building and state transitions; manual smoke test with
  sample processes.
- Commit.

### Phase 4 — Tray UI and live monitoring

- Build the `NotifyIcon` context menu: dynamic items "Name — Status", icons/glyphs,
  Start/Stop/Restart submenu or per-item verbs, Manage Processes, Settings, Exit.
- Double-click tray icon opens Manage Processes.
- Thread-safe UI refresh on process/output events; status colors and tooltips.
- Toast notifications for failures (optional, configurable).
- Commit.

### Phase 5 — Process management UI

- `ManageProcessesForm`: DataGridView with name, type, command, arguments, working
  directory, autostart flag, live status; Add/Edit/Delete dialogs; validation;
  reordering/start order; Save/Cancel semantics backed by `ISettingsService`.
- Confirmations for delete and for stopping running processes; inline error messages.
- Commit.

### Phase 6 — Terminal-like output viewer

- `ProcessOutputForm`: dark terminal-style `RichTextBox` (monospaced font), ANSI
  color escape parsing, follow-tail autoscroll, pause/resume, clear, save to file,
  separate stdout/stderr coloring, process selector.
- Handles high-frequency output efficiently (batched UI updates).
- Commit.

### Phase 7 — Autostart features

- `IAutostartService`: read/write the HKCU Run value, report state, handle
  missing/renamed executable paths.
- Settings toggle "Start raccoonwatch with Windows" wired to the service.
- Per-process `AutoStartOnAppLaunch`: on startup, start flagged processes in
  configured order; status reflected in tray as soon as the icon appears.
- Commit.

### Phase 8 — Polish and quality

- Exception handling, graceful shutdown (stop tracked processes if configured),
  resource cleanup, accessibility, DPI awareness, logging review.
- Sample README screenshots, manual test checklist `docs/test-checklist.md`.
- Commit.

### Phase 9 — Release

- Build/publish scripts (`scripts/publish.ps1`), single-file publish, icon verification,
  version stamping, optional self-contained build.
- Final tag/commit.

## 4. Definition of done

- All requested features implemented and manually verified on Windows.
- Tray menu shows every configured process with correct live status.
- exe/cmd/powershell processes can be added, edited, deleted, started, restarted,
  stopped and have their output viewed in a terminal-like window.
- Both autostart mechanisms work and are configurable.
- Settings survive application and machine restarts.
- Tests pass (`dotnet test`) and a single-file publish starts cleanly.

## 5. Current status

- [x] Plan written and committed.
- [x] Phase 0 — Documentation and Specification — executed and approved.
- [x] Phase 1 — Solution scaffolding and core infrastructure — implemented.
- [x] Phase 2 — Domain models and settings persistence — implemented.
- [x] Phase 3 — Process management engine — implemented.
- [x] Phase 4 — Tray UI and live monitoring — implemented.
- [x] Phase 5 — Process management UI — implemented.
- [x] Phase 6 — Terminal-like output viewer — implemented.
- [x] Phase 7 — Autostart features — implemented.
- [ ] Phases 8–9 — pending.
