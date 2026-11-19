using System.Text.Json;

namespace Raccoonwatch.Api;

/// <summary>A single command sent from a client to the running raccoonwatch instance.</summary>
public sealed record ApiRequest
{
    /// <summary>One of the <see cref="ApiMethods"/> values.</summary>
    public string Method { get; init; } = string.Empty;

    /// <summary>Process id as a GUID string, for process-specific methods.</summary>
    public string? Id { get; init; }
}

/// <summary>The result of an API operation.</summary>
public sealed record ApiResponse
{
    public bool Success { get; init; }

    public JsonElement? Data { get; init; }

    public string? Error { get; init; }

    public static ApiResponse Ok(object? data = null) => new()
    {
        Success = true,
        Data = data is null ? null : JsonSerializer.SerializeToElement(data, ApiProtocol.JsonOptions)
    };

    public static ApiResponse Fail(string error) => new() { Success = false, Error = error };

    /// <summary>Deserializes the data payload into the requested type.</summary>
    public T? GetData<T>()
    {
        if (Data is null || Data.Value.ValueKind == JsonValueKind.Null)
        {
            return default;
        }

        return Data.Value.Deserialize<T>(ApiProtocol.JsonOptions);
    }
}

/// <summary>A process definition combined with a snapshot of its runtime state.</summary>
public sealed record ProcessInfoDto
{
    public Guid Id { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public int? ProcessId { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? ExitedAt { get; init; }

    /// <summary>Current uptime in seconds, computed by the running instance.</summary>
    public double? UptimeSeconds { get; init; }

    public int? ExitCode { get; init; }

    public string? LastError { get; init; }

    public int RestartCount { get; init; }

    public string CommandPath { get; init; } = string.Empty;

    public string Arguments { get; init; } = string.Empty;

    public string WorkingDirectory { get; init; } = string.Empty;

    public string UpdateCommandPath { get; init; } = string.Empty;

    public string UpdateArguments { get; init; } = string.Empty;

    public int StartOrder { get; init; }

    public bool AutoStartOnLaunch { get; init; }

    public string Notes { get; init; } = string.Empty;
}
