using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using WindowSwitcher.ViewModels;
using WindowSwitcherLib.Data.FileAccess;
using WindowSwitcherLib.Data.WindowAccess;
using WindowSwitcherLib.WindowAccess;
using WindowSwitcherLib.WindowAccess.CustomWindows.Commands;
using WindowConfig = WindowSwitcherLib.Models.WindowConfig;

namespace WindowSwitcher;

public partial class MainWindow : Window
{
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    private WindowAccessor WindowAccessor { get; set; } = WindowFactories.GetAccessor();
    private ObservableCollection<WindowConfig> Windows { get; set; } = new();
    private List<FloatingWindow> FloatingWindows { get; set; } = new();
    private PrefixesWindow PrefixesWindow { get; set; }
    public PrefixesWindow BlacklistWindow { get; set; }
    private string? LastSelectedItemId { get; set; } = "";
    private static bool RefreshButtonEnabled { get; set; } = true;

    public MainWindow()
    {
        InitializeComponent();

        this.DataContext = new WindowListViewModel(WindowAccessor);

        Title = StaticData.AppName;

        PrefixesWindow = new PrefixesWindow(ConfigFileAccessor.GetInstance().Config.WhitelistPrefixes,
            StaticData.PrefixWindowType.whitelist, "Prefix window");
        BlacklistWindow = new PrefixesWindow(ConfigFileAccessor.GetInstance().Config.BlacklistPrefixes,
            StaticData.PrefixWindowType.blacklist, "Blacklist window");

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
            await RefreshWindowsAsync();
            await Task.Delay(ConfigFileAccessor.GetInstance().Config.RefreshTimeoutMs, cancellationToken);
        }
    }

    private async Task RefreshWindowsAsync()
    {
        try
        {
            Collection<WindowConfig> windowConfigs = new Collection<WindowConfig>();
            Dispatcher.UIThread.Post(() =>
            {
                windowConfigs = ((WindowListViewModel)DataContext).WindowsConfigs;
            });
            
            // Show the managed windows on the ui
            foreach (WindowConfig window in windowConfigs)
            {
                if (FloatingWindows.All(x => x.WindowConfig!.WindowId != window.WindowId))
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        FloatingWindows.Add(new FloatingWindow(window, WindowAccessor));
                    });
            }

            // Delete the windows that doesn't exist anymore
            ClearClosedFloatingWindows();
        }
        catch (Exception ex)
        {
            
        }
        

        GC.Collect();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        StaticData.AppClosing = true;
        PrefixesWindow.Close();
        BlacklistWindow.Close();
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
        
        ((WindowListViewModel)DataContext).LastSelectedItemId = selectedPrefix.Name!.ToLower();
    }

    public Task AddToBlacklist(string windowTitle)
    {
        if (RefreshButtonEnabled)
        {
            windowTitle = windowTitle.ToLower();
            if (!BlacklistWindow.ListToEdit.Any(x => x.StartsWith(windowTitle)))
            {
                BlacklistWindow.ListToEdit.Add(windowTitle);
                BlacklistWindow.AddPrefixToList(windowTitle);
            
                ConfigFileAccessor.GetInstance().SaveBlacklist(BlacklistWindow.ListToEdit);
            }
        }

        return Task.CompletedTask;
    }
    
    private async void RefreshClicked(object? sender, RoutedEventArgs e)
    {
        RefreshButtonEnabled = false;
        await ((WindowListViewModel)DataContext).FetchWindowsAsync();
        RefreshButtonEnabled = true;
    }

    private void ClearClosedFloatingWindows()
    {
        //IEnumerable<ListBoxItem> itemsToRemove = ((WindowListViewModel)DataContext).WindowsListBoxItems.Where(window => Windows.All(config => config.WindowId != ((ListBoxItem)window).Name)).ToList();
        IEnumerable<FloatingWindow> windowsToRemove = FloatingWindows.Where(x => Windows.All(c => c.WindowId != x.WindowConfig.WindowId)).ToList();

        // foreach(var itemToRemove in itemsToRemove)
        //     ((WindowListViewModel)DataContext).WindowsListBoxItems.Remove(itemToRemove);

        foreach (FloatingWindow window in windowsToRemove)
        {
            // TODO : Find a more elegant solution for closing the floating window
            StaticData.AppClosing = true;
            window.Close();
            StaticData.AppClosing = false;
            FloatingWindows.Remove(window);
        }
    }
}