using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Extensions.Logging.Abstractions;
using Raccoonwatch.Infrastructure;
using Raccoonwatch.Models;
using Raccoonwatch.Services;
using Raccoonwatch.UI;

namespace Raccoonwatch.Tests;

public sealed class CoreTests : IDisposable
{
    private readonly string _directory;

    public CoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"raccoonwatch-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void SettingsStore_RoundTripsDefinitions()
    {
        var path = Path.Combine(_directory, "settings.json");
        var store = new JsonSettingsStore(NullLogger<JsonSettingsStore>.Instance, path);
        var definition = new ProcessDefinition
        {
            DisplayName = "API server",
            Kind = ProcessKind.PowerShell,
            CommandPath = @"C:\scripts\api.ps1",
            Arguments = "-Port 5000",
            WorkingDirectory = @"C:\scripts",
            UpdateCommandPath = @"C:\scripts\update.ps1",
            UpdateArguments = "-Force",
            StartOrder = 7,
            AutoStartOnLaunch = true,
            Notes = "local API"
        };
        var settings = new AppSettings { Processes = [definition], PollIntervalMs = 1500 };

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal(1, loaded.SchemaVersion);
        Assert.Single(loaded.Processes);
        Assert.Equal(definition.Id, loaded.Processes[0].Id);
        Assert.Equal("API server", loaded.Processes[0].DisplayName);
        Assert.Equal(ProcessKind.PowerShell, loaded.Processes[0].Kind);
        Assert.True(loaded.Processes[0].AutoStartOnLaunch);
        Assert.Equal(@"C:\scripts\update.ps1", loaded.Processes[0].UpdateCommandPath);
        Assert.Equal("-Force", loaded.Processes[0].UpdateArguments);
        Assert.Equal(1500, loaded.PollIntervalMs);
    }

    [Fact]
    public void SettingsStore_QuarantinesCorruptFileAndCreatesDefaults()
    {
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, "{ this is not json");

        var store = new JsonSettingsStore(NullLogger<JsonSettingsStore>.Instance, path);
        var settings = store.Load();

        Assert.Empty(settings.Processes);
        Assert.Equal(2000, settings.PollIntervalMs);
        Assert.True(File.Exists(path + ".bad"));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void SettingsService_ValidatesNamesPathsAndKinds()
    {
        var executable = Path.Combine(_directory, "tool.exe");
        File.WriteAllText(executable, string.Empty);
        var store = new JsonSettingsStore(NullLogger<JsonSettingsStore>.Instance, Path.Combine(_directory, "settings.json"));
        var service = new SettingsService(store, NullLogger<SettingsService>.Instance);
        service.Load();

        var valid = new ProcessDefinition
        {
            DisplayName = "Tool",
            Kind = ProcessKind.Executable,
            CommandPath = executable
        };
        Assert.Empty(service.Validate(valid));

        var duplicate = valid with { Id = Guid.NewGuid() };
        service.AddOrUpdate(valid);
        Assert.Contains(service.Validate(duplicate), error => error.Contains("already uses"));

        var wrongKind = valid with { DisplayName = "Wrong", Kind = ProcessKind.Cmd };
        Assert.Contains(service.Validate(wrongKind), error => error.Contains("extension"));

        var missing = valid with { DisplayName = "Missing", CommandPath = Path.Combine(_directory, "nope.exe") };
        Assert.Contains(service.Validate(missing), error => error.Contains("does not exist"));
    }

    [Fact]
    public void SettingsService_ClampsRangesAndFallsBackForInvalidLogLevel()
    {
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, """
            {"schemaVersion":1,"processes":[],"pollIntervalMs":10,
             "outputHistoryLines":1,"logLevel":"nonsense"}
            """);

        var store = new JsonSettingsStore(NullLogger<JsonSettingsStore>.Instance, path);
        var service = new SettingsService(store, NullLogger<SettingsService>.Instance);
        service.Load();

