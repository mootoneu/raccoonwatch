# raccoonwatch — UI Design (Phase 0)

Version: 0.1 (draft for review)
Status: Pending approval

## 1. Design principles

- The tray is the dashboard; windows are for management and details.
- At a glance the user must see which processes are running and which are not.
- No modal dialogs for status changes; notifications and inline errors only.
- Follow standard Win32/WinForms conventions: system font (Segoe UI), standard
  buttons (`Add…`, `Edit…`, `Delete`, `OK`, `Cancel`, `Apply`), keyboard navigation.

## 2. Tray icon

- Icon: raccoon with binoculars, generated from the provided PNGs into
  `assets/raccoonwatch.ico` (16/24/32/48 px images for tray and title bars).
- Tooltip (max ~63 chars): `raccoonwatch — 2 running / 5 configured`.
- Optional transient status overlays (small colored dot) or balloon notifications for
  failures; base icon remains recognizable.

## 3. Tray context menu layout

```
┌───────────────────────────────────────────┐
│ ● API server — Running                ▶ │  ← submenu
│ ○ Backup job — Stopped                ▶ │
│ ▲ Worker — Starting                   ▶ │
│ ✖ Legacy tool — Failed                ▶ │
├───────────────────────────────────────────┤
│ Start All                                 │
│ Stop All                                  │
├───────────────────────────────────────────┤
│ Manage Processes…                         │
│ Settings…                                 │
│ About…                                    │
├───────────────────────────────────────────┤
│ Exit                                      │
└───────────────────────────────────────────┘
```

Per-process submenu (example for Running):

```
┌─────────────────────────────────┐
│ Status: Running (PID 1234)      │ ← disabled info line
│ Uptime: 00:42:13                │ ← disabled info line
├─────────────────────────────────┤
│ Restart                         │
│ Stop                            │
├─────────────────────────────────┤
│ View Output…                    │
│ Edit…                           │
└─────────────────────────────────┘
```

- For `Stopped`/`Exited`/`Failed`: Start enabled, Stop/Restart disabled.
- For `Starting`/`Stopping`: Start/Stop/Restart disabled until transition completes.
- Glyphs/colors: `●` green Running, `○` grey Stopped, `▲` amber Starting/Stopping,
  `✖` red Failed/Exited. Menu item text uses status suffix as shown.
- Entries are ordered by `StartOrder`, then name.
- Double-click on the tray icon opens Manage Processes; single right-click opens menu.

## 4. Manage Processes form

Modeless window, resizable, minimum 900×520. Toolbar buttons; DataGridView with
double-buffered, alternating row colors.

```
┌──────────────────────────────────────────────────────────────┐
│ Manage Processes                                    _ □ X    │
├──────────────────────────────────────────────────────────────┤
│ [Add…] [Edit…] [Delete] [Start] [Stop] [Restart] [View Output]│
├──────────────────────────────────────────────────────────────┤
│ Status │ Name        │ Type │ Command / Arguments │ Order│Auto│
│ ● Run  │ API server  │ Exe  │ C:\tools\api.exe... │  1   │ ✓  │
│ ○ Stop │ Backup job  │ Cmd  │ C:\scripts\backup.. │  2   │    │
│ ✖ Fail │ Worker      │ PS1  │ C:\scripts\worker.. │  3   │ ✓  │
├──────────────────────────────────────────────────────────────┤
│ Tip: double-click a row to edit. Changes are saved on OK.    │
│                                        [Close]  [Apply] [OK] │
└──────────────────────────────────────────────────────────────┘
```

- Editable fields/flags are only in the Add/Edit dialog; the grid is read-only except
  for ordering via up/down toolbar actions (optional).
- "Auto" column reflects `AutoStartOnLaunch`.
- Live status column updates from `IProcessManager.StateChanged`.
- Delete asks: `Delete "Backup job"? It is currently running and will be stopped.`
- Closing with unsaved changes prompts to Save/Discard/Cancel.

## 5. Process edit dialog (`Add` / `Edit`)

