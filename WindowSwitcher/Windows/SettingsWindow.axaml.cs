using System;
using Avalonia.Controls;
using WindowSwitcher.ViewModels;
using WindowSwitcher.Windows.Services;

namespace WindowSwitcher.Windows;

public partial class SettingsWindow : Window
{
    private readonly UtilityWindowService _windowLifecycle = new();

    public SettingsWindow(Action applyAction)
    {
        InitializeComponent();
        DataContext = new SettingsViewModel(applyAction);
        Closing += OnClosing;
    }
    
    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _windowLifecycle.HandleClosing(this, e, hideWhenCanceled: true, hideWhenAllowed: true);
    }

}
