using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Templates;
using Avalonia.Threading;
using WindowSwitcherLib.WindowAccess;
using WindowSwitcherLib.Models;
using WindowConfig = WindowSwitcherLib.Models.WindowConfig;

namespace WindowSwitcher;

public partial class MainWindow : Avalonia.Controls.Window
{
    private CancellationTokenSource _cts;
    private WindowAccessor WindowAccessor { get; set; } = WindowFactories.GetAccessor();
    private List<WindowConfig> Windows { get; set; } = new();
    private PrefixesWindow PrefixesWindow { get; set; } = new(ConfigFileAccessor.GetInstance().Config!.WhitelistPrefixes, "Prefix window");
    private PrefixesWindow BlacklistWindow { get; set; } = new(ConfigFileAccessor.GetInstance().Config!.BlacklistPrefixes, "Blacklist window");
    
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
            await Task.Delay(2000, cancellationToken);
            await FetchWindowsAsync();
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
            WindowStackPanel.Children.Clear();
            foreach (WindowConfig window in Windows)
                WindowStackPanel.Children.Add(new ListBoxItem()
                {
                    Name = window.WindowId,
                    Content = window.ShortWindowTitle,
                });
        });
        
        GC.Collect();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        PrefixesWindow.Destroy();
        BlacklistWindow.Destroy();
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
}