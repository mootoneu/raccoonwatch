namespace Raccoonwatch.Models;

/// <summary>Root persisted settings document.</summary>
public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;

    public List<ProcessDefinition> Processes { get; set; } = [];

    public int PollIntervalMs { get; set; } = 2000;

    public int OutputHistoryLines { get; set; } = 5000;

    public bool ShowNotifications { get; set; } = true;

    public string LogLevel { get; set; } = "Information";

    public bool StopProcessesOnExit { get; set; }

    public bool StartWithWindowsMirrored { get; set; }
}
