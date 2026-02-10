using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using WindowSwitcher.Theming;
using WindowSwitcherLib.Data.Common;
using WindowSwitcherLib.Data.Configuration;

namespace WindowSwitcher;

public partial class App : Application
{
    private Windows.MainWindow? MainWindow { get; set; }
    
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        StaticData.CheckFolders();
        ApplyAccentColorFromConfig();
        
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow = new Windows.MainWindow();
            desktop.MainWindow = MainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ApplyAccentColorFromConfig()
    {
        string configValue = ConfigFileAccessor.GetInstance().ReadConfig(config => config.PreviewHighlightColor);
        if (Color.TryParse(configValue, out Color accentColor))
            AccentColorApplier.Apply(accentColor);
    }

    private void ExitMenuItemClicked(object? sender, EventArgs e)
    {
        MainWindow?.Close();
    }

    private void TrayIconClicked(object? sender, EventArgs e)
    {
        if (MainWindow is null)
            return;
        if (MainWindow.WindowState == WindowState.Minimized)
            MainWindow.WindowState = WindowState.Normal;
    }
}
