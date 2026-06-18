using System;
using System.Collections.Generic;

namespace WindowSwitcher.Windows.Services;

internal sealed class WindowBlacklistCoordinator
{
    private readonly FiltersWindow _filtersWindow;
    private readonly ISet<string> _temporaryWindowIds;

    public WindowBlacklistCoordinator(FiltersWindow filtersWindow, ISet<string> temporaryWindowIds)
    {
        ArgumentNullException.ThrowIfNull(filtersWindow);
        ArgumentNullException.ThrowIfNull(temporaryWindowIds);

        _filtersWindow = filtersWindow;
        _temporaryWindowIds = temporaryWindowIds;
    }

    public void AddToBlacklist(string windowTitle)
    {
        if (string.IsNullOrWhiteSpace(windowTitle))
            return;

        string normalizedTitle = windowTitle.ToLowerInvariant();
        if (_filtersWindow.HasBlacklistPrefixStartingWith(normalizedTitle))
            return;

        _ = _filtersWindow.TryAddBlacklistPrefix(normalizedTitle);
    }

    public void AddToTemporaryBlacklist(string windowId)
    {
        if (string.IsNullOrWhiteSpace(windowId))
            return;

        _temporaryWindowIds.Add(windowId);
    }
}
