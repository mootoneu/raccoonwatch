using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Raccoonwatch.Infrastructure;
using Raccoonwatch.Models;

namespace Raccoonwatch.Services;

/// <summary>Owns runtime state for managed processes and polls for unexpected exits.</summary>
public sealed class ProcessManager : IProcessManager
{
    private static readonly TimeSpan GracefulStopTimeout = TimeSpan.FromSeconds(5);

    private readonly IProcessLauncher _launcher;
    private readonly IOutputStore _outputStore;
    private readonly ISettingsService _settings;
    private readonly ILogger<ProcessManager> _logger;
    private readonly ConcurrentDictionary<Guid, TrackedProcess> _tracked = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _gates = new();
    private readonly ConcurrentDictionary<Guid, int> _restartCounts = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _monitorTask;

    public ProcessManager(
        IProcessLauncher launcher,
        IOutputStore outputStore,
        ISettingsService settings,
        ILogger<ProcessManager> logger)
    {
        _launcher = launcher;
        _outputStore = outputStore;
        _settings = settings;
        _logger = logger;
        _monitorTask = Task.Run(MonitorLoopAsync);
    }

    public event EventHandler<ProcessStateChangedEventArgs>? StateChanged;

    public RuntimeState GetState(Guid definitionId) =>
        _tracked.TryGetValue(definitionId, out var tracked)
            ? tracked.State
            : RuntimeState.Initial(definitionId);

    public IReadOnlyDictionary<Guid, RuntimeState> GetStates() =>
        _tracked.ToDictionary(pair => pair.Key, pair => pair.Value.State);

