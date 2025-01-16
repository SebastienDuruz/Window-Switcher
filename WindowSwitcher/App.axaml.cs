using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using WindowSwitcherLib.Data.FileAccess;
using WindowSwitcherLib.WindowAccess;

namespace WindowSwitcher;

public partial class App : Application
{
    public override void Initialize()
    {
        ConfigFileAccessor.GetInstance().ReadUserSettings();
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}