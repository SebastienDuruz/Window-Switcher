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
using WindowSwitcherLib.Models;

namespace WindowSwitcher.ViewModels;

public partial class WindowListViewModel : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource _cts = new ();
    [ObservableProperty] 
    private ObservableCollection<ListBoxItem> _windowsListBoxItems = new();
    [ObservableProperty]
    private ObservableCollection<WindowConfig> _windowsConfigs = new();
    private WinAccessor WinAccessor { get; }
    public string LastSelectedItemId { get; } = "";
    public List<string> TempWindowIdsBlacklist { get; set; } = new ();
    
    public WindowListViewModel(WinAccessor winAccessor)
    {
        WinAccessor = winAccessor;
        Task.Run(async () => await RunPeriodicTask(_cts.Token));
    }

    private async Task RunPeriodicTask(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                ObservableCollection<WindowConfig> fetchedWindows = WinAccessor.GetWindows();
                await Dispatcher.UIThread.InvokeAsync(() => ApplyWindowsWithFilters(fetchedWindows));
            }
            catch (Exception ex)
            {
                if (ConfigFileAccessor.GetInstance().ReadConfig(config => config.ActivateLogs))
                    AppLogger.Log(ex.Message, StaticData.LogSeverity.ERRO);
            }
            int refreshTimeoutMs = ConfigFileAccessor.GetInstance().ReadConfig(config =>
            {
                return Math.Clamp(config.LinuxPipeWireRefreshTimeoutMs, 30, 5_000);
            });
            await Task.Delay(refreshTimeoutMs, cancellationToken);
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
        ApplyWindowsWithFilters(WinAccessor.GetWindows());
    }

    private void ApplyWindowsWithFilters(IReadOnlyCollection<WindowConfig> fetchedWindows)
    {
        var configAccessor = ConfigFileAccessor.GetInstance();
        var configSnapshot = configAccessor.ReadConfig(config => new
        {
            config.ActivateLogs,
            BlacklistPrefixes = config.BlacklistPrefixes.ToList(),
            WhitelistPrefixes = config.WhitelistPrefixes.ToList()
        });

        // Apply the prefixes and remove the blacklisted clients
        foreach (WindowConfig fetchedWindow in fetchedWindows)
        {
            bool isOnBlacklist = (configSnapshot.BlacklistPrefixes.Exists(x =>
                x.Equals(fetchedWindow.WindowTitle, StringComparison.OrdinalIgnoreCase)) ||
                TempWindowIdsBlacklist.Contains(fetchedWindow.WindowId));
            bool isOnWhiteList = configSnapshot.WhitelistPrefixes.Any(prefix =>
                fetchedWindow.WindowTitle.Contains(prefix, StringComparison.OrdinalIgnoreCase));
            bool isOnWindowsList = WindowsConfigs.Any(x => x.WindowId == fetchedWindow.WindowId);

            if ((isOnBlacklist && isOnWindowsList) || (isOnWindowsList && !isOnWhiteList))
            {
                if(configSnapshot.ActivateLogs)
                    AppLogger.Log($"[REMOVE] {fetchedWindow.ShortWindowTitle} ({fetchedWindow.WindowId}) || isOnBlacklist: {isOnBlacklist} isOnWhiteList: {isOnWhiteList} isOnWindowsList: {isOnWindowsList}", StaticData.LogSeverity.INFO);                
                WindowsConfigs.Remove(WindowsConfigs.First(x => x.WindowId == fetchedWindow.WindowId));
            }
            else if (!isOnBlacklist && !isOnWindowsList && isOnWhiteList)
            {
                if(configSnapshot.ActivateLogs)
                    AppLogger.Log($"[ADD] {fetchedWindow.ShortWindowTitle} ({fetchedWindow.WindowId}) || isOnBlacklist: {isOnBlacklist} isOnWhiteList: {isOnWhiteList} isOnWindowsList: {isOnWindowsList}", StaticData.LogSeverity.INFO);                
                WindowsConfigs.Add(fetchedWindow);
            }
            else if (!isOnBlacklist && isOnWindowsList && isOnWhiteList)
            {
                WindowConfig windowConfig = WindowsConfigs.First(x => x.WindowId == fetchedWindow.WindowId);
                if (windowConfig.WindowTitle != fetchedWindow.WindowTitle)
                {
                    if(configSnapshot.ActivateLogs)
                        AppLogger.Log($"[UPDATE] {fetchedWindow.ShortWindowTitle} ({fetchedWindow.WindowId}) || isOnBlacklist: {isOnBlacklist} isOnWhiteList: {isOnWhiteList} isOnWindowsList: {isOnWindowsList}", StaticData.LogSeverity.INFO);                
                    windowConfig.WindowTitle = fetchedWindow.WindowTitle;
                }
            }
        }
        
        List<WindowConfig> toRemove = WindowsConfigs.Where(x => fetchedWindows.All(y => y.WindowId != x.WindowId)).ToList();
        foreach (WindowConfig window in toRemove)
            WindowsConfigs.Remove(window);
    }
}
