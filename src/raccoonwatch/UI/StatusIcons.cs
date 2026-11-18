using System.Drawing;
using Raccoonwatch.Models;

namespace Raccoonwatch.UI;

/// <summary>Provides the embedded 16×16 status icons used by the tray menu.</summary>
public static class StatusIcons
{
    private static readonly object Gate = new();
    private static readonly Dictionary<ProcessStatus, Image?> Cache = [];

    /// <summary>Returns the icon for <paramref name="status"/>, or null when none exists.</summary>
    public static Image? For(ProcessStatus status)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(status, out var icon))
            {
                return icon;
            }

            var resourceName = ResourceName(status);
            if (resourceName is not null)
            {
                using var stream = typeof(StatusIcons).Assembly.GetManifestResourceStream(resourceName);
                if (stream is not null)
                {
                    // Copy off the stream so the cached bitmap remains valid after it closes.
                    using var loaded = Image.FromStream(stream);
                    icon = new Bitmap(loaded);
                }
            }

            Cache[status] = icon;
            return icon;
        }
    }

    private static string? ResourceName(ProcessStatus status) => status switch
    {
        ProcessStatus.Running => "Raccoonwatch.assets.status-running.png",
        // The supplied Stopped icon is named "status-stop.png" (there is no "status-stopped.png").
        ProcessStatus.Stopped => "Raccoonwatch.assets.status-stop.png",
        ProcessStatus.Starting => "Raccoonwatch.assets.status-starting.png",
        ProcessStatus.Stopping => "Raccoonwatch.assets.status-stopping.png",
        ProcessStatus.Exited => "Raccoonwatch.assets.status-exited.png",
        ProcessStatus.Failed => "Raccoonwatch.assets.status-failed.png",
        // No icon was supplied for Unknown, so that state stays text-only.
        _ => null
    };
}
