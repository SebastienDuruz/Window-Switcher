using System;
using Avalonia.Controls;
using WindowSwitcher.ViewModels;
using WindowSwitcherLib.Data;
using WindowSwitcherLib.Data.Common;

namespace WindowSwitcher.Windows;

public partial class AppInfoWindow : Window
{
    private AppInfoViewModel ViewModel { get; }

    public AppInfoWindow()
    {
        InitializeComponent();
        ViewModel = new AppInfoViewModel();
        DataContext = ViewModel;
        Closing += OnClosing;
    }

    public void Refresh()
    {
        ViewModel.Refresh();
    }

    private void CloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = !StaticData.AppClosing;
        if (e.Cancel)
            Hide();
    }
}
