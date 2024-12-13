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
    private ObservableCollection<WindowConfig> Windows = new();
    private ObservableCollection<CheckBox> WindowsCheckBoxes { get; set; }= new();
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
            await Task.Delay(1000, cancellationToken);
            await ExecuteAsyncMethod();
        }
    }

    private async Task ExecuteAsyncMethod()
    {
        foreach (WindowConfig fetchedWindow in WindowAccessor.GetWindows())
        {
            // Find the window in the config file
            WindowConfig? existingWindow = ConfigFile.Config.Windows
                .FirstOrDefault(x => x.WindowTitle == fetchedWindow.WindowTitle);

            if (existingWindow != null)
            {
                // Update the id of the window
                existingWindow.WindowId = fetchedWindow.WindowId;
            }
            else
            {
                ConfigFile.Config.Windows.Add(fetchedWindow);
            }
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            WindowsCheckBoxes.Clear();
            foreach (WindowConfig window in ConfigFile.Config.Windows)
                WindowsCheckBoxes.Add(new CheckBox()
                {
                    IsChecked = window.IsSelected,
                    Name = window.WindowId,
                    Content = window.ShortWindowTitle
                });

            WindowStackPanel.Children.Clear();
            foreach (CheckBox checkBox in WindowsCheckBoxes)
            {
                if (!WindowStackPanel.Children.Any(x => x.Name.Equals(checkBox.Name)))
                    WindowStackPanel.Children.Add(checkBox);
                else
                {
                    CheckBox box = (CheckBox)WindowStackPanel.Children.First(x => x.Name.Equals(checkBox.Name));
                    box.Content = checkBox.Content;
                    box.IsChecked = checkBox.IsChecked;
                }
            }
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