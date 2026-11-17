using System.Drawing;

namespace Raccoonwatch.Infrastructure;

/// <summary>
/// Loads the raccoon icon from the embedded resource (works in single-file publishes)
/// with a file-system fallback for local development.
/// </summary>
public static class AppIcon
{
    private const string ResourceName = "Raccoonwatch.assets.raccoonwatch.ico";

    public static Icon Create()
    {
        try
        {
            using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream(ResourceName);
            if (stream is not null)
            {
                return new Icon(stream);
            }
        }
        catch
        {
            // Fall through to the file-based loading below.
        }

        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "raccoonwatch.ico");
            if (File.Exists(path))
            {
                return new Icon(path);
            }
        }
        catch
        {
            // Fall through to the system icon.
        }

        return SystemIcons.Application;
    }
}
