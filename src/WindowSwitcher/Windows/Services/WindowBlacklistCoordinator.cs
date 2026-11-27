using System;
using System.Collections.Generic;
using WindowSwitcher.ViewModels;

namespace WindowSwitcher.Windows.Services;

internal sealed class WindowBlacklistCoordinator
{
    private readonly PrefixListViewModel _blacklist;
    private readonly ISet<string> _temporaryWindowIds;

    public WindowBlacklistCoordinator(
        PrefixListViewModel blacklist,
        ISet<string> temporaryWindowIds
    )
    {
        ArgumentNullException.ThrowIfNull(blacklist);
        ArgumentNullException.ThrowIfNull(temporaryWindowIds);

        _blacklist = blacklist;
        _temporaryWindowIds = temporaryWindowIds;
    }

    public void AddToBlacklist(string windowTitle)
    {
        if (string.IsNullOrWhiteSpace(windowTitle))
            return;

        string normalizedTitle = windowTitle.ToLowerInvariant();
        if (_blacklist.ContainsPrefixStartingWith(normalizedTitle))
            return;

        _ = _blacklist.TryAddPrefix(normalizedTitle);
    }

    public void AddToTemporaryBlacklist(string windowId)
    {
        if (string.IsNullOrWhiteSpace(windowId))
            return;

        _temporaryWindowIds.Add(windowId);
    }
}
