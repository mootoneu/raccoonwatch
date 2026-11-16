using Raccoonwatch.Models;

namespace Raccoonwatch.Services;

public sealed record ProcessResult(bool Success, string? Error = null)
{
    public static ProcessResult Ok() => new(true);

    public static ProcessResult Failed(string error) => new(false, error);
}

public sealed class ProcessStateChangedEventArgs(Guid definitionId, RuntimeState state) : EventArgs
{
    public Guid DefinitionId { get; } = definitionId;

    public RuntimeState State { get; } = state;
}

public interface IProcessManager
{
    event EventHandler<ProcessStateChangedEventArgs>? StateChanged;

    RuntimeState GetState(Guid definitionId);

    IReadOnlyDictionary<Guid, RuntimeState> GetStates();

    Task<ProcessResult> StartAsync(ProcessDefinition definition);

    Task<ProcessResult> StopAsync(Guid definitionId);

    Task<ProcessResult> RestartAsync(ProcessDefinition definition);

    Task StartAllAsync(IEnumerable<ProcessDefinition> definitions);

    Task StartAutostartAsync(IEnumerable<ProcessDefinition> definitions);

    Task StopAllAsync();

    Task ShutdownAsync();
}
