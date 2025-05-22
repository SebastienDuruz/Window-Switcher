using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using WindowSwitcher.ViewModels;
using WindowSwitcherLib.Data.FileAccess;
using WindowSwitcherLib.Data.Interop;
using WindowSwitcherLib.Data.WindowAccess;
using WindowSwitcherLib.WindowAccess;
using WindowConfig = WindowSwitcherLib.Models.WindowConfig;

namespace WindowSwitcher.Windows;

public partial class MainWindow : Window
{
    private readonly CancellationTokenSource _cts = new ();
    private WindowAccessor WindowAccessor { get; } = WindowFactories.GetAccessor();
    private static List<FloatingWindow> FloatingWindows { get; } = new();
    private PrefixesWindow PrefixesWindow { get; }
    private PrefixesWindow BlacklistWindow { get; }
    private SettingsWindow SettingsWindow { get; }
    private RenameWindow RenameWindow { get; }
    private static bool RefreshButtonEnabled { get; set; } = true;
    private List<WindowConfig> WindowConfigs { get; set; } = new();
    
    public MainWindow()
    {
        InitializeComponent();

        DataContext = new WindowListViewModel(WindowAccessor);
        Title = StaticData.AppName;

        PrefixesWindow = new PrefixesWindow(ConfigFileAccessor.GetInstance().Config.WhitelistPrefixes,
            StaticData.PrefixWindowType.whitelist, "Prefixes");
        BlacklistWindow = new PrefixesWindow(ConfigFileAccessor.GetInstance().Config.BlacklistPrefixes,
            StaticData.PrefixWindowType.blacklist, "Blacklist");
        SettingsWindow = new SettingsWindow();
        RenameWindow = new RenameWindow();

        StartBackgroundTask();

        if (ConfigFileAccessor.GetInstance().Config.StartMinimized)
            Dispatcher.UIThread.Post(() =>
            {
                this.WindowState = WindowState.Minimized;
            }, DispatcherPriority.Background);
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
        Console.WriteLine("Refresh periodic task finished...");
    }

    private async Task RefreshWindowsAsync()
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                WindowConfigs = ((WindowListViewModel)DataContext!).WindowsConfigs.ToList();
            });

            await AddFloatingWindows();
            await ClearClosedFloatingWindows();

            GC.Collect();
        }
        catch (Exception ex)
        {
            if(ConfigFileAccessor.GetInstance().Config.ActivateLogs)
                AppLogger.Log(ex.Message, StaticData.LogSeverity.ERRO);
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        StaticData.AppClosing = true;
        PrefixesWindow.Close();
        BlacklistWindow.Close();
        SettingsWindow.Close();
        RenameWindow.Close();
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
            FileName = DataFolders.DataFolder,
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
    
    private void OpenSettingsWindowClick(object? sender, RoutedEventArgs e)
    {
        SettingsWindow.Show();
    }

    public void AddToBlacklist(string windowTitle)
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
    }

    public void AddToTempBlacklist(string windowId)
    {
        if (RefreshButtonEnabled)
            if (((WindowListViewModel)DataContext!).TempWindowIdsBlacklist.All(x => x != windowId))
                ((WindowListViewModel)DataContext).TempWindowIdsBlacklist.Add(windowId);
    }

    /// <summary>
    /// TODO : Optimise this method for better user experience (lag in some cases)
    /// </summary>
    /// <param name="windowId"></param>
    /// <returns></returns>
    public async Task RenameWindowTitle(string windowId)
    {
        RenameWindow.Show();
        while (RenameWindow.IsVisible)
            await Task.Delay(500);

        if (RenameWindow.IsUpdated)
        {
            RenameWindow.IsUpdated = false;
            User32Functions.SetWindowText(IntPtr.Parse(windowId), RenameWindow.NewWindowTitle);
            await Task.Delay(500); // Give time to windowTitle to be updated
            FloatingWindow window = FloatingWindows.First(x => x.WindowConfig!.WindowId == windowId);
            FloatingWindows.Remove(window);
            StaticData.AppClosing = true;
            window.Close();
            StaticData.AppClosing = false;
        }
    }
    
    private void RefreshClicked(object? sender, RoutedEventArgs e)
    {
        RefreshButtonEnabled = false;
        ((WindowListViewModel)DataContext!).FetchWindowsWithFilters();
        RefreshButtonEnabled = true;
    }

    private async Task AddFloatingWindows()
    {
        // Show the managed windows on the ui
        foreach (WindowConfig window in WindowConfigs)
        {
            if (FloatingWindows.All(x => x.WindowConfig!.WindowId != window.WindowId))
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    FloatingWindows.Add(new FloatingWindow(window, WindowAccessor, this));
                });
        }
    }

    private async Task ClearClosedFloatingWindows()
    {
        IEnumerable<FloatingWindow> windowsToRemove =
            FloatingWindows.Where(x => WindowConfigs.All(c => c.WindowId != x.WindowConfig!.WindowId)).ToList();

        foreach (FloatingWindow window in windowsToRemove)
        {
            // TODO : Find a more elegant solution for closing the floating window
            StaticData.AppClosing = true;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                window.Close();
            });
            StaticData.AppClosing = false;
            FloatingWindows.Remove(window);
        }
    }

    private void BlacklistMenuItemClick(object? sender, RoutedEventArgs e)
    {
       AddToBlacklist(((string)((MenuItem)sender!).Tag!)); 
    }
    
    private void TempBlacklistMenuItemClick(object? sender, RoutedEventArgs e)
    {
        AddToTempBlacklist((string)((MenuItem)sender!).Tag!); 
    }

    public static void ApplySettings()
    {
        StaticData.AppClosing = true;
        foreach(FloatingWindow floatingWindow in FloatingWindows)
            floatingWindow.Close();
        StaticData.AppClosing = false;
        FloatingWindows.Clear();
    }
}