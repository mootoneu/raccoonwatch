using System.Text.Json;
using Raccoonwatch.Api;

namespace Raccoonwatch.Cli;

internal static class Program
{
    private static readonly Lazy<ApiClient> Client = new(() =>
        new ApiClient(Environment.GetEnvironmentVariable("RACCOONWATCH_PIPE")));

    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintHelp();
            return 0;
        }

        var options = CliOptions.Parse(args[1..]);
        if (options.Error is not null)
        {
            return Fail(options.Error);
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "status" => await StatusAsync(options).ConfigureAwait(false),
                "list" => await ListAsync(options).ConfigureAwait(false),
                "start" => await ProcessActionAsync(ApiMethods.Start, options).ConfigureAwait(false),
                "stop" => await ProcessActionAsync(ApiMethods.Stop, options).ConfigureAwait(false),
                "restart" => await ProcessActionAsync(ApiMethods.Restart, options).ConfigureAwait(false),
                "update" => await ProcessActionAsync(ApiMethods.Update, options).ConfigureAwait(false),
                "ping" => await SendAsync(new ApiRequest { Method = ApiMethods.Ping }, options).ConfigureAwait(false),
                _ => Fail($"Unknown command '{args[0]}'. Run 'raccoonwatch-cli --help' for usage.")
            };
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            return Fail("raccoonwatch is not running (no response from the local API).");
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    private static async Task<int> StatusAsync(CliOptions options)
    {
        if (options.Id is not null)
        {
            return await SendAsync(
                new ApiRequest { Method = ApiMethods.GetStatus, Id = options.Id },
                options).ConfigureAwait(false);
        }

        return await ListAsync(options).ConfigureAwait(false);
    }

    private static async Task<int> ListAsync(CliOptions options) =>
        await SendAsync(new ApiRequest { Method = ApiMethods.List }, options).ConfigureAwait(false);

    private static async Task<int> ProcessActionAsync(string method, CliOptions options)
    {
        if (options.Id is null)
        {
            return Fail("A process id is required. Use 'list' to find it.");
        }

        return await SendAsync(
            new ApiRequest { Method = method, Id = options.Id },
            options).ConfigureAwait(false);
    }

    private static async Task<int> SendAsync(ApiRequest request, CliOptions options)
    {
        var response = await Client.Value.SendAsync(request).ConfigureAwait(false);
        if (!response.Success)
        {
            return Fail(response.Error ?? "The operation failed.");
        }

        if (options.JsonOutput)
        {
            Console.WriteLine(ApiProtocol.SerializeResponse(response));
        }
        else
        {
            WriteHumanReadable(request.Method, response);
        }

        return 0;
    }

    private static void WriteHumanReadable(string method, ApiResponse response)
    {
        if (method == ApiMethods.Ping)
        {
            Console.WriteLine("raccoonwatch is running.");
            return;
        }

        var data = response.Data;
        if (data is null || data.Value.ValueKind == JsonValueKind.Null)
        {
            Console.WriteLine("OK");
            return;
        }

        if (data.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in data.Value.EnumerateArray())
            {
                WriteProcessLine(element);
            }

            return;
        }

        if (data.Value.ValueKind == JsonValueKind.Object &&
            data.Value.TryGetProperty("id", out _))
        {
            WriteProcessLine(data.Value);
            return;
        }

        Console.WriteLine(ApiProtocol.SerializeResponse(response));
    }

    private static void WriteProcessLine(JsonElement element)
    {
        string Get(string name)
        {
            if (!element.TryGetProperty(name, out var value))
            {
                return string.Empty;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? string.Empty,
                JsonValueKind.Number => value.ToString(),
                _ => string.Empty
            };
        }

        var uptime = element.TryGetProperty("uptimeSeconds", out var u) &&
                     u.ValueKind == JsonValueKind.Number
            ? UptimeFormatter.Format(u.GetDouble())
            : "-";

        var id = Get("id");
        Console.WriteLine(
            $"{Get("displayName"),-24} {Get("status"),-10} up {uptime,-12} pid {(Get("processId") is { Length: > 0 } p ? p : "-"),-8} {id}");
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"error: {message}");
        return 1;
    }

    private static bool IsHelp(string value) =>
        value is "--help" or "-h" or "help" or "/?";

    private static void PrintHelp()
    {
        Console.WriteLine("""
            raccoonwatch-cli - control the running raccoonwatch instance

            Usage:
              raccoonwatch-cli <command> [options]

            Commands:
              list                         List all configured processes with status and uptime
              status [--id <guid>]         Show one process (or all when --id is omitted)
              start --id <guid>            Start a process
              stop --id <guid>             Stop a process
              restart --id <guid>          Restart a process
              update --id <guid>           Run the configured update command for a process
              ping                         Check that the local API server is reachable

            Options:
              --id <guid>        Process id (see 'list')
              --json-output      Print raw JSON responses
              -h, --help         Show this help

            Environment:
              RACCOONWATCH_PIPE  Override the local named pipe name.

            """);
    }
}
