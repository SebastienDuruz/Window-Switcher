using Avalonia.Controls;
using Avalonia.Interactivity;
using WindowSwitcher.ViewModels;
using WindowSwitcherLib.Data.FileAccess;
using WindowSwitcherLib.WindowAccess;

namespace WindowSwitcher.Windows;

public partial class RenameWindow : Window
{
    public bool IsUpdated { get; set; } = false;
    public string NewWindowTitle { get; set; } = "";

    public RenameWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }
    
    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = !StaticData.AppClosing;
        Hide();
    }

    private void CancelButtonClick(object? sender, RoutedEventArgs e)
    {
        this.Hide();
    }

    private void RenameButtonClick(object? sender, RoutedEventArgs e)
    {
        IsUpdated = true;
        NewWindowTitle = WindowTitleTextBox.Text;
        this.Hide();
    }
}