using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using WindowSwitcherLib.Data.FileAccess;

namespace WindowSwitcher;

public partial class App : Application
{
    private MainWindow MainWindow { get; set; }
    
    public override void Initialize()
    {
        ConfigFileAccessor.GetInstance().ReadUserSettings();
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow = new MainWindow();
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