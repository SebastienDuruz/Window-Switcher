using System.Collections.Generic;

namespace WindowSwitcher.ViewModels.Abstractions;

/// <summary>
/// Provides read/write access to a persisted list of window title prefixes.
/// </summary>
public interface IPrefixListService
{
    /// <summary>
    /// Returns a snapshot of the currently persisted prefixes.
    /// </summary>
    IReadOnlyCollection<string> GetPrefixes();

    /// <summary>
    /// Returns whether any persisted prefix starts with the provided candidate.
    /// </summary>
    bool ContainsPrefixStartingWith(string candidate);

    /// <summary>
    /// Adds a normalized prefix when it is non-empty and not already present.
    /// </summary>
    bool TryAddPrefix(string? value, out string normalizedPrefix);

    /// <summary>
    /// Removes an existing prefix when present.
    /// </summary>
    bool TryRemovePrefix(string? value, out string normalizedPrefix);
}
