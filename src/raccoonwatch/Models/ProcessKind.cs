namespace Raccoonwatch.Models;

/// <summary>Determines how a process definition is launched.</summary>
public enum ProcessKind
{
    Executable,
    Cmd,
    PowerShell
}
