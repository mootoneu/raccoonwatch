namespace Raccoonwatch.Models;

/// <summary>Immutable snapshot of the runtime state of a managed process.</summary>
public sealed record RuntimeState
{
    public Guid DefinitionId { get; init; }

    public ProcessStatus Status { get; init; } = ProcessStatus.Stopped;

    public int? ProcessId { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? ExitedAt { get; init; }

    public int? ExitCode { get; init; }

    public string? LastError { get; init; }

    public int RestartCount { get; init; }

    public static RuntimeState Initial(Guid definitionId) => new() { DefinitionId = definitionId };

    public TimeSpan? Uptime =>
        StartedAt is null ? null : (ExitedAt ?? DateTimeOffset.Now) - StartedAt.Value;
}
