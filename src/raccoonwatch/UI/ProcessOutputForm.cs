using System.Drawing;
using System.Windows.Forms;
using Raccoonwatch.Models;
using Raccoonwatch.Services;

namespace Raccoonwatch.UI;

/// <summary>Terminal-style read-only view of captured process output.</summary>
public sealed class ProcessOutputForm : Form
{
    private readonly IOutputStore _output;
    private readonly ISettingsService _settings;
    private readonly ComboBox _selector = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 240
    };
    private readonly RichTextBox _box = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        BackColor = Color.FromArgb(30, 30, 30),
        ForeColor = Color.FromArgb(220, 220, 220),
        Font = new Font("Cascadia Mono", 10F, FontStyle.Regular, GraphicsUnit.Point),
        WordWrap = false,
        ScrollBars = RichTextBoxScrollBars.Both,
        DetectUrls = false
    };
    private readonly CheckBox _pause = new() { Text = "Pause follow", AutoSize = true };
    private readonly CheckBox _timestamps = new() { Text = "Timestamps", AutoSize = true, Checked = true };
    private readonly NumericUpDown _fontSize = new()
    {
        Minimum = 7,
        Maximum = 20,
        Increment = 1,
        Value = 10,
        Width = 52
    };

    private Guid _currentId;

    public ProcessOutputForm(IOutputStore output, ISettingsService settings)
    {
        _output = output;
        _settings = settings;

        Text = "raccoonwatch — Process Output";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(640, 360);
        Size = new Size(900, 560);

        BuildUi();
        RefreshProcessList();
        _output.OutputReceived += OnOutputReceived;
        _settings.Changed += OnSettingsChanged;
    }

    public void SetProcess(Guid definitionId)
    {
        _currentId = definitionId;

        var definitions = _settings.Settings.Processes;
        var definition = definitions.FirstOrDefault(p => p.Id == definitionId);
        if (definition is not null)
        {
            _selector.SelectedValue = definitionId;
        }

        Text = $"raccoonwatch — Output — {definition?.DisplayName ?? "Process"}";
        LoadSnapshot();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        e.Cancel = true;
        Hide();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _output.OutputReceived -= OnOutputReceived;
            _settings.Changed -= OnSettingsChanged;
        }

        base.Dispose(disposing);
    }

    private void BuildUi()
    {
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 76,
            Padding = new Padding(6),
            BackColor = SystemColors.Control
        };
        toolbar.Controls.Add(new Label { Text = "Process:", AutoSize = true, Padding = new Padding(0, 8, 4, 0) });
        toolbar.Controls.Add(_selector);
        toolbar.Controls.Add(_pause);
        toolbar.Controls.Add(_timestamps);
        toolbar.Controls.Add(new Label { Text = "Font:", AutoSize = true, Padding = new Padding(6, 8, 2, 0) });
        toolbar.Controls.Add(_fontSize);
        toolbar.Controls.Add(CreateButton("Save…", (_, _) => SaveOutput()));
        toolbar.Controls.Add(CreateButton("Clear", (_, _) =>
        {
            _output.Clear(_currentId);
            _box.Clear();
        }));
        toolbar.Controls.Add(CreateButton("Close", (_, _) => Hide()));
        _fontSize.ValueChanged += (_, _) => UpdateFont();
        _selector.SelectedIndexChanged += (_, _) =>
        {
            if (_selector.SelectedValue is Guid id)
            {
                SetProcess(id);
            }
        };

        Controls.Add(_box);
        Controls.Add(toolbar);
    }

    private static Button CreateButton(string text, EventHandler onClick)
    {
        var button = new Button { Text = text, AccessibleName = text, Width = 80, Height = 30 };
        button.Click += onClick;
        return button;
    }

    private void RefreshProcessList()
    {
        var definitions = _settings.Settings.Processes
            .OrderBy(d => d.StartOrder)
            .ThenBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _selector.DataSource = definitions;
        _selector.DisplayMember = nameof(ProcessDefinition.DisplayName);
        _selector.ValueMember = nameof(ProcessDefinition.Id);
    }

    private void LoadSnapshot()
    {
        _box.Clear();
        foreach (var entry in _output.GetSnapshot(_currentId))
        {
            AppendEntry(entry);
        }

        ScrollToEnd();
    }

    private void AppendEntry(OutputEntry entry)
    {
        if (_box.IsDisposed)
        {
            return;
        }

        var prefix = (_timestamps.Checked ? $"{entry.Timestamp:HH:mm:ss} " : string.Empty) +
                     (entry.IsStandardError ? "[ERR] " : string.Empty);
        var defaultColor = entry.IsStandardError
            ? Color.FromArgb(255, 140, 140)
            : Color.FromArgb(220, 220, 220);

        if (prefix.Length > 0)
        {
            AppendColored(prefix, defaultColor);
        }

        foreach (var segment in AnsiTextParser.Parse(entry.Text))
        {
            AppendColored(segment.Text, segment.Foreground ?? defaultColor);
        }

        AppendColored(Environment.NewLine, defaultColor);
    }

    private void AppendColored(string text, Color color)
    {
        _box.SelectionStart = _box.TextLength;
        _box.SelectionColor = color;
        _box.AppendText(text);
    }

    private void UpdateFont()
    {
        _box.Font = new Font("Cascadia Mono", (float)_fontSize.Value, FontStyle.Regular, GraphicsUnit.Point);
    }

    private void SaveOutput()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "Log files (*.log)|*.log|Text files (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = $"raccoonwatch-output-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.log"
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            File.WriteAllText(dialog.FileName, _box.Text, System.Text.Encoding.UTF8);
        }
    }

    private void ScrollToEnd()
    {
        _box.SelectionStart = _box.TextLength;
        _box.ScrollToCaret();
    }

    private void OnOutputReceived(object? sender, OutputBatchEventArgs e)
    {
        var relevant = e.Entries.Where(entry => entry.DefinitionId == _currentId).ToArray();
        if (relevant.Length == 0)
        {
            return;
        }

        RunOnUiThread(() =>
        {
            foreach (var entry in relevant)
            {
                AppendEntry(entry);
            }

            if (!_pause.Checked)
            {
                ScrollToEnd();
            }
        });
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => RunOnUiThread(RefreshProcessList);

    private void RunOnUiThread(Action action)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(action);
        }
        else
        {
            action();
        }
    }
}
