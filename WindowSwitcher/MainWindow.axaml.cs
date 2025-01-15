using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Templates;
using Avalonia.Media;
using Avalonia.Threading;
using WindowSwitcherLib.WindowAccess;
using WindowSwitcherLib.Models;
using WindowSwitcherLib.WindowAccess.CustomWindows.Commands;
using WindowConfig = WindowSwitcherLib.Models.WindowConfig;

namespace WindowSwitcher;

public partial class MainWindow : Window
{
    private readonly CancellationTokenSource _cts;
    private WindowAccessor WindowAccessor { get; set; } = WindowFactories.GetAccessor();
    private List<WindowConfig> Windows { get; set; } = new();
    private List<FloatingWindow> FloatingWindows { get; set; } = new();
    private PrefixesWindow PrefixesWindow { get; set; } = new(ConfigFileAccessor.GetInstance().Config!.WhitelistPrefixes, "Prefix window");
    private PrefixesWindow BlacklistWindow { get; set; } = new(ConfigFileAccessor.GetInstance().Config!.BlacklistPrefixes, "Blacklist window");
    private string? LastSelectedItemID { get; set; } = "";
    
    public MainWindow()
    {
        InitializeComponent();

        _cts = new CancellationTokenSource();
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
            await Task.Delay(5000, cancellationToken);
        }
    }

    private async Task FetchWindowsAsync()
    {
        Windows.Clear();
        foreach (WindowConfig fetchedWindow in WindowAccessor.GetWindows())
            foreach(string prefix in ConfigFileAccessor.GetInstance().Config!.WhitelistPrefixes)
                if(fetchedWindow.WindowTitle.ToLower().StartsWith(prefix) 
                   && !ConfigFileAccessor.GetInstance().Config!.BlacklistPrefixes.Exists(
                       x => x.StartsWith(fetchedWindow.WindowTitle.ToLower())))
                    Windows.Add(fetchedWindow);
        
        // TODO : currently the blacklist is only used as full name blacklist, not prefix

        // TODO : Show the managed windows
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            WindowsListBox.Items.Clear();
            foreach (WindowConfig window in Windows)
            {
                // ListBox Configuration panel
                WindowsListBox.Items.Add(new ListBoxItem()
                {
                    Name = window.WindowId,
                    Content = window.ShortWindowTitle,
                    Height = 22,
                    FontSize = 14,
                    Padding = new Thickness(8, 2),
                    IsSelected = LastSelectedItemID == window.WindowId,
                    ContextMenu = new ContextMenu()
                    {
                        Items =
                        {
                            new MenuItem()
                            {
                                Header = "Raise to front",
                                Command = new ContextMenuCommand(() => WindowAccessor.RaiseWindow(Windows.FirstOrDefault(x => x.WindowId.StartsWith(LastSelectedItemID))))
                            },
                            new MenuItem() { 
                                Header = "Add to blacklist",
                                Command = new ContextMenuCommand(() => AddToBlacklist(window.WindowTitle.ToLower()))
                            }
                        }
                    }
                });
                
                // Refresh floating windows
                if(FloatingWindows.All(x => x.WindowConfig.WindowId != window.WindowId))
                    FloatingWindows.Add(new FloatingWindow(window, WindowAccessor));
            }
            
            // Delete the windows that doesn't exist anymore
            ClearClosedFloatingWindows();
        });
        
        GC.Collect();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
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
        
        LastSelectedItemID = selectedPrefix.Name!.ToLower();
    }
    
    private async void RefreshClicked(object? sender, RoutedEventArgs e)
    {
        await FetchWindowsAsync();
    }

    private void AddToBlacklist(string windowTitle)
    {
        if (BlacklistWindow.ListToEdit.All(x => x != windowTitle))
        {
            BlacklistWindow.ListToEdit.Add(windowTitle);
            BlacklistWindow.AddPrefixToList(windowTitle);       
        }
        ConfigFileAccessor.GetInstance().WriteUserSettings();
    }

    private void ClearClosedFloatingWindows()
    {
        List<FloatingWindow> windowsToRemove = FloatingWindows.Where(window => Windows.All(config => config.WindowId != window.WindowConfig.WindowId)).ToList();
        windowsToRemove.ForEach(window => window.Close());
        FloatingWindows.RemoveAll(window => windowsToRemove.Contains(window));
    }
    
}