using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using WindowSwitcher.ViewModels;
using WindowSwitcherLib.Data.FileAccess;
using WindowSwitcherLib.Data.WindowAccess;

namespace WindowSwitcher;

public partial class App : Application
{
    private Windows.MainWindow MainWindow { get; set; }
    
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        DataFolders.CheckFolders();
        
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow = new Windows.MainWindow();
            desktop.MainWindow = MainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ExitMenuItemClicked(object? sender, EventArgs e)
    {
        MainWindow.Close();
    }

    private void TrayIconClicked(object? sender, EventArgs e)
    {
        if(MainWindow.WindowState == WindowState.Minimized)
            MainWindow.WindowState = WindowState.Normal;
    }
}