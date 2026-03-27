using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using WindowSwitcher.Hosting;
using WindowSwitcher.Lib.Data.Updates.Abstractions;
using WindowSwitcher.Lib.Data.Platform.SystemInfo.Abstractions;
using WindowSwitcher.ViewModels;
using WindowSwitcher.Windows.Services;

namespace WindowSwitcher.Windows;

public partial class AppInfoWindow : Window
{
    private readonly UtilityWindowService _windowLifecycle = new();
    private AppInfoViewModel ViewModel { get; }

    public AppInfoWindow()
        : this(RequestApplicationShutdownFromLifetime) { }

    public AppInfoWindow(Action requestApplicationShutdown)
    {
        ArgumentNullException.ThrowIfNull(requestApplicationShutdown);

        InitializeComponent();
        IPlatformAppInfoProvider appInfoProvider =
            AppServiceProvider.GetRequiredService<IPlatformAppInfoProvider>();
        IAppUpdateService appUpdateService =
            AppServiceProvider.GetRequiredService<IAppUpdateService>();
        ViewModel = new AppInfoViewModel(
            appInfoProvider,
            appUpdateService,
            requestApplicationShutdown
        );
        DataContext = ViewModel;
        Closing += OnClosing;
    }

    public void Refresh()
    {
        ViewModel.Refresh();
    }

    public Task<bool> CheckForUpdatesAsync(
        bool force,
        bool showUpToDateMessage,
        CancellationToken cancellationToken = default
    )
    {
        return ViewModel.CheckForUpdatesAsync(force, showUpToDateMessage, cancellationToken);
    }

    private void CloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _windowLifecycle.HandleClosing(this, e);
    }

    private static void RequestApplicationShutdownFromLifetime()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}
