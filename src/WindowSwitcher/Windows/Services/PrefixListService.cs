using System;
using System.Collections.Generic;
using System.Linq;
using WindowSwitcher.Lib.Data;

namespace WindowSwitcher.Windows.Services;

internal sealed class PrefixListService
{
    private readonly List<string> _prefixes;
    private readonly StaticData.PrefixWindowType _prefixWindowType;

    public PrefixListService(List<string> prefixes, StaticData.PrefixWindowType prefixWindowType)
    {
        ArgumentNullException.ThrowIfNull(prefixes);
        _prefixes = prefixes;
        _prefixWindowType = prefixWindowType;
    }

    public IReadOnlyCollection<string> GetPrefixesSnapshot()
    {
        return _prefixes.ToArray();
    }

    public bool ContainsPrefixStartingWith(string candidate)
    {
        string normalizedCandidate = Normalize(candidate);
        if (string.IsNullOrWhiteSpace(normalizedCandidate))
            return false;

        return _prefixes.Any(prefix =>
            prefix.StartsWith(normalizedCandidate, StringComparison.Ordinal)
        );
    }

    public bool TryAddPrefix(string? value, out string normalizedPrefix)
    {
        normalizedPrefix = Normalize(value);
        if (string.IsNullOrWhiteSpace(normalizedPrefix))
            return false;

        if (_prefixes.Contains(normalizedPrefix, StringComparer.Ordinal))
            return false;

        _prefixes.Add(normalizedPrefix);
        Persist();
        return true;
    }

    public bool TryRemovePrefix(string? value, out string normalizedPrefix)
    {
        normalizedPrefix = Normalize(value);
        if (string.IsNullOrWhiteSpace(normalizedPrefix))
            return false;

        bool removed = _prefixes.Remove(normalizedPrefix);
        if (!removed)
            return false;

        Persist();
        return true;
    }

    private static string Normalize(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant();
    }

    private void Persist()
    {
        switch (_prefixWindowType)
        {
            case StaticData.PrefixWindowType.whitelist:
                ConfigFileAccessor.GetInstance().SavePrefixesList(_prefixes);
                break;
            case StaticData.PrefixWindowType.blacklist:
                ConfigFileAccessor.GetInstance().SaveBlacklist(_prefixes);
                break;
        }
    }
}
