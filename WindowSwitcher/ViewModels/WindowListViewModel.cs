using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using WindowSwitcherLib.Data.FileAccess;
using WindowSwitcherLib.Data.WindowAccess;
using WindowSwitcherLib.Models;
using WindowSwitcherLib.WindowAccess;

namespace WindowSwitcher.ViewModels;

public partial class WindowListViewModel : ObservableObject
{
    private readonly CancellationTokenSource _cts = new ();
    [ObservableProperty] 
    private ObservableCollection<ListBoxItem> _windowsListBoxItems = new();
    [ObservableProperty]
    private ObservableCollection<WindowConfig> _windowsConfigs = new();
    private WindowAccessor WindowAccessor { get; }
    public string LastSelectedItemId { get; } = "";
    public List<string> TempWindowIdsBlacklist { get; set; } = new ();
    
    public WindowListViewModel(WindowAccessor windowAccessor)
    {
        WindowAccessor = windowAccessor;
        Task.Run(async () => await RunPeriodicTask(_cts.Token));
    }

    private async Task RunPeriodicTask(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            FetchWindowsWithFilters();
            await Task.Delay(ConfigFileAccessor.GetInstance().Config.RefreshTimeoutMs, cancellationToken);
        }
    }
    
    public void FetchWindowsWithFilters()
    {
        ObservableCollection<WindowConfig> fetchedWindows = WindowAccessor.GetWindows();

        // Apply the prefixes and remove the blacklisted clients
        foreach (WindowConfig fetchedWindow in fetchedWindows)
        {
            bool isOnBlacklist = (ConfigFileAccessor.GetInstance().Config!.BlacklistPrefixes.Exists(x =>
                x.Equals(fetchedWindow.WindowTitle, StringComparison.CurrentCultureIgnoreCase)) || TempWindowIdsBlacklist.Contains(fetchedWindow.WindowId));
            bool isOnWhiteList = ConfigFileAccessor.GetInstance().Config!.WhitelistPrefixes.Any(prefix =>
                fetchedWindow.WindowTitle.ToLower().Contains(prefix.ToLower()));
            bool isOnWindowsList = WindowsConfigs.Any(x => x.WindowId == fetchedWindow.WindowId);

            if ((isOnBlacklist && isOnWindowsList) || (isOnWindowsList && !isOnWhiteList))
            {
                if(ConfigFileAccessor.GetInstance().Config.ActivateLogs)
                    AppLogger.Log($"[REMOVE] {fetchedWindow.ShortWindowTitle} ({fetchedWindow.WindowId}) || isOnBlacklist: {isOnBlacklist} isOnWhiteList: {isOnWhiteList} isOnWindowsList: {isOnWindowsList}", StaticData.LogSeverity.INFO);                
                WindowsConfigs.Remove(WindowsConfigs.First(x => x.WindowId == fetchedWindow.WindowId));
            }
            else if (!isOnBlacklist && !isOnWindowsList && isOnWhiteList)
            {
                if(ConfigFileAccessor.GetInstance().Config.ActivateLogs)
                    AppLogger.Log($"[ADD] {fetchedWindow.ShortWindowTitle} ({fetchedWindow.WindowId}) || isOnBlacklist: {isOnBlacklist} isOnWhiteList: {isOnWhiteList} isOnWindowsList: {isOnWindowsList}", StaticData.LogSeverity.INFO);                
                WindowsConfigs.Add(fetchedWindow);
            }
            else if (!isOnBlacklist && isOnWindowsList && isOnWhiteList)
            {
                WindowConfig windowConfig = WindowsConfigs.First(x => x.WindowId == fetchedWindow.WindowId);
                if (windowConfig.WindowTitle != fetchedWindow.WindowTitle)
                {
                    if(ConfigFileAccessor.GetInstance().Config.ActivateLogs)
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