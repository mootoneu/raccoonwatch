using System.Windows.Forms;
using Raccoonwatch.Models;
using Raccoonwatch.Services;

namespace Raccoonwatch.UI;

/// <summary>
/// Tray-side process dashboard with full add/edit/delete/reorder support and live status.
/// </summary>
public sealed class ManageProcessesForm : Form
{
    private readonly ISettingsService _settings;
    private readonly IProcessManager _processes;
    private readonly Action<Guid> _showOutput;
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AutoGenerateColumns = false,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        BackgroundColor = System.Drawing.Color.White,
        RowHeadersVisible = false
    };

    public ManageProcessesForm(
        ISettingsService settings,
        IProcessManager processes,
        Action<Guid> showOutput)
    {
        _settings = settings;
        _processes = processes;
        _showOutput = showOutput;

        Text = "raccoonwatch — Manage Processes";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new System.Drawing.Size(820, 420);
        Size = new System.Drawing.Size(980, 520);

        BuildUi();
        RefreshGrid();

        _processes.StateChanged += OnStateChanged;
        _settings.Changed += OnSettingsChanged;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // The tray icon owns application lifetime; keep the form cached instead.
        e.Cancel = true;
        Hide();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _processes.StateChanged -= OnStateChanged;
            _settings.Changed -= OnSettingsChanged;
        }

        base.Dispose(disposing);
    }

    private void BuildUi()
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Status",
            DataPropertyName = "Status",
            Width = 130
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Name",
            DataPropertyName = "Name",
            Width = 180
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Type",
            DataPropertyName = "Type",
            Width = 100
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Command / Arguments",
            DataPropertyName = "Command",
            Width = 350
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Order",
            DataPropertyName = "Order",
            Width = 70
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Auto",
            DataPropertyName = "Auto",
            Width = 60
        });
        _grid.CellDoubleClick += (_, _) => ViewSelectedOutput();

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8),
            Height = 84
        };
        buttonPanel.Controls.Add(CreateButton("Close", 80, (_, _) => Hide()));
        buttonPanel.Controls.Add(CreateButton("View Output", 110, (_, _) => ViewSelectedOutput()));
        buttonPanel.Controls.Add(CreateButton("Restart", 80, (_, _) => _ = RestartSelectedAsync()));
        buttonPanel.Controls.Add(CreateButton("Stop", 70, (_, _) => _ = StopSelectedAsync()));
        buttonPanel.Controls.Add(CreateButton("Start", 70, (_, _) => _ = StartSelectedAsync()));
        buttonPanel.Controls.Add(CreateButton("Move Down", 95, (_, _) => MoveSelected(1)));
        buttonPanel.Controls.Add(CreateButton("Move Up", 85, (_, _) => MoveSelected(-1)));
        buttonPanel.Controls.Add(CreateButton("Delete", 80, (_, _) => _ = DeleteSelectedAsync()));
        buttonPanel.Controls.Add(CreateButton("Edit…", 80, (_, _) => EditSelectedProcess()));
        buttonPanel.Controls.Add(CreateButton("Add…", 80, (_, _) => AddProcess()));

        Controls.Add(_grid);
        Controls.Add(buttonPanel);
    }

    private static Button CreateButton(string text, int width, EventHandler onClick)
    {
        var button = new Button
        {
            Text = text,
            Width = width,
            Height = 32,
            Margin = new Padding(4, 0, 0, 0)
        };
        button.Click += onClick;
        return button;
    }

    private Guid? SelectedDefinitionId() => _grid.CurrentRow?.Tag as Guid?;

    private async Task StartSelectedAsync()
    {
        var definition = FindSelectedDefinition();
        if (definition is not null)
        {
            await ExecuteAsync(() => _processes.StartAsync(definition));
        }
    }

    private async Task StopSelectedAsync()
    {
        if (SelectedDefinitionId() is { } id)
        {
            await ExecuteAsync(() => _processes.StopAsync(id));
        }
    }

    private async Task RestartSelectedAsync()
    {
        var definition = FindSelectedDefinition();
        if (definition is not null)
        {
            await ExecuteAsync(() => _processes.RestartAsync(definition));
        }
    }

    private void ViewSelectedOutput()
    {
        if (SelectedDefinitionId() is { } id)
        {
            _showOutput(id);
        }
    }

    private void AddProcess()
    {
        using var dialog = new ProcessEditForm(null, _settings);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _settings.AddOrUpdate(dialog.Result);
        }
    }

    private void EditSelectedProcess()
    {
        var definition = FindSelectedDefinition();
        if (definition is null)
        {
            return;
        }

        using var dialog = new ProcessEditForm(definition, _settings);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var updated = dialog.Result;
        var commandChanged = !string.Equals(definition.CommandPath, updated.CommandPath, StringComparison.OrdinalIgnoreCase) ||
                             !string.Equals(definition.WorkingDirectory, updated.WorkingDirectory, StringComparison.OrdinalIgnoreCase);
        if (commandChanged && IsActive(_processes.GetState(definition.Id).Status))
        {
            var restart = MessageBox.Show(
                "This process is running. The new command path will apply after a restart.\n\nRestart it now?",
                "raccoonwatch",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (restart != DialogResult.Yes)
            {
                return;
            }

            _ = RestartSelectedAsync();
        }

        _settings.AddOrUpdate(updated);
    }

    private async Task DeleteSelectedAsync()
    {
        var definition = FindSelectedDefinition();
        if (definition is null)
        {
            return;
        }

        var isRunning = IsActive(_processes.GetState(definition.Id).Status);
        var message = isRunning
            ? $"Delete '{definition.DisplayName}'? It is currently running and will be stopped."
            : $"Delete '{definition.DisplayName}'?";

        var answer = MessageBox.Show(
            message,
            "raccoonwatch",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (answer != DialogResult.Yes)
        {
            return;
        }

        if (isRunning)
        {
            await _processes.StopAsync(definition.Id);
        }

        _settings.Delete(definition.Id);
    }

    private void MoveSelected(int delta)
    {
        var definition = FindSelectedDefinition();
        if (definition is null)
        {
            return;
        }

        var ordered = _settings.Settings.Processes
            .OrderBy(d => d.StartOrder)
            .ThenBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var index = ordered.FindIndex(d => d.Id == definition.Id);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= ordered.Count)
        {
            return;
        }

        var other = ordered[target];
        _settings.AddOrUpdate(definition with { StartOrder = other.StartOrder });
        _settings.AddOrUpdate(other with { StartOrder = definition.StartOrder });
    }

    private static bool IsActive(ProcessStatus status) =>
        status is ProcessStatus.Starting or ProcessStatus.Running or ProcessStatus.Stopping;

    private ProcessDefinition? FindSelectedDefinition()
    {
        if (SelectedDefinitionId() is not { } id)
        {
            return null;
        }

        return _settings.Settings.Processes.FirstOrDefault(p => p.Id == id);
    }

    private async Task ExecuteAsync(Func<Task<ProcessResult>> action)
    {
        try
        {
            var result = await action();
            if (!result.Success)
            {
                MessageBox.Show(
                    result.Error ?? "Operation failed.",
                    "raccoonwatch",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "raccoonwatch", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RefreshGrid()
    {
        var selected = SelectedDefinitionId();
        _grid.Rows.Clear();

        foreach (var definition in _settings.Settings.Processes
                     .OrderBy(d => d.StartOrder)
                     .ThenBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var state = _processes.GetState(definition.Id);
            var row = new DataGridViewRow { Tag = definition.Id };
            row.CreateCells(
                _grid,
                $"{TrayGlyph.For(state.Status)} {state.Status}",
                definition.DisplayName,
                definition.Kind.ToString(),
                $"{definition.CommandPath} {definition.Arguments}".TrimEnd(),
                definition.StartOrder,
                definition.AutoStartOnLaunch ? "✓" : string.Empty);
            _grid.Rows.Add(row);

            if (selected == definition.Id)
            {
                _grid.CurrentCell = row.Cells[0];
            }
        }
    }

    private void OnStateChanged(object? sender, ProcessStateChangedEventArgs e) => RefreshOnUiThread();

    private void OnSettingsChanged(object? sender, EventArgs e) => RefreshOnUiThread();

    private void RefreshOnUiThread()
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(RefreshGrid);
        }
        else
        {
            RefreshGrid();
        }
    }
}