    public async Task<ProcessResult> StartAsync(ProcessDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var gate = GetGate(definition.Id);
        await gate.WaitAsync();
        try
        {
            if (_tracked.TryGetValue(definition.Id, out var existing) &&
                existing.Process is not null &&
                existing.State.Status is ProcessStatus.Starting or ProcessStatus.Running or ProcessStatus.Stopping)
            {
                return ProcessResult.Failed("The process is already running.");
            }

            var tracked = existing ?? new TrackedProcess(definition);
            tracked.Definition = definition;
            tracked.StopRequested = false;

            var starting = Merge(tracked.State) with
            {
                Status = ProcessStatus.Starting,
                LastError = null,
                ExitCode = null,
                ExitedAt = null,
                StartedAt = null,
                ProcessId = null,
                RestartCount = _restartCounts.GetValueOrDefault(definition.Id)
            };
            tracked.State = starting;
            _tracked[definition.Id] = tracked;
            RaiseStateChanged(definition.Id, starting);

            try
            {
                var process = _launcher.Start(definition);
                tracked.Process = process;

                var running = starting with
                {
                    Status = ProcessStatus.Running,
                    ProcessId = process.Id,
                    StartedAt = DateTimeOffset.Now
                };
                tracked.State = running;

                _outputStore.Attach(definition.Id, process);
                RaiseStateChanged(definition.Id, running);

                _logger.LogInformation("Started '{Name}' (PID {Pid}).", definition.DisplayName, process.Id);
                return ProcessResult.Ok();
            }
            catch (Exception ex)
            {
                var failed = starting with
                {
                    Status = ProcessStatus.Failed,
                    LastError = ex.Message,
                    ExitedAt = DateTimeOffset.Now
                };
                tracked.State = failed;
                RaiseStateChanged(definition.Id, failed);

                _logger.LogError(ex, "Failed to start '{Name}'.", definition.DisplayName);
                return ProcessResult.Failed(ex.Message);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ProcessResult> StopAsync(Guid definitionId)
    {
        var gate = GetGate(definitionId);
        await gate.WaitAsync();
        try
        {
            if (!_tracked.TryGetValue(definitionId, out var tracked))
            {
                return ProcessResult.Ok();
            }

            if (tracked.Process is null)
            {
                if (tracked.State.Status != ProcessStatus.Stopped)
                {
                    var stopped = Merge(tracked.State) with
                    {
                        Status = ProcessStatus.Stopped,
                        ProcessId = null,
                        LastError = null
                    };
                    tracked.State = stopped;
                    RaiseStateChanged(definitionId, stopped);
                }

                return ProcessResult.Ok();
            }

            tracked.StopRequested = true;
            var stopping = tracked.State with { Status = ProcessStatus.Stopping };
            tracked.State = stopping;
            RaiseStateChanged(definitionId, stopping);

            var process = tracked.Process;
            try
            {
                if (!process.HasExited)
                {
                    TryCloseMainWindow(process);
                    if (!await WaitForExitAsync(process, GracefulStopTimeout))
                    {
                        TryKill(process);
                        await WaitForExitAsync(process, TimeSpan.FromSeconds(2));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while stopping '{Name}'.", tracked.Definition.DisplayName);
            }

            var exitCode = TryReadExitCode(process);
            var final = tracked.State with
            {
                Status = ProcessStatus.Stopped,
                ProcessId = null,
                ExitCode = exitCode,
                ExitedAt = DateTimeOffset.Now,
                LastError = null
            };
            tracked.State = final;
            tracked.Process = null;
            SafeDispose(process);
            RaiseStateChanged(definitionId, final);

            _logger.LogInformation("Stopped '{Name}'.", tracked.Definition.DisplayName);
            return ProcessResult.Ok();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ProcessResult> RestartAsync(ProcessDefinition definition)
    {
        _restartCounts.AddOrUpdate(definition.Id, 1, (_, count) => count + 1);
        _outputStore.AddSeparator(
            definition.Id,
            $"--- restarted at {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} ---");

        await StopAsync(definition.Id);
        return await StartAsync(definition);
    }

    public async Task StartAllAsync(IEnumerable<ProcessDefinition> definitions)
    {
        foreach (var definition in Order(definitions))
        {
            var result = await StartAsync(definition);
            if (!result.Success)
            {
                _logger.LogWarning("Start All: '{Name}' — {Error}", definition.DisplayName, result.Error);
            }
        }
    }

    public async Task StartAutostartAsync(IEnumerable<ProcessDefinition> definitions) =>
        await StartAllAsync(definitions.Where(d => d.AutoStartOnLaunch));

    public async Task StopAllAsync()
    {
        foreach (var definition in Order(_settings.Settings.Processes).Reverse())
        {
            await StopAsync(definition.Id);
        }

        foreach (var id in _tracked.Keys)
        {
            await StopAsync(id);
        }
    }

    public async Task ShutdownAsync()
    {
        _cancellation.Cancel();

        try
        {
            await _monitorTask;
        }
        catch
        {
            // Best-effort shutdown.
        }

        if (_settings.Settings.StopProcessesOnExit)
        {
            await StopAllAsync();
        }
    }

    private static IReadOnlyList<ProcessDefinition> Order(IEnumerable<ProcessDefinition> definitions) =>
        definitions
            .OrderBy(d => d.StartOrder)
            .ThenBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static RuntimeState Merge(RuntimeState state) => state with { };

    private static void TryCloseMainWindow(Process process)
    {
        try
        {
            process.CloseMainWindow();
        }
        catch
        {
            // Console processes have no main window; the timeout/kill path follows.
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // The process may have exited between the check and the kill.
        }
    }

    private static int? TryReadExitCode(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode : null;
        }
        catch
        {
            return null;
        }
    }

    private static void SafeDispose(Process process)
    {
        try
        {
            process.Dispose();
        }
        catch
        {
            // Ignore disposal failures.
        }
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private SemaphoreSlim GetGate(Guid id) => _gates.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));

    private void RaiseStateChanged(Guid id, RuntimeState state) =>
        StateChanged?.Invoke(this, new ProcessStateChangedEventArgs(id, state));

    private async Task MonitorLoopAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            var interval = Math.Clamp(_settings.Settings.PollIntervalMs, 250, 60_000);
            try
            {
                await Task.Delay(interval, _cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            foreach (var (id, tracked) in _tracked.ToArray())
            {
                var process = tracked.Process;
                if (process is null)
                {
                    continue;
                }

                var gate = GetGate(id);
                if (!await gate.WaitAsync(0))
                {
                    continue;
                }

                try
                {
                    if (tracked.Process is null || !process.HasExited)
                    {
                        continue;
                    }

                    var exitCode = TryReadExitCode(process);
                    var age = DateTimeOffset.Now - (tracked.State.StartedAt ?? DateTimeOffset.Now);
                    var failed = !tracked.StopRequested &&
                                 exitCode.GetValueOrDefault(0) != 0 &&
                                 (tracked.State.Status == ProcessStatus.Starting || age < TimeSpan.FromSeconds(2));
                    var status = tracked.StopRequested
                        ? ProcessStatus.Stopped
                        : failed
                            ? ProcessStatus.Failed
                            : ProcessStatus.Exited;

                    var state = tracked.State with
                    {
                        Status = status,
                        ExitCode = exitCode,
                        ExitedAt = DateTimeOffset.Now,
                        LastError = failed ? BuildErrorTail(id) : null
                    };
                    tracked.State = state;
                    tracked.Process = null;
                    SafeDispose(process);
                    RaiseStateChanged(id, state);

                    _logger.LogInformation(
                        "'{Name}' exited with code {Code} ({Status}).",
                        tracked.Definition.DisplayName,
                        exitCode,
                        status);
                }
                finally
                {
                    gate.Release();
                }
            }
        }
    }

    private string BuildErrorTail(Guid definitionId)
    {
        var lines = _outputStore.GetSnapshot(definitionId)
            .Where(entry => entry.IsStandardError)
            .TakeLast(3)
            .Select(entry => entry.Text);
        return string.Join(" | ", lines);
    }

    private sealed class TrackedProcess(ProcessDefinition definition)
    {
        public ProcessDefinition Definition { get; set; } = definition;

        public Process? Process { get; set; }

        public RuntimeState State { get; set; } = RuntimeState.Initial(definition.Id);

        public bool StopRequested { get; set; }
    }
}
