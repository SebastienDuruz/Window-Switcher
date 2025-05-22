using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using WindowSwitcher.ViewModels;
using WindowSwitcherLib.Data.FileAccess;
using WindowSwitcherLib.WindowAccess;

namespace WindowSwitcher.Windows;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        DataContext = new SettingsViewModel();
        Closing += OnClosing;
    }
    
    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = !StaticData.AppClosing;
        Hide();
    }

    private void StartMinimizedCheckedChange(object? sender, RoutedEventArgs e)
    {
        ConfigFileAccessor.GetInstance().Config.StartMinimized =
            ((CheckBox)sender!).IsChecked!.Value;
    }
    
    private void ResizeWindowsCheckedChange(object? sender, RoutedEventArgs e)
    {
        ConfigFileAccessor.GetInstance().Config.ResizeWindows =
            ((CheckBox)sender!).IsChecked!.Value;
    }
    
    private void MoveWindowsCheckedChange(object? sender, RoutedEventArgs e)
    {
        ConfigFileAccessor.GetInstance().Config.MoveWindows =
            ((CheckBox)sender!).IsChecked!.Value;
    }
    
    private void ApplyButtonClick(object? sender, RoutedEventArgs e)
    {
        MainWindow.ApplySettings();
    }

    private void FixedWindowCheckedChange(object? sender, RoutedEventArgs e)
    {
        ConfigFileAccessor.GetInstance().Config.UseFixedWindowSize =
            ((CheckBox)sender!).IsChecked!.Value;

        FixedWidthStackPanel.IsVisible = ((CheckBox)sender!).IsChecked!.Value;
        FixedHeightStackPanel.IsVisible = ((CheckBox)sender!).IsChecked!.Value;
    }

    private void FixedWidthNumericChange(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        ConfigFileAccessor.GetInstance().Config.WindowWidth =
            (int)((NumericUpDown)sender!).Value.Value;
    }
    
    private void FixedHeightNumericChange(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        ConfigFileAccessor.GetInstance().Config.WindowHeight =
            (int)((NumericUpDown)sender!).Value.Value;
    }
}