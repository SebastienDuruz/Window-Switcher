using Microsoft.Extensions.Caching.Memory;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Pipewire;

internal sealed class WaylandScreenCastMemoryCache : IDisposable
{
    private const string RestoreTokenPrefix = "wayland_restore_token:";
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, HashSet<string>> _aliasesByToken = new(
        StringComparer.Ordinal
    );

    internal static WaylandScreenCastMemoryCache Shared { get; } = new();

    internal string? TakeRestoreToken(IReadOnlyList<string> restoreKeys)
    {
        ArgumentNullException.ThrowIfNull(restoreKeys);

        lock (_syncRoot)
        {
            string? value = null;
            for (int index = 0; index < restoreKeys.Count; index++)
            {
                string cacheKey = BuildCacheKey(restoreKeys[index]);
                if (
                    value is null
                    && _cache.TryGetValue(cacheKey, out string? candidate)
                    && !string.IsNullOrWhiteSpace(candidate)
                )
                {
                    value = candidate.Trim();
                }

                RemoveCacheKey(cacheKey);
            }

            if (value is not null && _aliasesByToken.Remove(value, out HashSet<string>? aliases))
            {
                foreach (string alias in aliases)
                    _cache.Remove(alias);
            }
            return value;
        }
    }

    internal bool SetRestoreToken(IReadOnlyList<string> restoreKeys, string restoreToken)
    {
        ArgumentNullException.ThrowIfNull(restoreKeys);
        ArgumentException.ThrowIfNullOrWhiteSpace(restoreToken);

        string normalizedToken = restoreToken.Trim();
        bool changed = false;
        lock (_syncRoot)
        {
            for (int index = 0; index < restoreKeys.Count; index++)
            {
                string cacheKey = BuildCacheKey(restoreKeys[index]);
                if (
                    !_cache.TryGetValue(cacheKey, out string? existing)
                    || !string.Equals(existing, normalizedToken, StringComparison.Ordinal)
                )
                    changed = true;

                if (!string.IsNullOrWhiteSpace(existing))
                    RemoveAlias(existing, cacheKey);
                _cache.Set(cacheKey, normalizedToken);
                if (!_aliasesByToken.TryGetValue(normalizedToken, out HashSet<string>? aliases))
                {
                    aliases = new HashSet<string>(StringComparer.Ordinal);
                    _aliasesByToken[normalizedToken] = aliases;
                }
                aliases.Add(cacheKey);
            }
        }

        return changed;
    }

    internal bool ClearRestoreToken(IReadOnlyList<string> restoreKeys)
    {
        ArgumentNullException.ThrowIfNull(restoreKeys);

        bool changed = false;
        lock (_syncRoot)
        {
            var tokensToRemove = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < restoreKeys.Count; index++)
            {
                string cacheKey = BuildCacheKey(restoreKeys[index]);
                if (
                    _cache.TryGetValue(cacheKey, out string? token)
                    && !string.IsNullOrWhiteSpace(token)
                )
                {
                    changed = true;
                    tokensToRemove.Add(token);
                }
                else
                {
                    _cache.Remove(cacheKey);
                }
            }

            foreach (string token in tokensToRemove)
            {
                if (!_aliasesByToken.Remove(token, out HashSet<string>? aliases))
                    continue;

                foreach (string alias in aliases)
                    _cache.Remove(alias);
            }
        }

        return changed;
    }

    internal void Clear()
    {
        lock (_syncRoot)
        {
            _cache.Compact(1.0);
            _aliasesByToken.Clear();
        }
    }

    public void Dispose()
    {
        _cache.Dispose();
    }

    private static string BuildCacheKey(string restoreKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(restoreKey);
        return $"{RestoreTokenPrefix}{restoreKey.Trim()}";
    }

    private void RemoveCacheKey(string cacheKey)
    {
        if (_cache.TryGetValue(cacheKey, out string? token) && !string.IsNullOrWhiteSpace(token))
            RemoveAlias(token, cacheKey);
        _cache.Remove(cacheKey);
    }

    private void RemoveAlias(string token, string cacheKey)
    {
        if (!_aliasesByToken.TryGetValue(token, out HashSet<string>? aliases))
            return;
        aliases.Remove(cacheKey);
        if (aliases.Count == 0)
            _aliasesByToken.Remove(token);
    }
}
