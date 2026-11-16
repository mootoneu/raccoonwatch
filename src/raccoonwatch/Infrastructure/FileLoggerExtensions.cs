using Microsoft.Extensions.Logging;

namespace Raccoonwatch.Infrastructure;

public static class FileLoggerExtensions
{
    public static ILoggingBuilder AddRaccoonwatchFileLogger(
        this ILoggingBuilder builder,
        string directory,
        LoggingOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddProvider(new FileLoggerProvider(directory, options));
        return builder;
    }
}
