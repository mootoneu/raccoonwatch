namespace Raccoonwatch.Infrastructure;

/// <summary>Central definition of all filesystem locations used by raccoonwatch.</summary>
public static class AppPaths
{
    public const string ApplicationName = "raccoonwatch";

    public static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ApplicationName);

    public static string SettingsFilePath => Path.Combine(SettingsDirectory, "settings.json");

    public static string LogDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ApplicationName, "logs");
}
