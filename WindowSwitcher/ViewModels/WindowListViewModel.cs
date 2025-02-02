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

namespace WindowSwitcher.ViewModels;

public partial class WindowListViewModel : ObservableObject
{
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    public string LastSelectedItemId { get; set; } = "";
    private WindowAccessor WindowAccessor { get; set; }

    [ObservableProperty] 
    private ObservableCollection<ListBoxItem> windowsListBoxItems = new();
    
    [ObservableProperty]
    private ObservableCollection<WindowConfig> windowsConfigs = new();

    public WindowListViewModel(WindowAccessor windowAccessor)
    {
        WindowAccessor = windowAccessor;
        StartBackgroundTask();
    }

    private void StartBackgroundTask()
    {
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
            bool isOnBlacklist = ConfigFileAccessor.GetInstance().Config!.BlacklistPrefixes.Exists(x =>
                x.StartsWith(fetchedWindow.WindowTitle, StringComparison.CurrentCultureIgnoreCase));
            bool isOnWhiteList = ConfigFileAccessor.GetInstance().Config!.WhitelistPrefixes.Any(prefix =>
                fetchedWindow.WindowTitle.ToLower().StartsWith(prefix.ToLower()));
            bool isOnWindowsList = WindowsConfigs.Any(x => x.WindowId == fetchedWindow.WindowId);
                
            if((isOnBlacklist && isOnWindowsList) || (!isOnBlacklist && isOnWindowsList && !isOnWhiteList))
                WindowsConfigs.Remove(WindowsConfigs.First(x => x.WindowId == fetchedWindow.WindowId));
            else if(!isOnBlacklist && !isOnWindowsList && isOnWhiteList)
                WindowsConfigs.Add(fetchedWindow);
            else if(!isOnBlacklist && isOnWindowsList && isOnWhiteList)
                (WindowsConfigs.First(x => x.WindowId == fetchedWindow.WindowId)).WindowTitle = fetchedWindow.WindowTitle;
        }
        
        List<WindowConfig> toRemove = WindowsConfigs.Where(x => !fetchedWindows.Any(x => x.WindowId == x.WindowId)).ToList();
        foreach (WindowConfig window in toRemove)
            WindowsConfigs.Remove(window);
    }
}