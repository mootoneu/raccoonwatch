using System.Drawing;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Raccoonwatch.Infrastructure;
using Raccoonwatch.Models;
using Raccoonwatch.Services;
using Raccoonwatch.UI;

namespace Raccoonwatch;

/// <summary>Owns the tray icon and builds its live context menu.</summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly IServiceProvider _services;
    private readonly IAppController _controller;
    private readonly ISettingsService _settings;
    private readonly IProcessManager _processes;
    private readonly IOutputStore _output;
    private readonly ILogger<TrayApplicationContext> _logger;
    private readonly SingleInstanceGuard _instanceGuard;
    private readonly NotifyIcon _trayIcon;
    private readonly Control _invoker = new();

    private ManageProcessesForm? _manageForm;
    private SettingsForm? _settingsForm;
    private AboutForm? _aboutForm;
    private readonly Dictionary<Guid, ProcessOutputForm> _outputForms = [];

    public TrayApplicationContext(
        IServiceProvider services,
        IAppController controller,
        ISettingsService settings,
        IProcessManager processes,
        IOutputStore output,
        ILogger<TrayApplicationContext> logger,
        SingleInstanceGuard instanceGuard)
    {
        _services = services;
        _controller = controller;
        _settings = settings;
        _processes = processes;
        _output = output;
        _logger = logger;
        _instanceGuard = instanceGuard;

        _ = _invoker.Handle; // Force creation of the handle used for UI-thread marshalling.

        _trayIcon = new NotifyIcon
        {
            Icon = AppIcon.Create(),
            Text = "raccoonwatch",
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip { ShowImageMargin = false }
        };
        _trayIcon.ContextMenuStrip.Opening += (_, _) => RebuildMenu(_trayIcon.ContextMenuStrip!);
        _trayIcon.DoubleClick += (_, _) => ShowManageProcesses();

        _processes.StateChanged += OnProcessStateChanged;
        _settings.Changed += OnSettingsChanged;
        _instanceGuard.ShowManageRequested += OnShowManageRequested;

        RefreshTooltip();
    }

    public void ShowManageProcesses()
    {
        RunOnUiThread(() =>
        {
            _manageForm ??= new ManageProcessesForm(_settings, _processes, ShowOutput);
            _manageForm.Show();
            _manageForm.BringToFront();
        });
    }

    public void ShowSettings()
    {
        RunOnUiThread(() =>
        {
            _settingsForm ??= _services.GetRequiredService<SettingsForm>();
            _settingsForm.Show();
            _settingsForm.BringToFront();
        });
    }

    public void ShowAbout()
    {
        RunOnUiThread(() =>
        {
            _aboutForm ??= new AboutForm();
            _aboutForm.Show();
            _aboutForm.BringToFront();
        });
    }

    public void ShowEdit(Guid definitionId)
    {
        var definition = _settings.Settings.Processes.FirstOrDefault(p => p.Id == definitionId);
        if (definition is null)
        {
            return;
        }

        RunOnUiThread(() =>
        {
            using var dialog = new ProcessEditForm(definition, _settings);
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                _settings.AddOrUpdate(dialog.Result);
            }
        });
    }

    public void ShowOutput(Guid definitionId)
    {
        RunOnUiThread(() =>
        {
            if (!_outputForms.TryGetValue(definitionId, out var form) || form.IsDisposed)
            {
                form = new ProcessOutputForm(_output, _settings);
                _outputForms[definitionId] = form;
            }

            form.SetProcess(definitionId);
            form.Show();
            form.BringToFront();
        });
    }

    public async Task ExitApplicationAsync()
    {
        if (_settings.Settings.StopProcessesOnExit)
        {
            var running = _settings.Settings.Processes
                .Count(p => IsActive(_processes.GetState(p.Id).Status));
            if (running > 0)
            {
                var answer = MessageBox.Show(
                    $"Exiting raccoonwatch will stop {running} running process(es). Continue?",
                    "raccoonwatch",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (answer != DialogResult.Yes)
                {
                    return;
                }
            }
        }

        try
        {
            await _controller.ExitAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during shutdown.");
        }
        finally
        {
            _trayIcon.Visible = false;
            ExitThread();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _processes.StateChanged -= OnProcessStateChanged;
            _settings.Changed -= OnSettingsChanged;
            _instanceGuard.ShowManageRequested -= OnShowManageRequested;

            foreach (var form in _outputForms.Values)
            {
                form.Dispose();
            }

            _manageForm?.Dispose();
            _settingsForm?.Dispose();
            _aboutForm?.Dispose();
            _trayIcon.ContextMenuStrip?.Dispose();
            _trayIcon.Dispose();
            _invoker.Dispose();
        }

        base.Dispose(disposing);
    }

    private static bool IsActive(ProcessStatus status) =>
        status is ProcessStatus.Starting or ProcessStatus.Running or ProcessStatus.Stopping;

    private void RebuildMenu(ContextMenuStrip menu)
    {
        menu.Items.Clear();

        var definitions = _settings.Settings.Processes
            .OrderBy(d => d.StartOrder)
            .ThenBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase);

        var any = false;
        foreach (var definition in definitions)
        {
            any = true;
            menu.Items.Add(BuildProcessItem(definition));
        }

        if (!any)
        {
            var empty = new ToolStripMenuItem("No processes configured") { Enabled = false };
            menu.Items.Add(empty);
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Start All", null, (_, _) => _ = StartAllAsync()));
        menu.Items.Add(new ToolStripMenuItem("Stop All", null, (_, _) => _ = StopAllAsync()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Manage Processes…", null, (_, _) => ShowManageProcesses()));
        menu.Items.Add(new ToolStripMenuItem("Settings…", null, (_, _) => ShowSettings()));
        menu.Items.Add(new ToolStripMenuItem("About…", null, (_, _) => ShowAbout()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => _ = ExitApplicationAsync()));
    }

    private ToolStripMenuItem BuildProcessItem(ProcessDefinition definition)
    {
        var state = _processes.GetState(definition.Id);
        var item = new ToolStripMenuItem(
            $"{TrayGlyph.For(state.Status)}  {definition.DisplayName} — {state.Status}");

        item.DropDownItems.Add(new ToolStripMenuItem($"Status: {state.Status}") { Enabled = false });

        if (state.Status == ProcessStatus.Running && state.Uptime is { } uptime)
        {
            item.DropDownItems.Add(new ToolStripMenuItem(
                $"PID {state.ProcessId} · uptime {uptime:hh\\:mm\\:ss}") { Enabled = false });
        }
        else if (state is { Status: ProcessStatus.Exited or ProcessStatus.Failed, ExitCode: { } code })
        {
            item.DropDownItems.Add(new ToolStripMenuItem($"Exit code: {code}") { Enabled = false });
        }

        if (!string.IsNullOrWhiteSpace(state.LastError))
        {
            item.DropDownItems.Add(new ToolStripMenuItem($"Error: {state.LastError}") { Enabled = false });
        }

        item.DropDownItems.Add(new ToolStripSeparator());

        var start = new ToolStripMenuItem("Start", null, (_, _) => _ = ExecuteAsync(() => _processes.StartAsync(definition)))
        {
            Enabled = state.Status is ProcessStatus.Stopped or ProcessStatus.Exited or
                      ProcessStatus.Failed or ProcessStatus.Unknown
        };
        var restart = new ToolStripMenuItem("Restart", null, (_, _) => _ = ExecuteAsync(() => _processes.RestartAsync(definition)))
        {
            Enabled = state.Status is ProcessStatus.Running or ProcessStatus.Exited or ProcessStatus.Failed
        };
        var stop = new ToolStripMenuItem("Stop", null, (_, _) => _ = ExecuteAsync(() => _processes.StopAsync(definition.Id)))
        {
            Enabled = state.Status is ProcessStatus.Running or ProcessStatus.Starting or ProcessStatus.Stopping
        };
        item.DropDownItems.Add(start);
        item.DropDownItems.Add(restart);
        item.DropDownItems.Add(stop);

        item.DropDownItems.Add(new ToolStripSeparator());
        item.DropDownItems.Add(new ToolStripMenuItem("View Output…", null, (_, _) => ShowOutput(definition.Id))
        {
            Enabled = state.Status == ProcessStatus.Running || _output.GetSnapshot(definition.Id).Count > 0
        });
        item.DropDownItems.Add(new ToolStripMenuItem("Edit…", null, (_, _) => ShowEdit(definition.Id)));
        item.DropDownItems.Add(BuildOpenProjectFolderItem(definition, OpenProjectFolder));
        item.DropDownItems.Add(BuildUpdateItem(definition, process => _ = RunUpdateAsync(process)));

        return item;
    }

    /// <summary>Builds the per-process "Update" menu entry.</summary>
    internal static ToolStripMenuItem BuildUpdateItem(
        ProcessDefinition definition, Action<ProcessDefinition> update)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(update);

        var configured = !string.IsNullOrWhiteSpace(definition.UpdateCommandPath);
        var item = new ToolStripMenuItem("Update")
        {
            Enabled = configured,
            ToolTipText = configured
                ? $"Run {definition.UpdateCommandPath} {definition.UpdateArguments}".TrimEnd()
                : "No update command is configured for this process."
        };

        if (configured)
        {
            item.Click += (_, _) => update(definition);
        }

        return item;
    }

    private async Task RunUpdateAsync(ProcessDefinition definition)
    {
        try
        {
            using var process = UpdateCommandLauncher.Launch(
                definition.UpdateCommandPath, definition.UpdateArguments);
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                _logger.LogInformation(
                    "Update command '{Command}' for '{Process}' completed successfully.",
                    definition.UpdateCommandPath,
                    definition.DisplayName);
            }
            else
            {
                _logger.LogWarning(
                    "Update command '{Command}' for '{Process}' exited with code {ExitCode}.",
                    definition.UpdateCommandPath,
                    definition.DisplayName,
                    process.ExitCode);
                NotifyError($"Update of '{definition.DisplayName}' exited with code {process.ExitCode}.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not run update command for '{Process}'.", definition.DisplayName);
            NotifyError($"Could not update '{definition.DisplayName}': {ex.Message}");
        }
    }

    /// <summary>Builds the per-process "Open Project Folder" menu entry.</summary>
    internal static ToolStripMenuItem BuildOpenProjectFolderItem(
        ProcessDefinition definition, Action<string> openFolder)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(openFolder);

        var folder = ProjectFolderOpener.ResolveFolder(definition);
        var item = new ToolStripMenuItem("Open Project Folder")
        {
            Enabled = folder is not null,
            ToolTipText = folder ?? "No project folder is configured for this process."
        };

        if (folder is not null)
        {
            item.Click += (_, _) => openFolder(folder);
        }

        return item;
    }

    private void OpenProjectFolder(string folder)
    {
        try
        {
            ProjectFolderOpener.Open(folder);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not open project folder '{Folder}'.", folder);
            NotifyError($"Could not open project folder '{folder}': {ex.Message}");
        }
    }

    private async Task StartAllAsync()
    {
        try
        {
            await _processes.StartAllAsync(_settings.Settings.Processes);
        }
        catch (Exception ex)
        {
            NotifyError(ex.Message);
        }
    }

    private async Task StopAllAsync()
    {
        try
        {
            await _processes.StopAllAsync();
        }
        catch (Exception ex)
        {
            NotifyError(ex.Message);
        }
    }

    private async Task ExecuteAsync(Func<Task<ProcessResult>> action)
    {
        try
        {
            var result = await action();
            if (!result.Success)
            {
                NotifyError(result.Error ?? "Operation failed.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tray action failed.");
            NotifyError(ex.Message);
        }
    }

    private void NotifyError(string message)
    {
        if (!_settings.Settings.ShowNotifications)
        {
            return;
        }

        RunOnUiThread(() =>
            _trayIcon.ShowBalloonTip(4000, "raccoonwatch", message, ToolTipIcon.Error));
    }

    private void OnProcessStateChanged(object? sender, ProcessStateChangedEventArgs e)
    {
        if (e.State.Status == ProcessStatus.Failed)
        {
            var definition = _settings.Settings.Processes.FirstOrDefault(p => p.Id == e.DefinitionId);
            NotifyError($"{definition?.DisplayName ?? "Process"} failed: {e.State.LastError}");
        }

        RunOnUiThread(RefreshTooltip);
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => RunOnUiThread(RefreshTooltip);

    private void OnShowManageRequested() => ShowManageProcesses();

    private void RefreshTooltip()
    {
        var definitions = _settings.Settings.Processes;
        var running = definitions.Count(d => _processes.GetState(d.Id).Status == ProcessStatus.Running);
        _trayIcon.Text = $"raccoonwatch — {running} running / {definitions.Count} configured";
    }

    private void RunOnUiThread(Action action)
    {
        if (_invoker.IsDisposed)
        {
            return;
        }

        if (_invoker.InvokeRequired)
        {
            _invoker.BeginInvoke(action);
        }
        else
        {
            action();
        }
    }
}
