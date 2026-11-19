using System.Text.Json;
using System.Text.Json.Serialization;

namespace Raccoonwatch.Api;

/// <summary>Names of the operations exposed by the local control API.</summary>
public static class ApiMethods
{
    public const string Ping = "ping";
    public const string List = "list";
    public const string GetStatus = "get_status";
    public const string Start = "start";
    public const string Stop = "stop";
    public const string Restart = "restart";
    public const string RunUpdate = "run_update";
}

/// <summary>
/// Wire protocol shared by the running raccoonwatch instance and its command-line client.
/// Each connection carries exactly one UTF-8 JSON request line followed by one JSON response line.
/// </summary>
public static class ApiProtocol
{
    /// <summary>Default local named pipe used by raccoonwatch and the companion CLI.</summary>
    public const string DefaultPipeName = "raccoonwatch.api";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static string SerializeRequest(ApiRequest request) =>
        JsonSerializer.Serialize(request, JsonOptions);

    public static ApiRequest? DeserializeRequest(string json) =>
        JsonSerializer.Deserialize<ApiRequest>(json, JsonOptions);

    public static string SerializeResponse(ApiResponse response) =>
        JsonSerializer.Serialize(response, JsonOptions);

    public static ApiResponse? DeserializeResponse(string json) =>
        JsonSerializer.Deserialize<ApiResponse>(json, JsonOptions);
}
