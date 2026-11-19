using Microsoft.Extensions.Logging;
using Raccoonwatch.Models;
using Raccoonwatch.Services;

namespace Raccoonwatch.Api;

/// <summary>
/// Translates protocol requests into calls on the existing application services.
/// All data returned here is a plain DTO, so the protocol layer never touches WinForms types.
/// </summary>
public sealed class ApiRequestHandler(
    ISettingsService settings,
    IProcessManager processes,
    IUpdateCommandRunner updateRunner,
    ILogger<ApiRequestHandler> logger) : IApiRequestHandler
{
    public Task<ApiResponse> HandleAsync(ApiRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogDebug("API request: {Method}", request.Method);

        return request.Method switch
        {
            ApiMethods.Ping => Task.FromResult(ApiResponse.Ok(new { pong = true })),
            ApiMethods.List => ListProcesses(),
            ApiMethods.GetStatus => WithProcess(
                request.Id,
                (definition, _) => Task.FromResult(ApiResponse.Ok(Snapshot(definition))),
                cancellationToken),
            ApiMethods.Start => WithProcess(
                request.Id,
                async (definition, token) => FromResult(
                    await processes.StartAsync(definition).ConfigureAwait(false), definition),
                cancellationToken),
            ApiMethods.Stop => WithProcess(
                request.Id,
                async (definition, token) => FromResult(
                    await processes.StopAsync(definition.Id).ConfigureAwait(false), definition),
                cancellationToken),
            ApiMethods.Restart => WithProcess(
                request.Id,
                async (definition, token) => FromResult(
                    await processes.RestartAsync(definition).ConfigureAwait(false), definition),
                cancellationToken),
            ApiMethods.Update => WithProcess(
                request.Id,
                async (definition, token) =>
                {
                    var result = await updateRunner.RunAsync(definition, token).ConfigureAwait(false);
                    return result.Success
                        ? ApiResponse.Ok(Snapshot(definition))
                        : ApiResponse.Fail(result.Error ?? "The update command failed.");
                },
                cancellationToken),
            _ => Task.FromResult(ApiResponse.Fail($"Unknown method '{request.Method}'."))
        };
    }

    private Task<ApiResponse> ListProcesses()
    {
        var items = settings.Settings.Processes
            .Select(definition => Snapshot(definition))
            .ToArray();
        return Task.FromResult(ApiResponse.Ok(items));
    }

    private async Task<ApiResponse> WithProcess(
        string? id,
        Func<ProcessDefinition, CancellationToken, Task<ApiResponse>> action,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var definitionId) || definitionId == Guid.Empty)
        {
            return ApiResponse.Fail("A valid process id is required.");
        }

        var definition = settings.Settings.Processes.FirstOrDefault(p => p.Id == definitionId);
        if (definition is null)
        {
            return ApiResponse.Fail($"Process '{definitionId}' was not found.");
        }

        return await action(definition, cancellationToken).ConfigureAwait(false);
    }

    private ApiResponse FromResult(ProcessResult result, ProcessDefinition definition) =>
        result.Success
            ? ApiResponse.Ok(Snapshot(definition))
            : ApiResponse.Fail(result.Error ?? "The operation failed.");

    private ProcessInfoDto Snapshot(ProcessDefinition definition)
    {
        var state = processes.GetState(definition.Id);
        return new ProcessInfoDto
        {
            Id = definition.Id,
            DisplayName = definition.DisplayName,
            Kind = definition.Kind.ToString(),
            Status = state.Status.ToString(),
            ProcessId = state.ProcessId,
            StartedAt = state.StartedAt,
            ExitedAt = state.ExitedAt,
            UptimeSeconds = state.Uptime?.TotalSeconds,
            ExitCode = state.ExitCode,
            LastError = state.LastError,
            RestartCount = state.RestartCount,
            CommandPath = definition.CommandPath,
            Arguments = definition.Arguments,
            WorkingDirectory = definition.WorkingDirectory,
            UpdateCommandPath = definition.UpdateCommandPath,
            UpdateArguments = definition.UpdateArguments,
            StartOrder = definition.StartOrder,
            AutoStartOnLaunch = definition.AutoStartOnLaunch,
            Notes = definition.Notes
        };
    }
}
