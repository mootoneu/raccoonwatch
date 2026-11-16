namespace Raccoonwatch.Services;

/// <summary>Reads and writes the per-user Windows "Run" autostart entry.</summary>
public interface IAutostartService
{
    bool IsEnabled();

    string? GetConfiguredPath();

    void SetEnabled(bool enabled, string executablePath);

    bool IsPathCurrent(string executablePath);
}
