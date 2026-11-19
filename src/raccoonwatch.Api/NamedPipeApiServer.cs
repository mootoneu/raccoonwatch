using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Raccoonwatch.Api;

/// <summary>
/// Hosts the local control API on a named pipe. The server accepts one request per
/// connection and is independent of the UI thread so it can run inside the tray app.
/// </summary>
public sealed class NamedPipeApiServer : IDisposable
{
    private const int MaxServerInstances = 8;
    private const int ConnectWaitMilliseconds = 2000;

    private readonly string _pipeName;
    private readonly IApiRequestHandler _handler;
    private readonly Action<Exception>? _onError;
    private readonly object _gate = new();

    private CancellationTokenSource? _cancellation;
    private NamedPipeServerStream? _currentServer;
    private Task? _listener;
    private bool _disposed;

    public NamedPipeApiServer(string pipeName, IApiRequestHandler handler, Action<Exception>? onError = null)
    {
        if (string.IsNullOrWhiteSpace(pipeName))
        {
            throw new ArgumentException("Pipe name cannot be whitespace.", nameof(pipeName));
        }

        _pipeName = pipeName;
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _onError = onError;
    }

    public string PipeName => _pipeName;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _listener is not null && !_listener.IsCompleted;
            }
        }
    }

    /// <summary>Starts listening for client connections. Safe to call more than once.</summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_listener is not null)
            {
                return Task.CompletedTask;
            }

            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var server = CreateServer();
            _currentServer = server;
            _listener = Task.Run(() => ListenAsync(server, _cancellation.Token));
        }

        return Task.CompletedTask;
    }

    /// <summary>Stops listening and waits briefly for the listener to unwind.</summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? cancellation;
        NamedPipeServerStream? server;
        Task? listener;

        lock (_gate)
        {
            cancellation = _cancellation;
            server = _currentServer;
            listener = _listener;
            _cancellation = null;
            _currentServer = null;
            _listener = null;
        }

        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        server?.Dispose();

        if (listener is not null)
        {
            try
            {
                await listener.WaitAsync(TimeSpan.FromMilliseconds(ConnectWaitMilliseconds), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // The listener is blocked in an OS call; it will exit once the pipe is disposed.
            }
            catch (OperationCanceledException)
            {
            }
        }

        cancellation.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        StopAsync().GetAwaiter().GetResult();
        _disposed = true;
    }

    private NamedPipeServerStream CreateServer() => new(
        _pipeName,
        PipeDirection.InOut,
        MaxServerInstances,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous);

    private async Task ListenAsync(NamedPipeServerStream initialServer, CancellationToken cancellationToken)
    {
        var server = initialServer;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                lock (_gate)
                {
                    _currentServer = server;
                }

                try
                {
                    await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                    await ServeAsync(server, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // A malformed or abrupt client must not take down the whole API.
                    _onError?.Invoke(ex);
                }
                finally
                {
                    server.Dispose();
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                server = CreateServer();
            }
        }
        finally
        {
            server.Dispose();
        }
    }

    private async Task ServeAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        var encoding = new UTF8Encoding(false);
        using var reader = new StreamReader(
            server, encoding, detectEncodingFromByteOrderMarks: false, 1024, leaveOpen: true);
        using var writer = new StreamWriter(server, encoding, 1024, leaveOpen: true)
        {
            AutoFlush = true,
            NewLine = "\n"
        };

        ApiResponse response;

        try
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                response = ApiResponse.Fail("Empty request.");
            }
            else
            {
                var request = ApiProtocol.DeserializeRequest(line);
                response = request is null
                    ? ApiResponse.Fail("Unreadable request.")
                    : await _handler.HandleAsync(request, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (JsonException ex)
        {
            _onError?.Invoke(ex);
            response = ApiResponse.Fail("Unreadable request.");
        }
        catch (Exception ex)
        {
            _onError?.Invoke(ex);
            response = ApiResponse.Fail("The request could not be processed.");
        }

        await writer.WriteLineAsync(ApiProtocol.SerializeResponse(response).AsMemory(), cancellationToken)
            .ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
