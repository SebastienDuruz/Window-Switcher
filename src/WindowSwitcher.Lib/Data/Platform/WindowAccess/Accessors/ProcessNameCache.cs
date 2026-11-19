namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors;

/// <summary>
/// Caches process names by PID for a short time so periodic window enumerations do not read
/// the same process again on every refresh. Failed reads (empty names) are cached too.
/// </summary>
internal sealed class ProcessNameCache
{
    /// <summary>
    /// Default time during which a cached name is reused.
    /// </summary>
    public static readonly TimeSpan DefaultTimeToLive = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Default number of entries above which the whole cache is cleared.
    /// </summary>
    public const int DefaultMaximumEntries = 512;

    private readonly Func<uint, string> _read;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _timeToLive;
    private readonly int _maximumEntries;
    private readonly Dictionary<uint, Entry> _entries = [];
    private readonly Lock _sync = new();

    /// <summary>
    /// Creates a cache around a process name reader.
    /// </summary>
    /// <param name="read">Reads the name of a PID; returns an empty string on failure.</param>
    /// <param name="timeProvider">Clock used to expire entries.</param>
    /// <param name="timeToLive">Time during which a cached name is reused.</param>
    /// <param name="maximumEntries">Number of entries above which the cache is cleared.</param>
    public ProcessNameCache(
        Func<uint, string> read,
        TimeProvider timeProvider,
        TimeSpan? timeToLive = null,
        int maximumEntries = DefaultMaximumEntries
    )
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntries);

        _read = read;
        _timeProvider = timeProvider;
        _timeToLive = timeToLive ?? DefaultTimeToLive;
        _maximumEntries = maximumEntries;
    }

    /// <summary>
    /// Returns the cached name of a process, or reads it when missing or expired.
    /// </summary>
    public string GetName(uint processId)
    {
        long now = _timeProvider.GetTimestamp();
        lock (_sync)
        {
            if (
                _entries.TryGetValue(processId, out Entry cached)
                && _timeProvider.GetElapsedTime(cached.ReadAt, now) < _timeToLive
            )
            {
                return cached.Name;
            }
        }

        // Read outside the lock: a slow OS call must not block concurrent enumerations.
        // Two concurrent misses for the same PID may both read, which is harmless.
        string name = _read(processId);

        lock (_sync)
        {
            if (_entries.Count >= _maximumEntries && !_entries.ContainsKey(processId))
                _entries.Clear();

            _entries[processId] = new Entry(name, now);
        }

        return name;
    }

    private readonly record struct Entry(string Name, long ReadAt);
}