```
┌──────────────────────────────────────────────┐
│ Add Process                          _ □ X   │
├──────────────────────────────────────────────┤
│ Display name:  [API server               ]   │
│ Kind:          [Executable ▼]               │
│ Command path:  [C:\tools\api.exe       ][...]│
│ Arguments:     [--port 5000               ]  │
│ Working dir:   [C:\tools                ][...]│
│ Start order:   [1]                           │
│ ☑ Start this process when raccoonwatch starts│
│ Notes:         [                            ]│
│                                              │
│ ⓘ PowerShell scripts run with -ExecutionPolicy Bypass.
│ Validation message (inline, red)             │
│                         [OK]  [Cancel]        │
└──────────────────────────────────────────────┘
```

- `Kind` combo switches validation and file picker filter (`.exe` / `.cmd;*.bat` /
  `.ps1`).
- Inline validation runs on OK and on field blur; errors never throw a message box
  storm.
- Editing a running process: changing command/working directory requires a restart;
  the dialog shows that hint on OK.

## 6. Process output form (terminal-like)

```
┌──────────────────────────────────────────────────────────────┐
│ Output — API server                                 _ □ X    │
├──────────────────────────────────────────────────────────────┤
│ Process: [API server ▼]  Font size: [11]  [Pause] [Clear]    │
│                                             [Save…] [Close]  │
├──────────────────────────────────────────────────────────────┤
│ > starting server...                                         │
│ > listening on http://localhost:5000                          │
│ [ERR] failed to load config (falling back to defaults)        │
│ ▌                                                             │
└──────────────────────────────────────────────────────────────┘
```

- Dark background (`#1E1E1E`), monospace font (Cascadia Mono / Consolas fallback),
  stdout light grey, stderr soft red, ANSI SGR colors parsed.
- Auto-scroll follows tail until the user scrolls up (then a "Resume follow" hint) or
  presses Pause. On restart a separator line is inserted.
- Save writes the current buffer to a UTF-8 `.log` file via a Save dialog.
- Buffered output is shown for processes that already exited.

## 7. Settings form

```
┌──────────────────────────────────────────────┐
│ Settings                            _ □ X    │
├──────────────────────────────────────────────┤
│ ☑ Start raccoonwatch with Windows            │
│   (current entry: C:\path\raccoonwatch.exe)  │
│ ☐ Stop tracked processes when exiting        │
│ ☑ Show notifications on failures             │
│ Status polling interval: [2000] ms           │
│ Output history per process: [5000] lines     │
│ Log level: [Information ▼]  [Open log folder]│
│                         [Apply] [OK] [Cancel]│
└──────────────────────────────────────────────┘
```

- Registry state is the source of truth for the autostart checkbox; a mismatched path
  shows a repair link.
- Changing polling/history takes effect immediately (history re-bounds buffers).

## 8. About window

Accessible from the tray via `About…`. Displays, in rows:

```
        Raccoon Watch
  Manage your processes easily
   [400 px raccoon artwork]
 [GitHub icon] https://github.com/mootoneu/raccoonwatch.git
                                  [Close]
```

The artwork and GitHub icon are embedded resources so they are present in
single-file publishes. Clicking the link opens the repository in the default browser.

## 9. Accessibility and keyboard support

- All controls reachable via Tab with logical order; `&` mnemonics on buttons/labels.
- DataGridView supports arrow keys, Delete key for delete action, Enter for edit.
- Tray menu uses standard Win32 mnemonics where practical and is fully keyboard
  navigable after opening with the keyboard.
- Status is conveyed by text and glyph, not color alone.
- Forms respect system DPI and scale at 100/125/150/200%.

## 10. Window lifecycle and threading UX

- Closing Manage/Output/Settings hides or closes only that window; the tray app stays.
- Form updates are marshalled to the UI thread; slow operations show a wait cursor or
  become async with disabled buttons rather than freezing.
- On Exit: confirm if `StopProcessesOnExit` is enabled ("This will stop N processes"),
  persist settings, dispose resources, remove icon.
