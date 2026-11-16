namespace Raccoonwatch.Models;

/// <summary>Lifecycle state of a process tracked by raccoonwatch.</summary>
public enum ProcessStatus
{
    Unknown,
    Stopped,
    Starting,
    Running,
    Stopping,
    Exited,
    Failed
}
