using System.Text;
using Microsoft.Extensions.Logging;

namespace Raccoonwatch.Infrastructure;

/// <summary>Minimal rolling file logger provider; no external provider package needed.</summary>
public sealed class FileLoggerProvider(string logDirectory, LoggingOptions options) : ILoggerProvider
{
    private readonly object _gate = new();

    internal LogLevel MinLevel => options.MinLevel;

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    internal void Write(LogLevel level, string category, string message, Exception? exception)
    {
        if (level < options.MinLevel)
        {
            return;
        }

        var line = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Append(" [")
            .Append(level.ToString().ToUpperInvariant())
            .Append("] ")
            .Append(category)
            .Append(": ")
            .Append(message)
            .AppendLine();

        if (exception is not null)
        {
            line.Append(exception).AppendLine();
        }

        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(logDirectory);
                var path = Path.Combine(logDirectory, $"raccoonwatch-{DateTimeOffset.Now:yyyyMMdd}.log");
                File.AppendAllText(path, line.ToString(), Encoding.UTF8);
            }
            catch
            {
                // Logging must never break the application.
            }
        }
    }

    public void Dispose()
    {
        // Nothing to dispose: each write opens and closes its own file handle.
    }
}

internal sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
{
    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= provider.MinLevel;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        provider.Write(logLevel, category, formatter(state, exception), exception);
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        private NullScope()
        {
        }

        public void Dispose()
        {
        }
    }
}