        Assert.Equal(250, service.Settings.PollIntervalMs);
        Assert.Equal(100, service.Settings.OutputHistoryLines);
        Assert.Equal("Information", service.Settings.LogLevel);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void SettingsService_AddsUpdatesAndDeletesDefinitions()
    {
        var path = Path.Combine(_directory, "settings.json");
        var store = new JsonSettingsStore(NullLogger<JsonSettingsStore>.Instance, path);
        var service = new SettingsService(store, NullLogger<SettingsService>.Instance);
        service.Load();

        var first = new ProcessDefinition
        {
            DisplayName = "First",
            Kind = ProcessKind.Executable,
            CommandPath = @"C:\tools\first.exe",
            StartOrder = 1
        };
        var second = first with { Id = Guid.NewGuid(), DisplayName = "Second", StartOrder = 2 };

        service.AddOrUpdate(first);
        service.AddOrUpdate(second);
        Assert.Equal(2, service.Settings.Processes.Count);

        service.AddOrUpdate(first with { DisplayName = "First renamed", AutoStartOnLaunch = true });
        var renamed = Assert.Single(service.Settings.Processes, p => p.Id == first.Id);
        Assert.Equal("First renamed", renamed.DisplayName);
        Assert.True(renamed.AutoStartOnLaunch);

        service.Delete(second.Id);
        Assert.Single(service.Settings.Processes);
        Assert.DoesNotContain(service.Settings.Processes, p => p.Id == second.Id);

        var reloaded = store.Load();
        Assert.Single(reloaded.Processes);
        Assert.Equal("First renamed", reloaded.Processes[0].DisplayName);
    }

