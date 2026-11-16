using System.Windows.Forms;
using Raccoonwatch.Models;
using Raccoonwatch.Services;

namespace Raccoonwatch.UI;

/// <summary>
/// Tray-side process dashboard. Add/Edit/Delete arrive in Phase 5; this view already
/// supports live status, start/stop/restart and opening the output window.
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
            Height = 52
        };
        buttonPanel.Controls.Add(CreateButton("Close", 90, (_, _) => Hide()));
        buttonPanel.Controls.Add(CreateButton("View Output", 110, (_, _) => ViewSelectedOutput()));
        buttonPanel.Controls.Add(CreateButton("Restart", 90, (_, _) => _ = RestartSelectedAsync()));
        buttonPanel.Controls.Add(CreateButton("Stop", 80, (_, _) => _ = StopSelectedAsync()));
        buttonPanel.Controls.Add(CreateButton("Start", 80, (_, _) => _ = StartSelectedAsync()));

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
