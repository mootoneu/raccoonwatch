using System.Drawing;
using System.Windows.Forms;
using Raccoonwatch.Infrastructure;
using Raccoonwatch.Models;
using Raccoonwatch.Services;

namespace Raccoonwatch.UI;

/// <summary>Add/Edit dialog for a <see cref="ProcessDefinition"/>.</summary>
public sealed class ProcessEditForm : Form
{
    private readonly ISettingsService _settings;
    private readonly ProcessDefinition? _original;

    private readonly TextBox _name = new();
    private readonly ComboBox _kind = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList
    };
    private readonly TextBox _command = new();
    private readonly TextBox _arguments = new();
    private readonly TextBox _workingDirectory = new();
    private readonly NumericUpDown _order = new() { Minimum = 0, Maximum = 10_000 };
    private readonly CheckBox _autoStart = new() { Text = "Start with raccoonwatch", AutoSize = true };
    private readonly TextBox _notes = new() { Multiline = true, Height = 64, ScrollBars = ScrollBars.Vertical };
    private readonly Label _errors = new()
    {
        ForeColor = Color.Firebrick,
        AutoSize = false,
        Height = 58,
        TextAlign = ContentAlignment.TopLeft
    };

    public ProcessEditForm(ProcessDefinition? original, ISettingsService settings)
    {
        _original = original;
        _settings = settings;

        // Populate before setting the selection: a data-bound ComboBox discards a
        // SelectedItem assigned before its handle is created (it resets to item 0).
        _kind.Items.AddRange(Enum.GetNames<ProcessKind>());

        Text = original is null ? "raccoonwatch — Add Process" : "raccoonwatch — Edit Process";
        Icon = AppIcon.Create();
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(560, 520);

        PopulateFields();
        BuildUi();
    }

    public ProcessDefinition Result { get; private set; } = null!;

    private void PopulateFields()
    {
        if (_original is null)
        {
            _kind.SelectedItem = nameof(ProcessKind.Executable);
            _order.Value = _settings.Settings.Processes.Count + 1;
            return;
        }

        _name.Text = _original.DisplayName;
        _kind.SelectedItem = _original.Kind.ToString();
        _command.Text = _original.CommandPath;
        _arguments.Text = _original.Arguments;
        _workingDirectory.Text = _original.WorkingDirectory;
        _order.Value = Math.Clamp(_original.StartOrder, 0, 10_000);
        _autoStart.Checked = _original.AutoStartOnLaunch;
        _notes.Text = _original.Notes;
    }

    private void BuildUi()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 9,
            Padding = new Padding(14)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));

        var row = 0;
        AddLabel(layout, "Display name:", row++);
        layout.Controls.Add(_name, 1, row);
        layout.SetColumnSpan(_name, 2);
        row++;

        AddLabel(layout, "Kind:", row++);
        layout.Controls.Add(_kind, 1, row);
        layout.SetColumnSpan(_kind, 2);
        row++;

        AddLabel(layout, "Command path:", row++);
        layout.Controls.Add(_command, 1, row);
        layout.Controls.Add(CreateBrowseButton((_, _) => BrowseCommand()), 2, row);
        row++;

        AddLabel(layout, "Arguments:", row++);
        layout.Controls.Add(_arguments, 1, row);
        layout.SetColumnSpan(_arguments, 2);
        row++;

        AddLabel(layout, "Working directory:", row++);
        layout.Controls.Add(_workingDirectory, 1, row);
        layout.Controls.Add(CreateBrowseButton((_, _) => BrowseWorkingDirectory()), 2, row);
        row++;

        AddLabel(layout, "Start order:", row++);
        layout.Controls.Add(_order, 1, row);
        layout.SetColumnSpan(_order, 2);
        row++;

        layout.Controls.Add(_autoStart, 1, row);
        layout.SetColumnSpan(_autoStart, 2);
        row++;

        AddLabel(layout, "Notes:", row++);
        layout.Controls.Add(_notes, 1, row);
        layout.SetColumnSpan(_notes, 2);
        row++;

        layout.Controls.Add(_errors, 0, row);
        layout.SetColumnSpan(_errors, 3);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 48,
            Padding = new Padding(8)
        };
        var cancel = new Button { Text = "Cancel", Width = 84 };
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        var ok = new Button { Text = "OK", Width = 84 };
        ok.Click += (_, _) => TryAccept();
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        Controls.Add(layout);
        Controls.Add(buttons);
    }

    private static void AddLabel(TableLayoutPanel layout, string text, int row) =>
        layout.Controls.Add(new Label { Text = text, AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, row);

    private static Button CreateBrowseButton(EventHandler onClick)
    {
        var button = new Button { Text = "…", AccessibleName = "Browse", Width = 32, Height = 23 };
        button.Click += onClick;
        return button;
    }

    private void BrowseCommand()
    {
        using var dialog = new OpenFileDialog { CheckFileExists = true };
        dialog.Filter = SelectedKind() switch
        {
            ProcessKind.Cmd => "Command scripts (*.cmd;*.bat)|*.cmd;*.bat",
            ProcessKind.PowerShell => "PowerShell scripts (*.ps1)|*.ps1",
            _ => "Executables (*.exe)|*.exe"
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _command.Text = dialog.FileName;
            if (string.IsNullOrWhiteSpace(_workingDirectory.Text))
            {
                _workingDirectory.Text = Path.GetDirectoryName(dialog.FileName) ?? string.Empty;
            }
        }
    }

    private void BrowseWorkingDirectory()
    {
        using var dialog = new FolderBrowserDialog { SelectedPath = _workingDirectory.Text };
        if (!string.IsNullOrWhiteSpace(_workingDirectory.Text) && Directory.Exists(_workingDirectory.Text))
        {
            dialog.SelectedPath = _workingDirectory.Text;
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _workingDirectory.Text = dialog.SelectedPath;
        }
    }

    private ProcessKind SelectedKind() =>
        Enum.TryParse<ProcessKind>(_kind.SelectedItem?.ToString(), ignoreCase: true, out var kind)
            ? kind
            : ProcessKind.Executable;

    private void TryAccept()
    {
        var definition = new ProcessDefinition
        {
            Id = _original?.Id ?? Guid.NewGuid(),
            DisplayName = _name.Text.Trim(),
            Kind = SelectedKind(),
            CommandPath = _command.Text.Trim(),
            Arguments = _arguments.Text.Trim(),
            WorkingDirectory = _workingDirectory.Text.Trim(),
            StartOrder = (int)_order.Value,
            AutoStartOnLaunch = _autoStart.Checked,
            Notes = _notes.Text.Trim()
        };

        var errors = _settings.Validate(definition, _original?.Id);
        if (errors.Count > 0)
        {
            _errors.Text = "• " + string.Join("\n• ", errors);
            DialogResult = DialogResult.None;
            return;
        }

        Result = definition;
        DialogResult = DialogResult.OK;
        Close();
    }
}
