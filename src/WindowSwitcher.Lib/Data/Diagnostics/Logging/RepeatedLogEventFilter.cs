using Serilog.Core;
using Serilog.Events;

namespace WindowSwitcher.Lib.Data.Diagnostics.Logging;

/// <summary>
/// Drops log events that repeat the same level, rendered message and exception type
/// within a short window, so hot paths (frame capture, periodic refreshes, key events)
/// cannot flood the log file.
/// </summary>
public sealed class RepeatedLogEventFilter : ILogEventFilter
{
    /// <summary>
    /// Default duration during which an identical event is reported only once.
    /// </summary>
    public static readonly TimeSpan DefaultRepetitionWindow = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Default maximum number of distinct event signatures tracked at the same time.
    /// </summary>
    public const int DefaultMaximumTrackedSignatures = 64;

    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _repetitionWindow;
    private readonly int _maximumTrackedSignatures;
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, DateTimeOffset> _lastReports = new(StringComparer.Ordinal);

    /// <summary>
    /// Creates a filter using the system clock and the default limits.
    /// </summary>
    public RepeatedLogEventFilter()
        : this(TimeProvider.System, DefaultRepetitionWindow, DefaultMaximumTrackedSignatures) { }

    /// <summary>
    /// Creates a filter with an explicit clock and limits.
    /// </summary>
    /// <param name="timeProvider">Clock used to measure the repetition window.</param>
    /// <param name="repetitionWindow">Duration during which an identical event is dropped.</param>
    /// <param name="maximumTrackedSignatures">Maximum number of signatures kept in memory.</param>
    public RepeatedLogEventFilter(
        TimeProvider timeProvider,
        TimeSpan repetitionWindow,
        int maximumTrackedSignatures
    )
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(repetitionWindow, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumTrackedSignatures, 1);

        _timeProvider = timeProvider;
        _repetitionWindow = repetitionWindow;
        _maximumTrackedSignatures = maximumTrackedSignatures;
    }

    /// <inheritdoc />
    public bool IsEnabled(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        string key =
            $"{logEvent.Level}:{logEvent.RenderMessage()}:{logEvent.Exception?.GetType().FullName}";
        return ShouldReport(key);
    }

    private bool ShouldReport(string key)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        lock (_syncRoot)
        {
            if (
                _lastReports.TryGetValue(key, out DateTimeOffset previous)
                && now - previous < _repetitionWindow
            )
                return false;

            foreach (
                string expired in _lastReports
                    .Where(pair => now - pair.Value >= _repetitionWindow)
                    .Select(pair => pair.Key)
                    .ToArray()
            )
                _lastReports.Remove(expired);

            if (!_lastReports.ContainsKey(key) && _lastReports.Count >= _maximumTrackedSignatures)
            {
                string oldest = _lastReports.MinBy(pair => pair.Value).Key;
                _lastReports.Remove(oldest);
            }

            _lastReports[key] = now;
            return true;
        }
    }
}
