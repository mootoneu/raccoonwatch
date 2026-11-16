using Microsoft.Extensions.Logging;
using Raccoonwatch.Infrastructure;

namespace Raccoonwatch.Services;

public sealed class AppController(
    ISettingsService settings,
    IProcessManager processes,
    IAutostartService autostart,
    ILogger<AppController> logger) : IAppController
{
    public async Task StartupAsync()
    {
        var currentPath = Environment.ProcessPath ?? string.Empty;
        settings.Settings.StartWithWindowsMirrored = autostart.IsEnabled();
        settings.Save();

        await processes.StartAutostartAsync(settings.Settings.Processes);
        logger.LogInformation(
            "Startup complete. Autostart with Windows: {Enabled}",
            settings.Settings.StartWithWindowsMirrored);
    }

    public async Task ExitAsync()
    {
        await processes.ShutdownAsync();
        settings.Save();
        logger.LogInformation("Shutdown complete.");
    }
}
