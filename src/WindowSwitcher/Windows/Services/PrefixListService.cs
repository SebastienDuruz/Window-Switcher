using System;
using System.Collections.Generic;
using System.Linq;
using WindowSwitcher.Lib.Data;
using WindowSwitcher.ViewModels.Abstractions;

namespace WindowSwitcher.Windows.Services;

internal sealed class PrefixListService : IPrefixListService
{
    private readonly List<string> _prefixes;
    private readonly Action<List<string>> _persist;

    public PrefixListService(List<string> prefixes, StaticData.PrefixWindowType prefixWindowType)
        : this(prefixes, CreatePersister(prefixWindowType)) { }

    internal PrefixListService(List<string> prefixes, Action<List<string>> persist)
    {
        ArgumentNullException.ThrowIfNull(prefixes);
        ArgumentNullException.ThrowIfNull(persist);
        _prefixes = prefixes;
        _persist = persist;
    }

    public IReadOnlyCollection<string> GetPrefixes()
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

    private void Persist() => _persist(_prefixes);

    private static Action<List<string>> CreatePersister(
        StaticData.PrefixWindowType prefixWindowType
    ) =>
        prefixWindowType switch
        {
            StaticData.PrefixWindowType.whitelist => prefixes =>
                ConfigFileAccessor.GetInstance().SavePrefixesList(prefixes),
            StaticData.PrefixWindowType.blacklist => prefixes =>
                ConfigFileAccessor.GetInstance().SaveBlacklist(prefixes),
            _ => _ => { },
        };
}
