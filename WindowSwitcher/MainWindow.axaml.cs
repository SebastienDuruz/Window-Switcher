using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
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
    private ObservableCollection<CheckBox> WindowsCheckBoxes { get; set; }= new();
    private List<WindowConfig> Windows { get; set; } = new();
    private ConfigFileAccessor ConfigFile { get; set; }
    public MainWindow()
    {
        InitializeComponent();

        this.ConfigFile = ConfigFileAccessor.GetInstance();
        
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
            await ExecuteAsyncMethod();
        }
    }

    private async Task ExecuteAsyncMethod()
    {
        Windows.Clear();
        foreach (WindowConfig fetchedWindow in WindowAccessor.GetWindows())
            foreach (string prefix in ConfigFile.Config.WhitelistPrefixes)
                if (fetchedWindow.WindowTitle.ToLower().StartsWith(prefix.ToLower()))
                {
                    Windows.Add(fetchedWindow);
                    break;
                }                

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            WindowStackPanel.Children.Clear();
            foreach (WindowConfig window in Windows)
                WindowStackPanel.Children.Add(new CheckBox()
                {
                    IsChecked = false,
                    Name = window.WindowId,
                    Content = window.ShortWindowTitle
                });
        });
        
        GC.Collect();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        ConfigFile.WriteUserSettings();
        _cts.Cancel();
        base.OnClosing(e);
    }
}