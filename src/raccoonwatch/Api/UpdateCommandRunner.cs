using Raccoonwatch.Infrastructure;
using Raccoonwatch.Models;

namespace Raccoonwatch.Api;

/// <summary>Outcome of running a process's configured update command.</summary>
public sealed record UpdateRunResult(bool Success, int? ExitCode, string? Error)
{
    public static UpdateRunResult Ok(int? exitCode) => new(true, exitCode, null);

    public static UpdateRunResult Failed(string error) => new(false, null, error);
}

/// <summary>Runs a one-off update command for a managed process.</summary>
public interface IUpdateCommandRunner
{
    Task<UpdateRunResult> RunAsync(
        ProcessDefinition definition,
        CancellationToken cancellationToken = default);
}

/// <summary>Launches the configured updater and waits for it to finish.</summary>
public sealed class UpdateCommandRunner : IUpdateCommandRunner
{
    public async Task<UpdateRunResult> RunAsync(
        ProcessDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (string.IsNullOrWhiteSpace(definition.UpdateCommandPath))
        {
            return UpdateRunResult.Failed("No update command is configured for this process.");
        }

        try
        {
            using var process = UpdateCommandLauncher.Launch(
                definition.UpdateCommandPath, definition.UpdateArguments);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            var exitCode = process.ExitCode;
            return exitCode == 0
                ? UpdateRunResult.Ok(exitCode)
                : UpdateRunResult.Failed($"Update command exited with code {exitCode}.");
        }
        catch (Exception ex)
        {
            return UpdateRunResult.Failed(ex.Message);
        }
    }
}