    [Fact]
    public void ProcessLauncher_BuildsCommandLinesPerKind()
    {
        var executable = new ProcessDefinition
        {
            DisplayName = "exe",
            Kind = ProcessKind.Executable,
            CommandPath = @"C:\tools\api.exe",
            Arguments = "--port 5000"
        };
        var exeInfo = ProcessLauncher.BuildStartInfo(executable);
        Assert.Equal(@"C:\tools\api.exe", exeInfo.FileName);
        Assert.Equal("--port 5000", exeInfo.Arguments);
        Assert.True(exeInfo.RedirectStandardOutput);
        Assert.True(exeInfo.RedirectStandardError);
        Assert.False(exeInfo.UseShellExecute);

        var cmd = executable with
        {
            DisplayName = "cmd",
            Kind = ProcessKind.Cmd,
            CommandPath = @"C:\scripts\my script.cmd"
        };
        var cmdInfo = ProcessLauncher.BuildStartInfo(cmd);
        Assert.Contains("cmd.exe", cmdInfo.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("/c ", cmdInfo.Arguments);
        Assert.Contains("\"C:\\scripts\\my script.cmd\"", cmdInfo.Arguments);

        var ps = executable with
        {
            DisplayName = "ps",
            Kind = ProcessKind.PowerShell,
            CommandPath = @"C:\scripts\worker.ps1"
        };
        var psInfo = ProcessLauncher.BuildStartInfo(ps);
        Assert.Contains("powershell.exe", psInfo.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-NoProfile -ExecutionPolicy Bypass -File", psInfo.Arguments);
        Assert.Contains("\"C:\\scripts\\worker.ps1\"", psInfo.Arguments);
    }

    [Fact]
    public void ProcessLauncher_QuotesArguments()
    {
        Assert.Equal("\"C:\\my path\\tool.exe\"", ProcessLauncher.Quote(@"C:\my path\tool.exe"));
        Assert.Equal("\"already\"", ProcessLauncher.Quote("\"already\""));
        Assert.Equal("\"\"", ProcessLauncher.Quote(string.Empty));
    }

    [Fact]
    public void OutputStore_BoundsHistoryAndExposesSnapshot()
    {
        using var store = new OutputStore(NullLogger<OutputStore>.Instance) { HistoryLimit = 100 };
        var id = Guid.NewGuid();

        for (var i = 0; i < 250; i++)
        {
            store.Append(id, new OutputEntry(DateTimeOffset.Now, id, false, i.ToString()));
        }

        var snapshot = store.GetSnapshot(id);
        Assert.Equal(100, snapshot.Count);
        Assert.Equal("150", snapshot[0].Text);
        Assert.Equal("249", snapshot[^1].Text);

        store.Clear(id);
        Assert.Empty(store.GetSnapshot(id));
    }

    [Fact]
    public void AboutResources_AreEmbeddedAndLoadable()
    {
        var assembly = typeof(AboutForm).Assembly;
        var names = assembly.GetManifestResourceNames();

        Assert.Contains("Raccoonwatch.assets.raccoonwatch_icon_1254x1254.png", names);
        Assert.Contains("Raccoonwatch.assets.github-mark.png", names);

        using var stream = assembly.GetManifestResourceStream(
            "Raccoonwatch.assets.raccoonwatch_icon_1254x1254.png");
        Assert.NotNull(stream);
        using var image = Image.FromStream(stream!);
        Assert.Equal(image.Width, image.Height);
        Assert.True(image.Width >= 1254);
    }

    [Fact]
    public void AppIcon_LoadsEmbeddedRaccoonIcon()
    {
        using var icon = AppIcon.Create();

        Assert.True(icon.Width > 0);
        Assert.Equal(icon.Width, icon.Height);
        Assert.NotEqual(IntPtr.Zero, icon.Handle);
    }

    [Fact]
    public void AnsiTextParser_InterpretsForegroundAndReset()
    {
        var segments = AnsiTextParser.Parse("\u001b[31mred\u001b[0m normal");

        Assert.Equal(2, segments.Count);
        Assert.Equal("red", segments[0].Text);
        Assert.Equal(System.Drawing.Color.FromArgb(205, 49, 49), segments[0].Foreground);
        Assert.Equal(" normal", segments[1].Text);
        Assert.Null(segments[1].Foreground);
    }

    [Fact]
    public void AnsiTextParser_IgnoresNonSgrSequences()
    {
        var segments = AnsiTextParser.Parse("\u001b[2Kcleared\u001b[1;32mgreen");

        Assert.Equal("cleared", segments[0].Text);
        Assert.Null(segments[0].Foreground);
        Assert.Equal("green", segments[1].Text);
        Assert.Equal(System.Drawing.Color.FromArgb(13, 188, 121), segments[1].Foreground);
    }

    [Fact]
    public void WindowsRunRegistry_FormatsQuotedValue()
    {
        Assert.Equal("\"C:\\Program Files\\raccoonwatch\\raccoonwatch.exe\"",
            WindowsRunRegistry.FormatValue(@"C:\Program Files\raccoonwatch\raccoonwatch.exe"));
    }

    [Fact]
    public void RuntimeState_ComputesUptimeUntilExit()
    {
        var started = DateTimeOffset.Now.AddHours(-1);
        var state = new RuntimeState
        {
            StartedAt = started,
            ExitedAt = started.AddMinutes(30)
        };

        Assert.Equal(TimeSpan.FromMinutes(30), state.Uptime);
    }

    [Theory]
    [InlineData(ProcessStatus.Running, "●")]
    [InlineData(ProcessStatus.Stopped, "○")]
    [InlineData(ProcessStatus.Starting, "▲")]
    [InlineData(ProcessStatus.Failed, "✖")]
    public void TrayGlyph_IsStable(ProcessStatus status, string expected) =>
        Assert.Equal(expected, TrayGlyph.For(status));

    [Theory]
    [InlineData(ProcessStatus.Running)]
    [InlineData(ProcessStatus.Stopped)]
    [InlineData(ProcessStatus.Starting)]
    [InlineData(ProcessStatus.Stopping)]
    [InlineData(ProcessStatus.Exited)]
    [InlineData(ProcessStatus.Failed)]
    public void StatusIcons_AreEmbeddedAndLoadable(ProcessStatus status)
    {
        // Icons are cached for the app lifetime, so the test must not dispose them.
        var icon = StatusIcons.For(status);

        Assert.NotNull(icon);
        Assert.Equal(16, icon.Width);
        Assert.Equal(16, icon.Height);
    }

    [Fact]
    public void StatusIcons_HaveNoIconForUnknown() =>
        Assert.Null(StatusIcons.For(ProcessStatus.Unknown));

    [Fact]
    public void ProjectFolderOpener_ResolvesWorkingDirectoryWithCommandFolderFallback()
    {
        var commandPath = Path.Combine("tools", "api.exe");
        var workingDirectory = Path.Combine("projects", "api");
        var definition = new ProcessDefinition
        {
            CommandPath = commandPath,
            WorkingDirectory = workingDirectory
        };

        Assert.Equal(workingDirectory, ProjectFolderOpener.ResolveFolder(definition));
        Assert.Equal("tools", ProjectFolderOpener.ResolveFolder(definition with { WorkingDirectory = "  " }));
        Assert.Null(ProjectFolderOpener.ResolveFolder(definition with
        {
            CommandPath = "api.exe",
            WorkingDirectory = string.Empty
        }));
    }

    [Fact]
    public void ProjectFolderOpener_BuildsQuotedExplorerStartInfo()
    {
        var folder = Path.Combine(_directory, "my projects");
        Directory.CreateDirectory(folder);

        var startInfo = ProjectFolderOpener.BuildStartInfo(folder);

        Assert.Equal("explorer.exe", startInfo.FileName);
        Assert.Equal($"\"{folder}\"", startInfo.Arguments);
        Assert.True(startInfo.UseShellExecute);
    }

    [Fact]
    public void TrayMenu_IncludesOpenProjectFolderAction()
    {
        var folder = Path.Combine(_directory, "project");
        var definition = new ProcessDefinition
        {
            CommandPath = Path.Combine(folder, "api.exe"),
            WorkingDirectory = folder
        };

        string? openedFolder = null;
        var item = TrayApplicationContext.BuildOpenProjectFolderItem(definition, path => openedFolder = path);

        Assert.Equal("Open Project Folder", item.Text);
        Assert.True(item.Enabled);
        item.PerformClick();
        Assert.Equal(folder, openedFolder);

        var unconfigured = definition with { CommandPath = "api.exe", WorkingDirectory = string.Empty };
        var disabled = TrayApplicationContext.BuildOpenProjectFolderItem(
            unconfigured, _ => throw new InvalidOperationException("Disabled item must not open Explorer."));
        Assert.False(disabled.Enabled);
        disabled.PerformClick();
    }

    [Theory]
    [InlineData("update.exe", ProcessKind.Executable)]
    [InlineData("update.msi", ProcessKind.Executable)]
    [InlineData("update.cmd", ProcessKind.Cmd)]
    [InlineData("update.bat", ProcessKind.Cmd)]
    [InlineData("update.ps1", ProcessKind.PowerShell)]
    public void UpdateCommandLauncher_InfersKindFromExtension(string path, ProcessKind expected) =>
        Assert.Equal(expected, UpdateCommandLauncher.InferKind(path));

    [Fact]
    public void UpdateCommandLauncher_BuildsRunnableStartInfo()
    {
        var updater = Path.Combine(_directory, "my updater.ps1");
        var startInfo = UpdateCommandLauncher.BuildStartInfo(updater, "-Force");

        Assert.Contains("powershell.exe", startInfo.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"-File \"{updater}\" -Force", startInfo.Arguments);
        Assert.Equal(_directory, startInfo.WorkingDirectory);
        Assert.True(startInfo.UseShellExecute);
        Assert.False(startInfo.RedirectStandardOutput);
        Assert.False(startInfo.RedirectStandardError);

        var script = Path.Combine(_directory, "update.cmd");
        var scriptInfo = UpdateCommandLauncher.BuildStartInfo(script, "--yes");
        Assert.Contains("cmd.exe", scriptInfo.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("/c ", scriptInfo.Arguments);
        Assert.Contains($"\"{script}\" --yes", scriptInfo.Arguments);

        var executable = Path.Combine(_directory, "updater.exe");
        var exeInfo = UpdateCommandLauncher.BuildStartInfo(executable, "--silent");
        Assert.Equal(executable, exeInfo.FileName);
        Assert.Equal("--silent", exeInfo.Arguments);
    }

    [Fact]
    public async Task UpdateCommandLauncher_LaunchesUpdaterProcess()
    {
        var script = Path.Combine(_directory, "update.cmd");
        File.WriteAllText(script, "exit /b 0\r\n");

        using var process = UpdateCommandLauncher.Launch(script, string.Empty);
        await process.WaitForExitAsync();

        Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    public void TrayMenu_IncludesUpdateActionForConfiguredProcess()
    {
        var definition = new ProcessDefinition
        {
            DisplayName = "Worker",
            UpdateCommandPath = Path.Combine(_directory, "update.ps1"),
            UpdateArguments = "-Force"
        };

        ProcessDefinition? updated = null;
        var item = TrayApplicationContext.BuildUpdateItem(definition, process => updated = process);

        Assert.Equal("Update", item.Text);
        Assert.True(item.Enabled);
        item.PerformClick();
        Assert.Same(definition, updated);

        var unconfigured = definition with { UpdateCommandPath = string.Empty };
        var disabled = TrayApplicationContext.BuildUpdateItem(
            unconfigured, _ => throw new InvalidOperationException("Disabled item must not run the updater."));
        Assert.False(disabled.Enabled);
        disabled.PerformClick();
    }

    [Theory]
    [InlineData(ProcessKind.Executable)]
    [InlineData(ProcessKind.Cmd)]
    [InlineData(ProcessKind.PowerShell)]
    public void ProcessEditForm_PreservesKindOfEditedProcess(ProcessKind kind)
    {
        var definition = new ProcessDefinition
        {
            DisplayName = "Worker",
            Kind = kind,
            CommandPath = Path.Combine(_directory, "worker.ps1")
        };

        using var form = new ProcessEditForm(definition, CreateSettings());
        var combo = GetFieldValue<ComboBox>(form, "_kind");
        _ = combo.Handle; // Force handle creation; the selection set beforehand must survive it.

        Assert.Equal(kind.ToString(), combo.SelectedItem);
    }

    [Fact]
    public void ProcessEditForm_PopulatesAndKeepsUpdateFields()
    {
        var definition = new ProcessDefinition
        {
            DisplayName = "Worker",
            CommandPath = Path.Combine(_directory, "worker.ps1"),
            UpdateCommandPath = Path.Combine(_directory, "update.ps1"),
            UpdateArguments = "-Force"
        };

        using var form = new ProcessEditForm(definition, CreateSettings());

        Assert.Equal(definition.UpdateCommandPath, GetFieldValue<TextBox>(form, "_updateCommand").Text);
        Assert.Equal(definition.UpdateArguments, GetFieldValue<TextBox>(form, "_updateArguments").Text);
    }

    private SettingsService CreateSettings()
    {
        var store = new JsonSettingsStore(NullLogger<JsonSettingsStore>.Instance, Path.Combine(_directory, "settings.json"));
        var settings = new SettingsService(store, NullLogger<SettingsService>.Instance);
        settings.Load();
        return settings;
    }

    private static T GetFieldValue<T>(ProcessEditForm form, string name) where T : class =>
        (T)typeof(ProcessEditForm)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(form)!;

    [Fact]
    public async Task AppController_StartsOnlyAutostartProcessesAndMirrorsRegistry()
    {
        var store = new JsonSettingsStore(NullLogger<JsonSettingsStore>.Instance, Path.Combine(_directory, "settings.json"));
        var settings = new SettingsService(store, NullLogger<SettingsService>.Instance);
        settings.Load();

        var auto = new ProcessDefinition
        {
            DisplayName = "Auto",
            Kind = ProcessKind.Executable,
            CommandPath = @"C:\tools\auto.exe",
            AutoStartOnLaunch = true,
            StartOrder = 1
        };
        var manual = auto with { Id = Guid.NewGuid(), DisplayName = "Manual", AutoStartOnLaunch = false, StartOrder = 2 };
        settings.AddOrUpdate(auto);
        settings.AddOrUpdate(manual);

        var oldPath = WindowsRunRegistry.FormatValue(@"C:\old\raccoonwatch.exe");
        var registry = new FakeAutostart { Enabled = true, ConfiguredPath = oldPath };
        var processes = new FakeProcessManager();
        var controller = new AppController(settings, processes, registry, NullLogger<AppController>.Instance);

        await controller.StartupAsync();

        Assert.True(settings.Settings.StartWithWindowsMirrored);
        Assert.Single(processes.Started);
        Assert.Equal("Auto", processes.Started[0].DisplayName);
        Assert.False(processes.StopAllCalled);

        settings.Settings.StopProcessesOnExit = true;
        await controller.ExitAsync();
        Assert.True(processes.StopAllCalled);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch
        {
            // Temporary directory cleanup is best-effort.
        }
    }

    private sealed class FakeAutostart : IAutostartService
    {
        public bool Enabled { get; set; }

        public string? ConfiguredPath { get; set; }

        public bool IsEnabled() => Enabled;

        public string? GetConfiguredPath() => ConfiguredPath;

        public bool IsPathCurrent(string executablePath) => ConfiguredPath == WindowsRunRegistry.FormatValue(executablePath);

        public void SetEnabled(bool enabled, string executablePath)
        {
            Enabled = enabled;
            ConfiguredPath = enabled ? WindowsRunRegistry.FormatValue(executablePath) : null;
        }
    }

    private sealed class FakeProcessManager : IProcessManager
    {
        public List<ProcessDefinition> Started { get; } = [];

        public bool StopAllCalled { get; private set; }

        public event EventHandler<ProcessStateChangedEventArgs>? StateChanged;

        public RuntimeState GetState(Guid definitionId) => RuntimeState.Initial(definitionId);

        public IReadOnlyDictionary<Guid, RuntimeState> GetStates() => new Dictionary<Guid, RuntimeState>();

        public Task<ProcessResult> StartAsync(ProcessDefinition definition)
        {
            Started.Add(definition);
            StateChanged?.Invoke(this, new ProcessStateChangedEventArgs(definition.Id, RuntimeState.Initial(definition.Id)));
            return Task.FromResult(ProcessResult.Ok());
        }

        public Task<ProcessResult> StopAsync(Guid definitionId) => Task.FromResult(ProcessResult.Ok());

        public Task<ProcessResult> RestartAsync(ProcessDefinition definition) => Task.FromResult(ProcessResult.Ok());

        public Task StartAllAsync(IEnumerable<ProcessDefinition> definitions)
        {
            Started.AddRange(definitions);
            return Task.CompletedTask;
        }

        public Task StartAutostartAsync(IEnumerable<ProcessDefinition> definitions)
        {
            Started.AddRange(definitions.Where(d => d.AutoStartOnLaunch));
            return Task.CompletedTask;
        }

        public Task StopAllAsync()
        {
            StopAllCalled = true;
            return Task.CompletedTask;
        }

        public Task ShutdownAsync()
        {
            StopAllCalled = true;
            return Task.CompletedTask;
        }
    }
}
