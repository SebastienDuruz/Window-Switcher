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
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(Action applyAction)
    {
        InitializeComponent();
        ISettingsPlatformPolicy settingsPlatformPolicy = AppServiceProvider.GetRequiredService<ISettingsPlatformPolicy>();
        _viewModel = new SettingsViewModel(applyAction, settingsPlatformPolicy);
        DataContext = _viewModel;
        Closing += OnClosing;
    }

    public void RefreshPendingValues()
    {
        _viewModel.ResetPendingValues();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _viewModel.ResetPendingValues();
        _windowLifecycle.HandleClosing(this, e, hideWhenCanceled: true, hideWhenAllowed: true);
    }

}
