using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using WindowSwitcher.Lib.Models;
using WindowSwitcher.ViewModels.Abstractions;

namespace WindowSwitcher.ViewModels;

public partial class WindowListViewModel : ObservableObject, IDisposable
{
    private const int WindowListRefreshIntervalMs = 250;
    private readonly CancellationTokenSource _cts = new();
    private readonly IWindowSnapshotProvider _windowSnapshotProvider;
    private readonly IWindowFilterSettingsProvider _windowFilterSettingsProvider;
    private readonly IViewModelDispatcher _dispatcher;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    [ObservableProperty]
    private ObservableCollection<WindowConfig> _windowsConfigs = new();

    [ObservableProperty]
    private WindowConfig? _selectedWindow;

    public HashSet<string> TempWindowIdsBlacklist { get; } = new(StringComparer.Ordinal);

    public WindowListViewModel(
        IWindowSnapshotProvider windowSnapshotProvider,
        IWindowFilterSettingsProvider windowFilterSettingsProvider,
        IViewModelDispatcher dispatcher
    )
    {
        ArgumentNullException.ThrowIfNull(windowSnapshotProvider);
        ArgumentNullException.ThrowIfNull(windowFilterSettingsProvider);
        ArgumentNullException.ThrowIfNull(dispatcher);

        _windowSnapshotProvider = windowSnapshotProvider;
        _windowFilterSettingsProvider = windowFilterSettingsProvider;
        _dispatcher = dispatcher;
        _ = Task.Run(() => RunPeriodicTask(_cts.Token));
    }

    private async Task RunPeriodicTask(CancellationToken cancellationToken)
    {
        await RefreshWindowsAsync(cancellationToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(WindowListRefreshIntervalMs));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await RefreshWindowsAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown path.
        }
    }

    public void Dispose()
    {
        if (_cts.IsCancellationRequested)
            return;
        _cts.Cancel();
        _cts.Dispose();
    }

    public async Task FetchWindowsWithFiltersAsync(CancellationToken cancellationToken = default)
    {
        await FetchAndApplyWindowsAsync(cancellationToken).ConfigureAwait(false);
    }

    public bool TrySelectWindowById(string windowId)
    {
        if (string.IsNullOrWhiteSpace(windowId))
            return false;

        WindowConfig? matchingWindow = WindowsConfigs.FirstOrDefault(window =>
            string.Equals(window.WindowId, windowId, StringComparison.Ordinal)
        );
        if (matchingWindow is null)
            return false;

        SelectedWindow = matchingWindow;
        return true;
    }

    private void ApplyWindowsWithFilters(IReadOnlyCollection<WindowConfig> fetchedWindows)
    {
        WindowFilterSettings filterSettings = _windowFilterSettingsProvider.GetSettings();

        var fetchedIds = new HashSet<string>(StringComparer.Ordinal);
        var existingById = WindowsConfigs.ToDictionary(
            config => config.WindowId,
            StringComparer.Ordinal
        );

        // Apply whitelist/blacklist rules while iterating fetched windows.
        foreach (WindowConfig fetchedWindow in fetchedWindows)
        {
            fetchedIds.Add(fetchedWindow.WindowId);

            bool isOnBlacklist =
                filterSettings.BlacklistPrefixes.Contains(fetchedWindow.WindowTitle)
                || TempWindowIdsBlacklist.Contains(fetchedWindow.WindowId);
            bool isOnWhiteList = filterSettings.WhitelistPrefixes.Any(prefix =>
                fetchedWindow.WindowTitle.Contains(prefix, StringComparison.OrdinalIgnoreCase)
            );
            bool isOnWindowsList = existingById.TryGetValue(
                fetchedWindow.WindowId,
                out WindowConfig? existingConfig
            );

            if (isOnWindowsList && (isOnBlacklist || !isOnWhiteList))
            {
                WindowsConfigs.Remove(existingConfig!);
                existingById.Remove(fetchedWindow.WindowId);
            }
            else if (!isOnBlacklist && !isOnWindowsList && isOnWhiteList)
            {
                WindowsConfigs.Add(fetchedWindow);
                existingById[fetchedWindow.WindowId] = fetchedWindow;
            }
            else if (!isOnBlacklist && isOnWindowsList && isOnWhiteList)
            {
                if (existingConfig!.WindowTitle != fetchedWindow.WindowTitle)
                {
                    existingConfig.WindowTitle = fetchedWindow.WindowTitle;
                }
            }
        }

        // Remove stale windows that are no longer present in accessor output.
        for (int i = WindowsConfigs.Count - 1; i >= 0; i--)
        {
            WindowConfig window = WindowsConfigs[i];
            if (fetchedIds.Contains(window.WindowId))
                continue;

            WindowsConfigs.RemoveAt(i);
        }

        if (SelectedWindow is null)
            return;

        WindowConfig? selectedWindow = WindowsConfigs.FirstOrDefault(window =>
            string.Equals(window.WindowId, SelectedWindow.WindowId, StringComparison.Ordinal)
        );
        if (selectedWindow is not null)
        {
            SelectedWindow = selectedWindow;
            return;
        }

        SelectedWindow = null;
    }

    private async Task RefreshWindowsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await FetchAndApplyWindowsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown path.
        }
        catch (Exception) { }
    }

    private async Task FetchAndApplyWindowsAsync(CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IReadOnlyCollection<WindowConfig> fetchedWindows = await _windowSnapshotProvider
                .GetWindowsAsync(cancellationToken)
                .ConfigureAwait(false);
            await _dispatcher.InvokeAsync(() => ApplyWindowsWithFilters(fetchedWindows));
        }
        finally
        {
            _refreshGate.Release();
        }
    }
}
