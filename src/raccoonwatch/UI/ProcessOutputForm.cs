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
            Height = 44,
            Padding = new Padding(6),
            BackColor = SystemColors.Control
        };
        toolbar.Controls.Add(new Label { Text = "Process:", AutoSize = true, Padding = new Padding(0, 8, 4, 0) });
        toolbar.Controls.Add(_selector);
        toolbar.Controls.Add(_pause);
        toolbar.Controls.Add(CreateButton("Clear", (_, _) =>
        {
            _output.Clear(_currentId);
            _box.Clear();
        }));
        toolbar.Controls.Add(CreateButton("Close", (_, _) => Hide()));
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
        var button = new Button { Text = text, Width = 80, Height = 30 };
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

        _box.SelectionStart = _box.TextLength;
        _box.SelectionColor = entry.IsStandardError
            ? Color.FromArgb(255, 140, 140)
            : Color.FromArgb(220, 220, 220);
        var prefix = entry.IsStandardError ? "[ERR] " : string.Empty;
        _box.AppendText($"{entry.Timestamp:HH:mm:ss} {prefix}{entry.Text}{Environment.NewLine}");
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
