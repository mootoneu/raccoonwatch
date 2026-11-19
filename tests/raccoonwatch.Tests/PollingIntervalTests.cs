using Microsoft.Extensions.Logging.Abstractions;
using Raccoonwatch.Infrastructure;
using Raccoonwatch.Models;
using Raccoonwatch.Services;

namespace Raccoonwatch.Tests;

/// <summary>Tests adaptive status polling after a Start/Stop/Restart action.</summary>
public sealed class PollingIntervalTests
{
    private long _now = 1000;

    [Fact]
    public void Provider_UsesNormalIntervalByDefault()
    {
        var provider = CreateProvider(new FakeSettings());

        Assert.Equal(2000, provider.GetIntervalMs());
    }

    [Fact]
    public void Provider_UsesFastIntervalUntilWindowElapses()
    {
        var provider = CreateProvider(new FakeSettings());
        provider.NotifyActivity();

        Assert.Equal(250, provider.GetIntervalMs());
        Advance(119_999);
        Assert.Equal(250, provider.GetIntervalMs());
        Advance(1);
        Assert.Equal(2000, provider.GetIntervalMs());
    }

    [Fact]
    public void Provider_ActivityExtendsFastWindow()
    {
        var provider = CreateProvider(new FakeSettings());
        provider.NotifyActivity();

        Advance(100_000);
        provider.NotifyActivity();
        Advance(100_000);

        Assert.Equal(250, provider.GetIntervalMs());
        Advance(20_000);
        Assert.Equal(2000, provider.GetIntervalMs());
    }

    [Fact]
    public void Provider_UsesConfiguredDurations()
    {
        var settings = new FakeSettings();
        settings.Settings.PollIntervalMs = 1500;
        settings.Settings.FastPollIntervalMs = 500;
        settings.Settings.FastPollWindowMs = 1000;
        var provider = CreateProvider(settings);

        Assert.Equal(1500, provider.GetIntervalMs());
        provider.NotifyActivity();
        Assert.Equal(500, provider.GetIntervalMs());
        Advance(1000);
        Assert.Equal(1500, provider.GetIntervalMs());
    }

    [Fact]
    public void Provider_ClampsFastIntervalToMinimum()
    {
        var settings = new FakeSettings();
        settings.Settings.FastPollIntervalMs = 10;
        settings.Settings.FastPollWindowMs = 1000;
        var provider = CreateProvider(settings);

        provider.NotifyActivity();

        Assert.Equal(250, provider.GetIntervalMs());
    }

    [Fact]
    public void Provider_NeverPollsSlowerThanNormalDuringFastWindow()
    {
        var settings = new FakeSettings();
        settings.Settings.PollIntervalMs = 1000;
        settings.Settings.FastPollIntervalMs = 5000;
        var provider = CreateProvider(settings);

        provider.NotifyActivity();

        Assert.Equal(1000, provider.GetIntervalMs());
    }

    [Fact]
    public void Provider_NegativeWindowDisablesFastPolling()
    {
        var settings = new FakeSettings();
        settings.Settings.FastPollWindowMs = -5;
        var provider = CreateProvider(settings);

        provider.NotifyActivity();

        Assert.Equal(2000, provider.GetIntervalMs());
    }

    [Fact]
    public void Provider_ClampsWindowToMaximum()
    {
        var settings = new FakeSettings();
        settings.Settings.FastPollWindowMs = int.MaxValue;
        var provider = CreateProvider(settings);

        provider.NotifyActivity();
        Advance(PollingLimits.MaxWindowMs - 1);
        Assert.Equal(250, provider.GetIntervalMs());
        Advance(1);
        Assert.Equal(2000, provider.GetIntervalMs());
    }

    [Fact]
    public async Task ProcessManager_StartStopRestart_NotifyPollingProvider()
    {
        var settings = new FakeSettings();
        var definition = new ProcessDefinition
        {
            DisplayName = "Demo",
            Kind = ProcessKind.Executable,
            CommandPath = @"C:\tools\demo.exe"
        };
        settings.Settings.Processes.Add(definition);
        var polling = new SpyPollingProvider();
        using var output = new NoopOutputStore();
        var manager = new ProcessManager(
            new ThrowingLauncher(),
            output,
            settings,
            polling,
            NullLogger<ProcessManager>.Instance);

        await manager.StartAsync(definition);
        await manager.StopAsync(definition.Id);
        await manager.RestartAsync(definition);
        await manager.ShutdownAsync();

        // Start + Stop + (Restart's own notification + Stop + Start).
        Assert.Equal(5, polling.NotifyCalls);
    }

    private PollingIntervalProvider CreateProvider(ISettingsService settings) =>
        new(settings, () => _now);

    private void Advance(long milliseconds) => _now += milliseconds;

    private sealed class FakeSettings : ISettingsService
    {
        public AppSettings Settings { get; } = new();

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public void Load()
        {
        }

        public void Save()
        {
        }

        public IReadOnlyList<string> Validate(ProcessDefinition definition, Guid? ignoreId = null) => [];

        public void AddOrUpdate(ProcessDefinition definition)
        {
        }

        public void Delete(Guid id)
        {
        }
    }

    private sealed class SpyPollingProvider : IPollingIntervalProvider
    {
        public int NotifyCalls { get; private set; }

        public int GetIntervalMs() => 2000;

        public void NotifyActivity() => NotifyCalls++;
    }

    private sealed class ThrowingLauncher : IProcessLauncher
    {
        public System.Diagnostics.Process Start(ProcessDefinition definition) =>
            throw new InvalidOperationException("Launch failed.");
    }

    private sealed class NoopOutputStore : IOutputStore
    {
        public event EventHandler<OutputBatchEventArgs>? OutputReceived
        {
            add { }
            remove { }
        }

        public int HistoryLimit { get; set; }

        public void Attach(Guid definitionId, System.Diagnostics.Process process)
        {
        }

        public IReadOnlyList<OutputEntry> GetSnapshot(Guid definitionId) => [];

        public void Append(Guid definitionId, OutputEntry entry)
        {
        }

        public void AddSeparator(Guid definitionId, string text)
        {
        }

        public void Clear(Guid definitionId)
        {
        }

        public void Dispose()
        {
        }
    }
}
