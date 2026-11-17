# raccoonwatch — Manual Test Checklist (Phase 8)

Run against a build from `src/raccoonwatch/bin/Debug/net10.0-windows/raccoonwatch.exe`
(and repeat key items against the published single-file build from Phase 9).

## 1. Startup and tray presence

- [ ] The application starts with no main window; only the raccoon tray icon appears.
- [ ] Tray tooltip reads `raccoonwatch — 0 running / 0 configured` on a fresh profile.
- [ ] Double-clicking the icon opens Manage Processes.
- [ ] Right-clicking shows Start All, Stop All, Manage Processes, Settings, About, Exit.
- [ ] Launching a second instance does not create a second tray icon; it focuses Manage Processes.
- [ ] About shows `Raccoon Watch`, the tagline, the 400 px artwork and the GitHub link with icon; clicking the link opens the repository.
- [ ] Exit removes the tray icon and the process terminates.

## 2. Process CRUD (Phase 5)

- [ ] Add an Executable definition (existing `.exe`) — no validation errors; row appears.
- [ ] Add a Cmd definition (`.cmd`/`.bat`) with arguments — row appears.
- [ ] Add a PowerShell definition (`.ps1`) — row appears.
- [ ] Duplicate display name, missing file and wrong extension each show inline errors and do not save.
- [ ] Edit a definition and confirm the tray menu and settings.json reflect the change.
- [ ] Move Up/Move Down changes start order and persists after restart.
- [ ] Delete a stopped definition — confirmation appears and row disappears.
- [ ] Delete a running definition — confirmation warns it will be stopped; process is stopped and removed.

## 3. Start / stop / restart and status

- [ ] Start a process from the tray submenu; status becomes Starting then Running with PID/uptime.
- [ ] Stop it; status becomes Stopped and PID is cleared.
- [ ] Restart it; output shows a `--- restarted at … ---` separator.
- [ ] Start All respects start order; Stop All stops in reverse order.
- [ ] Kill a process externally via Task Manager; within one poll interval the tray shows Exited with exit code.
- [ ] Launch a script that exits with a non-zero code immediately; status becomes Failed with an error.
- [ ] Start a process whose file was deleted after configuration; failure is reported via balloon/log.

## 4. Output viewer (Phase 6)

- [ ] View Output streams new lines and follows the tail.
- [ ] Scrolling up or enabling Pause stops auto-follow.
- [ ] Stderr lines are red; `[ERR]` prefix is present.
- [ ] ANSI colored output (e.g. `\u001b[31mred\u001b[0m`) renders in color and escape codes are stripped.
- [ ] Font size control changes the monospaced font; timestamps toggle works.
- [ ] Clear empties the view and the stored buffer; Save writes a UTF-8 `.log` file.
- [ ] Closing and reopening the viewer shows the buffered history, including after process exit.

## 5. Autostart (Phase 7)

- [ ] Settings → "Start raccoonwatch with Windows" writes
      `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\raccoonwatch` with a quoted path.
- [ ] Toggling off removes the registry value.
- [ ] Simulate a stale path (edit registry to an old exe path): warning and Repair button appear; Repair fixes it.
- [ ] Mark two processes "Start with raccoonwatch" with orders 1 and 2; restart raccoonwatch.
- [ ] Both start in order; failing the first does not prevent the second.
- [ ] After login on Windows, raccoonwatch starts and the flagged processes run.

## 6. Settings, persistence, resilience

- [ ] Change polling interval, history size, notifications and log level; Apply persists to settings.json.
- [ ] Restart the app; all settings are restored.
- [ ] Corrupt settings.json manually; app starts with defaults and creates settings.json.bad.
- [ ] Logs appear under `%LOCALAPPDATA%\raccoonwatch\logs\` and respect the selected level.
- [ ] "Open log folder" opens that directory in Explorer.
- [ ] With "Stop tracked processes when raccoonwatch exits" enabled, Exit asks for confirmation and stops them.

## 7. Scaling and accessibility

- [ ] At 100%, 125%, 150% and 200% Windows scaling the tray text and forms remain readable and unclipped.
- [ ] All forms are keyboard navigable via Tab; Enter/Space activate focused buttons.
- [ ] In Manage Processes, Delete removes the selected definition and Enter opens its edit dialog.
- [ ] Status is understandable from text/glyphs, not color alone.

## 8. Release build (Phase 9)

- [ ] `scripts/publish.ps1` produces a single-file exe in `artifacts/publish/win-x64`.
- [ ] The published exe launches on a clean user profile with the correct tray icon.
- [ ] The published exe can start/stop a sample process and save output.
- [ ] The published exe shows artwork and GitHub icon in the About window (embedded resources).
