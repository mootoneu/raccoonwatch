using System.IO.Pipes;

namespace Raccoonwatch.Infrastructure;

/// <summary>
/// Enforces a single instance via a named mutex. A named pipe lets a second launch
/// ask the running instance to bring its Manage Processes window to the foreground.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Local\raccoonwatch.singleinstance";
    private const string PipeName = "raccoonwatch.ipc";
    private const string ShowManageMessage = "SHOW_MANAGE";

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cancellation = new();
    private NamedPipeServerStream? _server;
    private Task? _listener;

    public SingleInstanceGuard()
    {
        _mutex = new Mutex(true, MutexName, out var createdNew);
        IsFirstInstance = createdNew;

        if (createdNew)
        {
            StartListening();
        }
    }

    public bool IsFirstInstance { get; }

    public event Action? ShowManageRequested;

    public void SignalFirstInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(1500);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(ShowManageMessage);
        }
        catch
        {
            // The running instance may be shutting down; nothing else we can do.
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _server?.Dispose();

        try
        {
            _listener?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // Best-effort shutdown.
        }

        _cancellation.Dispose();
        _mutex.Dispose();
    }

    private void StartListening()
    {
        try
        {
            _server = new NamedPipeServerStream(
                PipeName,
                PipeDirection.In,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            _listener = Task.Run(ListenAsync);
        }
        catch
        {
            // Tray functionality remains available even if IPC cannot be created.
        }
    }

    private async Task ListenAsync()
    {
        try
        {
            while (!_cancellation.IsCancellationRequested && _server is not null)
            {
                await _server.WaitForConnectionAsync(_cancellation.Token);

                using (var reader = new StreamReader(_server, leaveOpen: true))
                {
                    var message = await reader.ReadLineAsync();
                    if (message == ShowManageMessage)
                    {
                        ShowManageRequested?.Invoke();
                    }
                }

                if (_server.IsConnected)
                {
                    _server.Disconnect();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch
        {
            // Ignore IPC failures; they are non-critical.
        }
    }
}
