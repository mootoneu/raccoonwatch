using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Raccoonwatch.Models;

namespace Raccoonwatch.Services;

/// <summary>Bounded per-process output buffers with batched change notifications.</summary>
public sealed class OutputStore : IOutputStore
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(100);

    private readonly ConcurrentDictionary<Guid, LineBuffer> _buffers = new();
    private readonly ConcurrentDictionary<Guid, Process> _attached = new();
    private readonly ConcurrentDictionary<Guid, List<OutputEntry>> _pending = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly PeriodicTimer _timer = new(FlushInterval);
    private readonly Task _flushTask;
    private readonly ILogger<OutputStore> _logger;
    private int _historyLimit = 5000;

    public OutputStore(ILogger<OutputStore>? logger = null)
    {
        _logger = logger ?? NullLogger<OutputStore>.Instance;
        _flushTask = Task.Run(FlushLoopAsync);
    }

    public event EventHandler<OutputBatchEventArgs>? OutputReceived;

    public int HistoryLimit
    {
        get => _historyLimit;
        set
        {
            var limit = Math.Clamp(value, 100, 100_000);
            _historyLimit = limit;
            foreach (var buffer in _buffers.Values)
            {
                buffer.Trim(limit);
            }
        }
    }

    public void Attach(Guid definitionId, Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (!_attached.TryAdd(definitionId, process))
        {
            return;
        }

        DataReceivedEventHandler onOutput = (_, e) =>
        {
            if (e.Data is not null)
            {
                Append(definitionId, new OutputEntry(DateTimeOffset.Now, definitionId, false, e.Data));
            }
        };

        DataReceivedEventHandler onError = (_, e) =>
        {
            if (e.Data is not null)
            {
                Append(definitionId, new OutputEntry(DateTimeOffset.Now, definitionId, true, e.Data));
            }
        };

        EventHandler onExit = null!;
        onExit = (_, _) =>
        {
            process.OutputDataReceived -= onOutput;
            process.ErrorDataReceived -= onError;
            process.Exited -= onExit;
            _attached.TryRemove(definitionId, out _);
        };

        process.OutputDataReceived += onOutput;
        process.ErrorDataReceived += onError;
        process.Exited += onExit;

        try
        {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not begin async output capture for {DefinitionId}.", definitionId);
        }
    }

    public IReadOnlyList<OutputEntry> GetSnapshot(Guid definitionId) =>
        _buffers.TryGetValue(definitionId, out var buffer) ? buffer.Snapshot() : [];

    public void Append(Guid definitionId, OutputEntry entry)
    {
        var buffer = _buffers.GetOrAdd(definitionId, _ => new LineBuffer(_historyLimit));
        buffer.Append(entry, _historyLimit);

        var pending = _pending.GetOrAdd(definitionId, _ => []);
        lock (pending)
        {
            pending.Add(entry);
        }
    }

    public void AddSeparator(Guid definitionId, string text) =>
        Append(definitionId, new OutputEntry(DateTimeOffset.Now, definitionId, false, text));

    public void Clear(Guid definitionId)
    {
        if (_buffers.TryGetValue(definitionId, out var buffer))
        {
            buffer.Clear();
        }

        if (_pending.TryGetValue(definitionId, out var pending))
        {
            lock (pending)
            {
                pending.Clear();
            }
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _timer.Dispose();

        try
        {
            _flushTask.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // Best-effort shutdown.
        }

        _cancellation.Dispose();
    }

    private async Task FlushLoopAsync()
    {
        try
        {
            while (await _timer.WaitForNextTickAsync(_cancellation.Token))
            {
                foreach (var (_, pending) in _pending)
                {
                    OutputEntry[] batch;
                    lock (pending)
                    {
                        if (pending.Count == 0)
                        {
                            continue;
                        }

                        batch = pending.ToArray();
                        pending.Clear();
                    }

                    OutputReceived?.Invoke(this, new OutputBatchEventArgs(batch));
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private sealed class LineBuffer(int limit)
    {
        private readonly object _gate = new();
        private readonly Queue<OutputEntry> _entries = new();
        private int _limit = limit;

        public void Append(OutputEntry entry, int currentLimit)
        {
            lock (_gate)
            {
                _limit = currentLimit;
                _entries.Enqueue(entry);
                Trim(_limit);
            }
        }

        public void Trim(int newLimit)
        {
            lock (_gate)
            {
                _limit = newLimit;
                while (_entries.Count > _limit)
                {
                    _entries.Dequeue();
                }
            }
        }

        public IReadOnlyList<OutputEntry> Snapshot()
        {
            lock (_gate)
            {
                return _entries.ToArray();
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _entries.Clear();
            }
        }
    }
}
