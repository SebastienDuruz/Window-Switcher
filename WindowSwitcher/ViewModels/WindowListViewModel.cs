using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using WindowSwitcherLib.Data;
using WindowSwitcherLib.Data.FileAccess;
using WindowSwitcherLib.Data.WindowAccess;
using WindowSwitcherLib.Data.WindowAccess.Accessors;
using WindowSwitcherLib.Models;

namespace WindowSwitcher.ViewModels;

public partial class WindowListViewModel : ObservableObject, IDisposable
{
    private const int WindowListRefreshIntervalMs = 250;
    private readonly CancellationTokenSource _cts = new();
    [ObservableProperty] 
    private ObservableCollection<ListBoxItem> _windowsListBoxItems = new();
    [ObservableProperty]
    private ObservableCollection<WindowConfig> _windowsConfigs = new();
    private WinAccessorBase WinAccessorBase { get; }
    public string LastSelectedItemId { get; } = string.Empty;
    public HashSet<string> TempWindowIdsBlacklist { get; } = new(StringComparer.Ordinal);
    
    public WindowListViewModel(WinAccessorBase winAccessorBase)
    {
        WinAccessorBase = winAccessorBase;
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
    
    public void FetchWindowsWithFilters()
    {
        ApplyWindowsWithFilters(WinAccessorBase.GetWindows());
    }

    private void ApplyWindowsWithFilters(IReadOnlyCollection<WindowConfig> fetchedWindows)
    {
        var configAccessor = ConfigFileAccessor.GetInstance();
        var configSnapshot = configAccessor.ReadConfig(config => new
        {
            config.ActivateLogs,
            BlacklistPrefixes = config.BlacklistPrefixes.ToHashSet(StringComparer.OrdinalIgnoreCase),
            WhitelistPrefixes = config.WhitelistPrefixes
                .Where(prefix => !string.IsNullOrWhiteSpace(prefix))
                .ToArray()
        });

        var fetchedIds = new HashSet<string>(StringComparer.Ordinal);
        var existingById = WindowsConfigs.ToDictionary(config => config.WindowId, StringComparer.Ordinal);

        // Apply whitelist/blacklist rules while iterating fetched windows.
        foreach (WindowConfig fetchedWindow in fetchedWindows)
        {
            fetchedIds.Add(fetchedWindow.WindowId);

            bool isOnBlacklist = configSnapshot.BlacklistPrefixes.Contains(fetchedWindow.WindowTitle) ||
                                 TempWindowIdsBlacklist.Contains(fetchedWindow.WindowId);
            bool isOnWhiteList = configSnapshot.WhitelistPrefixes.Any(prefix =>
                fetchedWindow.WindowTitle.Contains(prefix, StringComparison.OrdinalIgnoreCase));
            bool isOnWindowsList = existingById.TryGetValue(fetchedWindow.WindowId, out WindowConfig? existingConfig);

            if (isOnWindowsList && (isOnBlacklist || !isOnWhiteList))
            {
                LogWindowChange(configSnapshot.ActivateLogs, "REMOVE", fetchedWindow, isOnBlacklist, isOnWhiteList, isOnWindowsList);
                WindowsConfigs.Remove(existingConfig!);
                existingById.Remove(fetchedWindow.WindowId);
            }
            else if (!isOnBlacklist && !isOnWindowsList && isOnWhiteList)
            {
                LogWindowChange(configSnapshot.ActivateLogs, "ADD", fetchedWindow, isOnBlacklist, isOnWhiteList, isOnWindowsList);
                WindowsConfigs.Add(fetchedWindow);
                existingById[fetchedWindow.WindowId] = fetchedWindow;
            }
            else if (!isOnBlacklist && isOnWindowsList && isOnWhiteList)
            {
                if (existingConfig!.WindowTitle != fetchedWindow.WindowTitle)
                {
                    LogWindowChange(configSnapshot.ActivateLogs, "UPDATE", fetchedWindow, isOnBlacklist, isOnWhiteList, isOnWindowsList);
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

            LogWindowChange(configSnapshot.ActivateLogs, "REMOVE", window, false, false, true);
            WindowsConfigs.RemoveAt(i);
        }
    }

    private async Task RefreshWindowsAsync(CancellationToken cancellationToken)
    {
        try
        {
            ObservableCollection<WindowConfig> fetchedWindows = WinAccessorBase.GetWindows();
            await Dispatcher.UIThread.InvokeAsync(() => ApplyWindowsWithFilters(fetchedWindows), DispatcherPriority.Background);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown path.
        }
        catch (Exception ex)
        {
            if (ConfigFileAccessor.GetInstance().ReadConfig(config => config.ActivateLogs))
                AppLogger.Log(ex.Message, StaticData.LogSeverity.ERRO);
        }
    }

    private static void LogWindowChange(
        bool activateLogs,
        string action,
        WindowConfig windowConfig,
        bool isOnBlacklist,
        bool isOnWhiteList,
        bool isOnWindowsList)
    {
        if (!activateLogs)
            return;

        AppLogger.Log(
            $"[{action}] {windowConfig.ShortWindowTitle} ({windowConfig.WindowId}) || isOnBlacklist: {isOnBlacklist} isOnWhiteList: {isOnWhiteList} isOnWindowsList: {isOnWindowsList}",
            StaticData.LogSeverity.INFO);
    }
}
