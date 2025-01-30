using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using WindowSwitcherLib.Data.FileAccess;
using WindowSwitcherLib.Data.WindowAccess;
using WindowSwitcherLib.Models;
using WindowSwitcherLib.WindowAccess;
using WindowSwitcherLib.WindowAccess.CustomWindows.Commands;

namespace WindowSwitcher.ViewModels;

public partial class WindowListViewModel : ObservableObject
{
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    public string LastSelectedItemId { get; set; } = "";
    private WindowAccessor WindowAccessor { get; set; } = WindowFactories.GetAccessor();

    [ObservableProperty] 
    private ObservableCollection<ListBoxItem> windowsListBoxItems = new();
    
    [ObservableProperty]
    private ObservableCollection<WindowConfig> windowsConfigs = new();

    public WindowListViewModel()
    {
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

        // Show the managed windows on the ui
        // foreach (WindowConfig window in WindowsConfigs)
        // {
        //     if (windowsListBoxItems.All(x => x.Name != window.WindowId))
        //     {
        //         await Dispatcher.UIThread.InvokeAsync(() =>
        //         {
        //             // ListBox Configuration panel
        //             windowsListBoxItems.Add(new ListBoxItem()
        //             {
        //                 Name = window.WindowId,
        //                 Content = window.ShortWindowTitle,
        //                 Height = 22,
        //                 FontSize = 14,
        //                 Padding = StaticData.WindowListThickness,
        //                 IsSelected = LastSelectedItemId == window.WindowId,
        //                 ContextMenu = new ContextMenu()
        //                 {
        //                     Items =
        //                     {
        //                         new MenuItem()
        //                         {
        //                             Header = "Raise to front",
        //                             Command = new ContextMenuCommand(() =>
        //                                 WindowAccessor.RaiseWindow(WindowsConfigs.FirstOrDefault(x =>
        //                                     x.WindowId.StartsWith(LastSelectedItemId)).WindowId))
        //                         },
        //                         new MenuItem()
        //                         {
        //                             Header = "Add to blacklist",
        //                             Command = new ContextMenuCommand(async void () =>
        //                                 await MainWindow.AddToBlacklist(window.WindowTitle))
        //                         }
        //                     }
        //                 }
        //             });
        //         });
        //     }
        //     else
        //     {
        //         await Dispatcher.UIThread.InvokeAsync(() =>
        //         {
        //             WindowsListBoxItems.First(x => ((ListBoxItem)x).Name == window.WindowId)
        //                 .Content = window.ShortWindowTitle;
        //         });
        //     }
        //}
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
                    if (WindowsConfigs.Any(x => x.WindowId == fetchedWindow.WindowId))
                        WindowsConfigs.Remove(WindowsConfigs.First(x => x.WindowId == fetchedWindow.WindowId));
                }
            }
        }
    }
}