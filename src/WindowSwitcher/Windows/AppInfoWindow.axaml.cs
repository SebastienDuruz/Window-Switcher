using Avalonia.Controls;
using WindowSwitcher.Hosting;
using WindowSwitcher.Lib.Data.Platform.SystemInfo.Abstractions;
using WindowSwitcher.ViewModels;
using WindowSwitcher.Windows.Services;

namespace WindowSwitcher.Windows;

public partial class AppInfoWindow : Window
{
    private readonly UtilityWindowService _windowLifecycle = new();
    private AppInfoViewModel ViewModel { get; }

    public AppInfoWindow()
    {
        InitializeComponent();
        IPlatformAppInfoProvider appInfoProvider =
            AppServiceProvider.GetRequiredService<IPlatformAppInfoProvider>();
        ViewModel = new AppInfoViewModel(appInfoProvider);
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
        _windowLifecycle.HandleClosing(this, e);
    }
}
