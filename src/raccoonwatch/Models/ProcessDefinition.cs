namespace Raccoonwatch.Models;

/// <summary>Persisted user configuration for a single managed process.</summary>
public sealed record ProcessDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string DisplayName { get; init; } = string.Empty;

    public ProcessKind Kind { get; init; }

    public string CommandPath { get; init; } = string.Empty;

    public string Arguments { get; init; } = string.Empty;

    public string WorkingDirectory { get; init; } = string.Empty;

    /// <summary>Optional command used to update this process (exe, cmd/bat or ps1).</summary>
    public string UpdateCommandPath { get; init; } = string.Empty;

    /// <summary>Arguments passed to <see cref="UpdateCommandPath"/>.</summary>
    public string UpdateArguments { get; init; } = string.Empty;

    public int StartOrder { get; init; }

    public bool AutoStartOnLaunch { get; init; }

    public string Notes { get; init; } = string.Empty;
}
