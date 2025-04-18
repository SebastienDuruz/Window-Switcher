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
        Hide();
    }
}