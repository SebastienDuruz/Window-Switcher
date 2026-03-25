using Microsoft.Extensions.Caching.Memory;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Pipewire;

internal sealed class WaylandScreenCastMemoryCache : IDisposable
{
    private const string RestoreTokenPrefix = "wayland_restore_token:";
    private const string RestoreDataPrefix = "wayland_restore_data:";
    private const string StreamIdPrefix = "wayland_stream_id:";

    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly MemoryCacheEntryOptions _entryOptions = new();

    internal static WaylandScreenCastMemoryCache Shared { get; } = new();

    internal string? GetRestoreToken(IReadOnlyList<string> restoreKeys)
    {
        return GetFirstValue(RestoreTokenPrefix, restoreKeys);
    }

    internal IReadOnlyList<string> GetRestoreDataCandidates(IReadOnlyList<string> restoreKeys)
    {
        ArgumentNullException.ThrowIfNull(restoreKeys);

        var values = new List<string>(restoreKeys.Count);
        for (int index = 0; index < restoreKeys.Count; index++)
        {
            string? value = GetValue(RestoreDataPrefix, restoreKeys[index]);
            if (value is not null)
                values.Add(value);
        }

        return values;
    }

    internal string? GetStreamId(IReadOnlyList<string> restoreKeys)
    {
        return GetFirstValue(StreamIdPrefix, restoreKeys);
    }

    internal bool SetRestoreToken(IReadOnlyList<string> restoreKeys, string restoreToken)
    {
        return SetValues(RestoreTokenPrefix, restoreKeys, restoreToken);
    }

    internal bool SetRestoreData(IReadOnlyList<string> restoreKeys, string restoreData)
    {
        return SetValues(RestoreDataPrefix, restoreKeys, restoreData);
    }

    internal bool SetStreamId(IReadOnlyList<string> restoreKeys, string streamId)
    {
        return SetValues(StreamIdPrefix, restoreKeys, streamId);
    }

    internal bool ClearArtifacts(IReadOnlyList<string> restoreKeys)
    {
        ArgumentNullException.ThrowIfNull(restoreKeys);

        bool changed = false;
        for (int index = 0; index < restoreKeys.Count; index++)
        {
            string restoreKey = restoreKeys[index];
            changed |= RemoveValue(RestoreTokenPrefix, restoreKey);
            changed |= RemoveValue(RestoreDataPrefix, restoreKey);
            changed |= RemoveValue(StreamIdPrefix, restoreKey);
        }

        return changed;
    }

    internal bool ClearStreamId(IReadOnlyList<string> restoreKeys)
    {
        return RemoveValues(StreamIdPrefix, restoreKeys);
    }

    internal void Clear()
    {
        _cache.Compact(1.0);
    }

    public void Dispose()
    {
        _cache.Dispose();
    }

    private string? GetFirstValue(string prefix, IReadOnlyList<string> restoreKeys)
    {
        ArgumentNullException.ThrowIfNull(restoreKeys);

        for (int index = 0; index < restoreKeys.Count; index++)
        {
            string? value = GetValue(prefix, restoreKeys[index]);
            if (value is not null)
                return value;
        }

        return null;
    }

    private bool SetValues(string prefix, IReadOnlyList<string> restoreKeys, string value)
    {
        ArgumentNullException.ThrowIfNull(restoreKeys);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        string normalizedValue = value.Trim();
        bool changed = false;

        for (int index = 0; index < restoreKeys.Count; index++)
        {
            string cacheKey = BuildCacheKey(prefix, restoreKeys[index]);
            if (
                !_cache.TryGetValue(cacheKey, out string? existing)
                || !string.Equals(existing, normalizedValue, StringComparison.Ordinal)
            )
            {
                changed = true;
            }

            _cache.Set(cacheKey, normalizedValue, _entryOptions);
        }

        return changed;
    }

    private bool RemoveValues(string prefix, IReadOnlyList<string> restoreKeys)
    {
        ArgumentNullException.ThrowIfNull(restoreKeys);

        bool changed = false;
        for (int index = 0; index < restoreKeys.Count; index++)
            changed |= RemoveValue(prefix, restoreKeys[index]);

        return changed;
    }

    private string? GetValue(string prefix, string restoreKey)
    {
        string cacheKey = BuildCacheKey(prefix, restoreKey);
        if (!_cache.TryGetValue(cacheKey, out string? value) || string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim();
    }

    private bool RemoveValue(string prefix, string restoreKey)
    {
        string cacheKey = BuildCacheKey(prefix, restoreKey);
        bool existed = _cache.TryGetValue(cacheKey, out _);
        if (existed)
            _cache.Remove(cacheKey);

        return existed;
    }

    private static string BuildCacheKey(string prefix, string restoreKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(restoreKey);
        return $"{prefix}{restoreKey.Trim()}";
    }
}
