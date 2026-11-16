namespace Raccoonwatch.Services;

/// <summary>High-level application lifecycle orchestration used by the tray UI.</summary>
public interface IAppController
{
    Task StartupAsync();

    Task ExitAsync();
}
