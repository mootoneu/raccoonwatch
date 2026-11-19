namespace Raccoonwatch.Models;

/// <summary>Shared bounds for the normal and fast status-polling intervals.</summary>
public static class PollingLimits
{
    /// <summary>Shortest interval allowed for either polling mode.</summary>
    public const int MinIntervalMs = 250;

    /// <summary>Longest interval allowed for either polling mode.</summary>
    public const int MaxIntervalMs = 60_000;

    /// <summary>Longest supported fast-polling window (one hour).</summary>
    public const int MaxWindowMs = 3_600_000;
}
