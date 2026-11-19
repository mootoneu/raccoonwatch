using Raccoonwatch.Models;

namespace Raccoonwatch.Services;

/// <summary>
/// Decides how often the process monitor should poll. After a user action on a process it
/// temporarily switches to a shorter interval so status updates appear quickly, then returns
/// to the normal interval once the configured window has elapsed.
/// </summary>
public interface IPollingIntervalProvider
{
    /// <summary>Current polling interval in milliseconds.</summary>
    int GetIntervalMs();

    /// <summary>Starts (or extends) the fast-polling window.</summary>
    void NotifyActivity();
}

/// <inheritdoc cref="IPollingIntervalProvider"/>
public sealed class PollingIntervalProvider : IPollingIntervalProvider
{
    private readonly ISettingsService _settings;
    private readonly Func<long> _ticks;
    private long _fastUntil;

    /// <param name="settings">Settings holding the normal/fast durations.</param>
    /// <param name="ticks">
    /// Optional monotonic clock in milliseconds. Injectable so the window can be tested
    /// deterministically; defaults to <see cref="Environment.TickCount64"/>.
    /// </param>
    public PollingIntervalProvider(ISettingsService settings, Func<long>? ticks = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _ticks = ticks ?? (() => Environment.TickCount64);
    }

    public void NotifyActivity()
    {
        var window = Math.Clamp(_settings.Settings.FastPollWindowMs, 0, PollingLimits.MaxWindowMs);
        _fastUntil = _ticks() + window;
    }

    public int GetIntervalMs()
    {
        var normal = Math.Clamp(
            _settings.Settings.PollIntervalMs, PollingLimits.MinIntervalMs, PollingLimits.MaxIntervalMs);

        if (_ticks() >= _fastUntil)
        {
            return normal;
        }

        var fast = Math.Clamp(
            _settings.Settings.FastPollIntervalMs, PollingLimits.MinIntervalMs, PollingLimits.MaxIntervalMs);

        // Never make an "accelerated" poll slower than the configured normal interval.
        return Math.Min(fast, normal);
    }
}
