using System;
using Avalonia.Controls;
using WindowSwitcher.ViewModels;
using WindowSwitcherLib.Data;

namespace WindowSwitcher.Windows;

public partial class SettingsWindow : Window
{
    public SettingsWindow(Action applyAction)
    {
        InitializeComponent();
        DataContext = new SettingsViewModel(applyAction);
        Closing += OnClosing;
    }
    
    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = !StaticData.AppClosing;
        Hide();
    }

}
