using System;
using Avalonia.Controls;
using WindowSwitcher.Hosting;
using WindowSwitcher.ViewModels;
using WindowSwitcher.Windows.Services;
using WindowSwitcherLib.Data.Platform.SystemInfo.Abstractions;

namespace WindowSwitcher.Windows;

public partial class SettingsWindow : Window
{
    private readonly UtilityWindowService _windowLifecycle = new();

    public SettingsWindow(Action applyAction)
    {
        InitializeComponent();
        ISettingsPlatformPolicy settingsPlatformPolicy = AppServiceProvider.GetRequiredService<ISettingsPlatformPolicy>();
        DataContext = new SettingsViewModel(applyAction, settingsPlatformPolicy);
        Closing += OnClosing;
    }
    
    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _windowLifecycle.HandleClosing(this, e, hideWhenCanceled: true, hideWhenAllowed: true);
    }

}
