using System.IO.Pipes;
using System.Text;

namespace Raccoonwatch.Api;

/// <summary>
/// Sends a single request to the running raccoonwatch instance over a local named pipe
/// and returns its response. One instance can be reused for several sequential calls.
/// </summary>
public sealed class ApiClient
{
    private readonly string _pipeName;
    private readonly int _connectTimeoutMs;

    public ApiClient(string? pipeName = null, int connectTimeoutMs = 3000)
    {
        if (pipeName is not null && string.IsNullOrWhiteSpace(pipeName))
        {
            throw new ArgumentException("Pipe name cannot be whitespace.", nameof(pipeName));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(connectTimeoutMs);

        _pipeName = string.IsNullOrWhiteSpace(pipeName) ? ApiProtocol.DefaultPipeName : pipeName;
        _connectTimeoutMs = connectTimeoutMs;
    }

    public string PipeName => _pipeName;

    /// <summary>
    /// Connects to the local API, writes one request line and reads one response line.
    /// Throws <see cref="TimeoutException"/> when raccoonwatch is not running.
    /// </summary>
    public async Task<ApiResponse> SendAsync(
        ApiRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_connectTimeoutMs);

        using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut);
        await client.ConnectAsync(timeout.Token).ConfigureAwait(false);

        var encoding = new UTF8Encoding(false);
        using var writer = new StreamWriter(client, encoding, 1024, leaveOpen: true)
        {
            AutoFlush = true,
            NewLine = "\n"
        };
        using var reader = new StreamReader(
            client, encoding, detectEncodingFromByteOrderMarks: false, 1024, leaveOpen: true);

        await writer.WriteLineAsync(ApiProtocol.SerializeRequest(request).AsMemory(), cancellationToken)
            .ConfigureAwait(false);

        var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (line is null)
        {
            throw new IOException("The API server closed the connection without a response.");
        }

        return ApiProtocol.DeserializeResponse(line)
               ?? throw new IOException("The API server returned an unreadable response.");
    }
}
