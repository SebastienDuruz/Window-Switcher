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
            await FetchWindowsAsync();
            await Task.Delay(ConfigFileAccessor.GetInstance().Config.RefreshTimeoutMs, cancellationToken);
        }
    }

    public async Task FetchWindowsAsync()
    {
        FetchWindowsWithFilters();
    }
    
    private void FetchWindowsWithFilters()
    {
        Collection<WindowConfig?> fetchedWindows = WindowAccessor.GetWindows();
        
        // Apply the prefixes and remove the blacklisted clients
        foreach (WindowConfig? fetchedWindow in fetchedWindows)
        {
            foreach (string prefix in ConfigFileAccessor.GetInstance().Config!.WhitelistPrefixes)
            {
                // Add or update
                if (fetchedWindow!.WindowTitle.ToLower().StartsWith(prefix) && !ConfigFileAccessor.GetInstance().Config!.BlacklistPrefixes.Exists(x => x.StartsWith(fetchedWindow.WindowTitle.ToLower())))
                {
                    if (WindowsConfigs.Any(x => x.WindowId == fetchedWindow.WindowId))
                    {
                        WindowConfig windowConfig = WindowsConfigs.First(x => x.WindowId == fetchedWindow.WindowId);
                        if(windowConfig.WindowTitle != fetchedWindow.WindowTitle)
                            windowConfig.WindowTitle = fetchedWindow.WindowTitle;
                    }
                    else
                    {
                        WindowsConfigs.Add(fetchedWindow);
                    }
                }
                // Remove
                else
                {
                    List<WindowConfig> toRemove = WindowsConfigs.Where(existingWindow => !fetchedWindows.Any(x => x.WindowId == existingWindow.WindowId)).ToList();

                    foreach (WindowConfig window in toRemove)
                    {
                        WindowsConfigs.Remove(window);
                    }
                    if (WindowsConfigs.Any(x => x.WindowId == fetchedWindow.WindowId))
                        WindowsConfigs.Remove(WindowsConfigs.First(x => x.WindowId == fetchedWindow.WindowId));
                }
            }
        }
    }
}