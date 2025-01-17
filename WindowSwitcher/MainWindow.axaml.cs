using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using WindowSwitcherLib.Data.FileAccess;
using WindowSwitcherLib.WindowAccess;
using WindowSwitcherLib.WindowAccess.CustomWindows.Commands;
using WindowConfig = WindowSwitcherLib.Models.WindowConfig;

namespace WindowSwitcher;

public partial class MainWindow : Window
{
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    private WindowAccessor WindowAccessor { get; set; } = WindowFactories.GetAccessor();
    private List<WindowConfig> Windows { get; set; } = new();
    private List<FloatingWindow> FloatingWindows { get; set; } = new();
    private PrefixesWindow PrefixesWindow { get; set; }
    private PrefixesWindow BlacklistWindow { get; set; }
    private string? LastSelectedItemId { get; set; } = "";
    
    public MainWindow()
    {
        InitializeComponent();

        Title = StaticData.AppName;

        PrefixesWindow = new PrefixesWindow(ConfigFileAccessor.GetInstance().Config.WhitelistPrefixes, StaticData.PrefixWindowType.whitelist, "Prefix window");
        BlacklistWindow = new PrefixesWindow(ConfigFileAccessor.GetInstance().Config.BlacklistPrefixes, StaticData.PrefixWindowType.blacklist, "Blacklist window");

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

    private async Task FetchWindowsAsync()
    {
        Windows.Clear();
        FetchWindowsWithFilters();
        
        // Show the managed windows on the ui
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            foreach (WindowConfig window in Windows)
            {
                if (WindowsListBox.Items.All(x => ((ListBoxItem)x).Name != window.WindowId))
                {
                    // ListBox Configuration panel
                    WindowsListBox.Items.Add(new ListBoxItem()
                    {
                        Name = window.WindowId,
                        Content = window.ShortWindowTitle,
                        Height = 22,
                        FontSize = 14,
                        Padding = new Thickness(8, 2),
                        IsSelected = LastSelectedItemId == window.WindowId,
                        ContextMenu = new ContextMenu()
                        {
                            Items =
                            {
                                new MenuItem()
                                {
                                    Header = "Raise to front",
                                    Command = new ContextMenuCommand(() => WindowAccessor.RaiseWindow(Windows.FirstOrDefault(x => x.WindowId.StartsWith(LastSelectedItemId))))
                                },
                                new MenuItem() { 
                                    Header = "Add to blacklist",
                                    Command = new ContextMenuCommand(async void () =>
                                    {
                                        try
                                        {
                                            await AddToBlacklist(window.WindowTitle);
                                        }
                                        catch (Exception e)
                                        {
                                            // TODO handle exception
                                        }
                                    })
                                }
                            }
                        }
                    });
                }
                else
                {
                    ((ListBoxItem)WindowsListBox.Items.First(x => ((ListBoxItem)x).Name == window.WindowId)).Content = window.ShortWindowTitle;
                }
                
                // Refresh floating windows
                if(FloatingWindows.All(x => x.WindowConfig!.WindowId != window.WindowId))
                    FloatingWindows.Add(new FloatingWindow(window, WindowAccessor));
            }
            
            // Delete the windows that doesn't exist anymore
            ClearClosedFloatingWindows();
        });
        
        GC.Collect();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        StaticData.AppClosing = true;
        PrefixesWindow.Destroy();
        BlacklistWindow.Destroy();
        foreach(FloatingWindow floatingWindow in FloatingWindows)
            floatingWindow.Close();
        ConfigFileAccessor.GetInstance().WriteUserSettings();
        _cts.Cancel();
        base.OnClosing(e);
    }

    private void OpenDataFolderClick(object? sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = StaticData.DataFolder,
            UseShellExecute = true
        });
    }

    private void OpenPrefixesWindowClick(object? sender, RoutedEventArgs e)
    {
        PrefixesWindow.Show();
    }
    
    private void OpenBlacklistWindowClick(object? sender, RoutedEventArgs e)
    {
        BlacklistWindow.Show();
    }

    private void WindowsListBoxSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0)
            return;

        ListBoxItem? selectedPrefix = e.AddedItems[0] as ListBoxItem;
        if (selectedPrefix == null)
            return;
        
        LastSelectedItemId = selectedPrefix.Name!.ToLower();
    }

    private async Task AddToBlacklist(string windowTitle)
    {
        windowTitle = windowTitle.ToLower();
        if (!BlacklistWindow.ListToEdit.Any(x => x.StartsWith(windowTitle)))
        {
            BlacklistWindow.ListToEdit.Add(windowTitle);
            BlacklistWindow.AddPrefixToList(windowTitle);
            
            ConfigFileAccessor.GetInstance().SaveBlacklist(BlacklistWindow.ListToEdit);
        }
        
        await FetchWindowsAsync();
    }
    
    private async void RefreshClicked(object? sender, RoutedEventArgs e)
    {
        RefreshButton.IsEnabled = false;
        await FetchWindowsAsync();
        RefreshButton.IsEnabled = true;
    }

    private void FetchWindowsWithFilters()
    {
        // Apply the prefixes and remove the blacklisted clients
        foreach (WindowConfig? fetchedWindow in WindowAccessor.GetWindows())
        {
            foreach (string prefix in ConfigFileAccessor.GetInstance().Config!.WhitelistPrefixes)
            {
                if (fetchedWindow!.WindowTitle.ToLower().StartsWith(prefix)
                    && !ConfigFileAccessor.GetInstance().Config!.BlacklistPrefixes.Exists(x =>
                        x.StartsWith(fetchedWindow.WindowTitle.ToLower())))
                {
                    Windows.Add(fetchedWindow);
                }
            }
                
        }
            
    }

    private void ClearClosedFloatingWindows()
    {
        IEnumerable<object?> itemsToRemove = WindowsListBox.Items.Where(window =>
            Windows.All(config => config.WindowId != ((ListBoxItem)window).Name));

        IEnumerable<FloatingWindow> windowsToRemove =
            FloatingWindows.Where(x => Windows.All(c => c.WindowId != x.WindowConfig.WindowId));

        foreach (FloatingWindow window in windowsToRemove)
        {
            // TODO : Find a more elegant solution for closing the floating window
            StaticData.AppClosing = true;
            window.Close();
            StaticData.AppClosing = false;
            FloatingWindows.Remove(window);            
        }

        foreach(var itemToRemove in itemsToRemove)
            WindowsListBox.Items.Remove(itemToRemove);
    }
    
}