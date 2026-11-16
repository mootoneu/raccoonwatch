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
}
