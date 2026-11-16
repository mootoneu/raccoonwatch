using Microsoft.Extensions.Logging;

namespace Raccoonwatch.Infrastructure;

/// <summary>
/// Mutable logging settings shared between the settings service and the file logger.
/// Registered as a singleton so changes made in Settings take effect immediately.
/// </summary>
public sealed class LoggingOptions
{
    public LogLevel MinLevel { get; set; } = LogLevel.Information;
}
