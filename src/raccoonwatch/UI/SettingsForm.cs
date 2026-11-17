using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using Raccoonwatch.Infrastructure;
using Raccoonwatch.Services;

namespace Raccoonwatch.UI;

/// <summary>User preferences, including both autostart mechanisms.</summary>
public sealed class SettingsForm : Form
{
    private readonly ISettingsService _settings;
    private readonly IAutostartService _autostart;
    private readonly IOutputStore _output;
    private readonly LoggingOptions _logging;

    private readonly CheckBox _startWithWindows = new() { Text = "Start raccoonwatch with Windows", AutoSize = true };
    private readonly Label _pathWarning = new() { AutoSize = false, ForeColor = Color.DarkOrange, Height = 34 };
    private readonly Button _repair = new() { Text = "Repair", Width = 70, Height = 28, Visible = false };
    private readonly CheckBox _stopOnExit = new() { Text = "Stop tracked processes when raccoonwatch exits", AutoSize = true };
    private readonly CheckBox _notifications = new() { Text = "Show notifications on failures", AutoSize = true };
    private readonly NumericUpDown _pollInterval = new() { Minimum = 250, Maximum = 60_000, Increment = 250, Width = 100 };
    private readonly NumericUpDown _historyLines = new() { Minimum = 100, Maximum = 100_000, Increment = 500, Width = 100 };
    private readonly ComboBox _logLevel = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 130,
        DataSource = Enum.GetNames<LogLevel>()
    };

    public SettingsForm(
        ISettingsService settings,
        IAutostartService autostart,
        IOutputStore output,
        LoggingOptions logging)
    {
        _settings = settings;
        _autostart = autostart;
        _output = output;
        _logging = logging;

        Text = "raccoonwatch — Settings";
        Icon = AppIcon.Create();
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(520, 350);

        BuildUi();
        LoadSettings();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        e.Cancel = true;
        Hide();
        base.OnFormClosing(e);
    }

    private void BuildUi()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 9,
            Padding = new Padding(16),
            AutoSize = true
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));

        var row = 0;
        layout.Controls.Add(_startWithWindows, 0, row);
        layout.SetColumnSpan(_startWithWindows, 2);
        row++;
        layout.Controls.Add(_pathWarning, 0, row);
        layout.SetColumnSpan(_pathWarning, 2);
        layout.Controls.Add(_repair, 2, row);
        _repair.Click += (_, _) => RepairAutostartPath();
        row++;
        layout.Controls.Add(_stopOnExit, 0, row);
        layout.SetColumnSpan(_stopOnExit, 2);
        row++;
        layout.Controls.Add(_notifications, 0, row);
        layout.SetColumnSpan(_notifications, 2);
        row++;
        layout.Controls.Add(new Label { Text = "Status polling interval (ms):", AutoSize = true }, 0, row);
        layout.Controls.Add(_pollInterval, 1, row);
        row++;
        layout.Controls.Add(new Label { Text = "Output history per process (lines):", AutoSize = true }, 0, row);
        layout.Controls.Add(_historyLines, 1, row);
        row++;
        layout.Controls.Add(new Label { Text = "Log level:", AutoSize = true }, 0, row);
        layout.Controls.Add(_logLevel, 1, row);
        row++;
        layout.Controls.Add(new Label
        {
            Text = "PowerShell scripts run with -ExecutionPolicy Bypass.",
            AutoSize = true,
            ForeColor = Color.Gray
        }, 0, row);
        layout.SetColumnSpan(layout.Controls[layout.Controls.Count - 1], 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 48,
            Padding = new Padding(8)
        };
        buttons.Controls.Add(CreateButton("Cancel", (_, _) => Hide()));
        buttons.Controls.Add(CreateButton("OK", (_, _) =>
        {
            if (Apply())
            {
                Hide();
            }
        }));
        buttons.Controls.Add(CreateButton("Apply", (_, _) => Apply()));
        buttons.Controls.Add(CreateButton("Open log folder", (_, _) => OpenLogFolder()));

        Controls.Add(layout);
        Controls.Add(buttons);
    }

    private static Button CreateButton(string text, EventHandler onClick)
    {
        var button = new Button { Text = text, AccessibleName = text, Width = 84, Height = 30 };
        button.Click += onClick;
        return button;
    }

    private void LoadSettings()
    {
        var current = _settings.Settings;
        _startWithWindows.Checked = _autostart.IsEnabled();
        _stopOnExit.Checked = current.StopProcessesOnExit;
        _notifications.Checked = current.ShowNotifications;
        _pollInterval.Value = current.PollIntervalMs;
        _historyLines.Value = current.OutputHistoryLines;
        _logLevel.SelectedItem = current.LogLevel;

        var executable = Environment.ProcessPath ?? string.Empty;
        if (_startWithWindows.Checked && !string.IsNullOrEmpty(executable) && !_autostart.IsPathCurrent(executable))
        {
            _pathWarning.Text = $"Warning: autostart points to '{_autostart.GetConfiguredPath()}'.";
            _pathWarning.Visible = true;
            _repair.Visible = true;
        }
        else
        {
            _pathWarning.Text = string.Empty;
            _pathWarning.Visible = false;
            _repair.Visible = false;
        }
    }

    private static void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogDirectory);
            using var process = Process.Start(new ProcessStartInfo("explorer.exe")
            {
                Arguments = $"\"{AppPaths.LogDirectory}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not open the log folder: {ex.Message}",
                "raccoonwatch",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void RepairAutostartPath()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable))
        {
            MessageBox.Show(
                "Could not determine the executable path.",
                "raccoonwatch",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        _autostart.SetEnabled(true, executable);
        _settings.Settings.StartWithWindowsMirrored = true;
        _settings.Save();
        _startWithWindows.Checked = true;
        LoadSettings();
    }

    private bool Apply()
    {
        try
        {
            var current = _settings.Settings;
            current.ShowNotifications = _notifications.Checked;
            current.StopProcessesOnExit = _stopOnExit.Checked;
            current.PollIntervalMs = (int)_pollInterval.Value;
            current.OutputHistoryLines = (int)_historyLines.Value;
            current.LogLevel = _logLevel.SelectedItem?.ToString() ?? "Information";

            if (Enum.TryParse<LogLevel>(current.LogLevel, ignoreCase: true, out var level))
            {
                _logging.MinLevel = level;
            }

            _output.HistoryLimit = current.OutputHistoryLines;

            var executable = Environment.ProcessPath;
            if (_startWithWindows.Checked && string.IsNullOrEmpty(executable))
            {
                MessageBox.Show(
                    "Could not determine the executable path; the autostart entry was not changed.",
                    "raccoonwatch",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return false;
            }

            if (!string.IsNullOrEmpty(executable))
            {
                _autostart.SetEnabled(_startWithWindows.Checked, executable);
            }

            current.StartWithWindowsMirrored = _startWithWindows.Checked;
            _settings.Save();
            LoadSettings();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "raccoonwatch", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }
}
