namespace Raccoonwatch.Cli;

/// <summary>Formats an uptime in seconds into a compact human-readable string.</summary>
public static class UptimeFormatter
{
    public static string Format(double seconds) =>
        TimeSpan.FromSeconds(seconds) switch
        {
            var t when t.TotalDays >= 1 => $"{(int)t.TotalDays}d {t.Hours}h",
            var t when t.TotalHours >= 1 => $"{(int)t.TotalHours}h {t.Minutes}m",
            var t when t.TotalMinutes >= 1 => $"{(int)t.TotalMinutes}m {t.Seconds}s",
            var t => $"{(int)t.TotalSeconds}s"
        };
}
