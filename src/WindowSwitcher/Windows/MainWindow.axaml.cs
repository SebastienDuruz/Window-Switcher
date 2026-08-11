using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using WindowSwitcher.Hosting;
using WindowSwitcher.Lib.Data;
using WindowSwitcher.Lib.Data.Platform.Commands.Dependencies;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Factories;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcher.ViewModels;
using WindowSwitcher.Windows.Abstractions;
using WindowSwitcher.Windows.Services;
using WindowConfig = WindowSwitcher.Lib.Models.WindowConfig;

namespace WindowSwitcher.Windows;

public partial class MainWindow : Window, IFloatingWindowHost
{
    private WinAccessorBase WinAccessorBase { get; } = AccessorFactory.GetAccessor();
    private IPreviewFrameProvider PreviewFrameProvider { get; }
    private readonly IPreviewSelectionReset? _previewSelectionReset;
    private readonly FloatingWindowRegistry _floatingWindowRegistry;
    private FiltersWindow FiltersWindow { get; }
    private SettingsWindow SettingsWindow { get; }
    private KeybindsWindow KeybindsWindow { get; }
    private AppInfoWindow AppInfoWindow { get; }
    private RenameWindow RenameWindow { get; }
    private WindowListViewModel ViewModel { get; }
    private readonly FloatingPreviewCoordinator _previewCoordinator;
    private readonly WindowBlacklistCoordinator _blacklistCoordinator;
    private readonly MissingDependencyNotificationService _missingDependencyNotificationService;
    private readonly StartupUpdateNotificationService _startupUpdateNotificationService;
    private readonly IMainWindowConfigurationService _configurationService;
    private bool _suppressWindowStateHandling;
    private bool _isHiddenToTray;
    private readonly CancellationTokenSource _updateNotificationCts = new();

    public MainWindow()
    {
        InitializeComponent();

        PropertyChanged += OnWindowPropertyChanged;

        PreviewFrameProvider = PreviewFactory.Create(WinAccessorBase);
        _previewSelectionReset = PreviewFrameProvider as IPreviewSelectionReset;
        ResetAllPreviewsMenuItem.IsVisible = _previewSelectionReset is not null;
        if (_previewSelectionReset is not null)
            _previewSelectionReset.SelectionPromptChanged += OnSelectionPromptChanged;
        IFloatingWindowSettingsService floatingWindowSettingsService =
            AppServiceProvider.GetRequiredService<IFloatingWindowSettingsService>();
        _configurationService =
            AppServiceProvider.GetRequiredService<IMainWindowConfigurationService>();
        _floatingWindowRegistry = new FloatingWindowRegistry(
            WinAccessorBase,
            PreviewFrameProvider,
            this,
            floatingWindowSettingsService
        );

        var dispatcher = new AvaloniaViewModelDispatcher();
        ViewModel = new WindowListViewModel(
            new WindowAccessorWindowSnapshotProvider(WinAccessorBase),
            new ConfigFileWindowFilterSettingsProvider(),
            dispatcher
        );
        DataContext = ViewModel;
        Title = StaticData.AppName;

        FiltersWindow = new FiltersWindow(
            _configurationService.GetWhitelistPrefixes().ToList(),
            _configurationService.GetBlacklistPrefixes().ToList()
        );
        SettingsWindow = new SettingsWindow(ApplySettings);
        KeybindsWindow = new KeybindsWindow(() => ViewModel.WindowsConfigs.ToArray());
        AppInfoWindow = new AppInfoWindow(RequestApplicationShutdown)
        {
            Title = $"About {StaticData.AppName}",
        };
        RenameWindow = new RenameWindow();
        _previewCoordinator = new FloatingPreviewCoordinator(
            AppServiceProvider.GetRequiredService<IWindowKeybindActivator>(),
            dispatcher,
            SetActivePreviewByWindowId
        );
        _blacklistCoordinator = new WindowBlacklistCoordinator(
            FiltersWindow,
            ViewModel.TempWindowIdsBlacklist
        );
        _missingDependencyNotificationService = new MissingDependencyNotificationService(this);
        _startupUpdateNotificationService = new StartupUpdateNotificationService(
            this,
            AppInfoWindow
        );

        ViewModel.WindowsConfigs.CollectionChanged += WindowsConfigsChanged;
        _floatingWindowRegistry.Initialize(ViewModel.WindowsConfigs);
        _ = _startupUpdateNotificationService.NotifyUpdateAvailabilityOnStartupAsync(
            _updateNotificationCts.Token
        );
        if (OperatingSystem.IsLinux())
        {
            LinuxDependencies.DependencyMissing += OnDependencyMissing;
            ShowPreviouslyReportedDependencies();
        }

        if (_configurationService.ShouldStartMinimized())
            Dispatcher.UIThread.Post(
                HideToTray,
                DispatcherPriority.Background
            );
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        StaticData.AppClosing = true;
        if (!_updateNotificationCts.IsCancellationRequested)
            _updateNotificationCts.Cancel();
        if (OperatingSystem.IsLinux())
            LinuxDependencies.DependencyMissing -= OnDependencyMissing;
        if (_previewSelectionReset is not null)
            _previewSelectionReset.SelectionPromptChanged -= OnSelectionPromptChanged;
        PropertyChanged -= OnWindowPropertyChanged;
        _previewCoordinator.Dispose();
        ViewModel.WindowsConfigs.CollectionChanged -= WindowsConfigsChanged;
        FiltersWindow.Close();
        SettingsWindow.Close();
        KeybindsWindow.Close();
        AppInfoWindow.Close();
        RenameWindow.Close();
        _missingDependencyNotificationService.Dispose();
        _floatingWindowRegistry.CloseAll();
        PreviewFrameProvider.Dispose();
        _updateNotificationCts.Dispose();
        _configurationService.PersistUserSettings();
        ViewModel.Dispose();
        base.OnClosing(e);
    }

    public void RestoreFromTray()
    {
        if (_isHiddenToTray)
        {
            ShowInTaskbar = true;
            Show();
        }

        _isHiddenToTray = false;
        SetWindowStateWithoutTrayHandling(WindowState.Normal);
        Activate();
    }

    private void HideToTray()
    {
        if (_isHiddenToTray || StaticData.AppClosing)
            return;

        _isHiddenToTray = true;
        ShowInTaskbar = false;
        SetWindowStateWithoutTrayHandling(WindowState.Normal);
        Hide();
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_suppressWindowStateHandling || e.Property != WindowStateProperty)
            return;

        if (WindowState == WindowState.Minimized)
            HideToTray();
    }

    private void SetWindowStateWithoutTrayHandling(WindowState state)
    {
        if (WindowState == state)
            return;

        _suppressWindowStateHandling = true;
        try
        {
            WindowState = state;
        }
        finally
        {
            _suppressWindowStateHandling = false;
        }
    }

    private void OpenDataFolderClick(object? sender, RoutedEventArgs e)
    {
        Process.Start(
            new ProcessStartInfo { FileName = StaticData.DataFolder, UseShellExecute = true }
        );
    }

    private void ClearFloatingWindowSettings(object? sender, RoutedEventArgs e)
    {
        _configurationService.ResetFloatingWindowSettings();
    }

    private void ResetUserSettings(object? sender, RoutedEventArgs e)
    {
        _configurationService.ResetUserSettings();
    }

    private void OpenFiltersWindowClick(object? sender, RoutedEventArgs e)
    {
        FiltersWindow.ShowPrefixesTab();
    }

    private void OpenSettingsWindowClick(object? sender, RoutedEventArgs e)
    {
        SettingsWindow.RefreshPendingValues();
        SettingsWindow.Show();
    }

    private void OpenKeybindsWindowClick(object? sender, RoutedEventArgs e)
    {
        KeybindsWindow.RefreshData();
        KeybindsWindow.Show();
    }

    private void OpenAppInfoWindowClick(object? sender, RoutedEventArgs e)
    {
        AppInfoWindow.Refresh();
        _ = AppInfoWindow.CheckForUpdatesAsync(force: false, showUpToDateMessage: false);
        if (AppInfoWindow.IsVisible)
        {
            AppInfoWindow.Activate();
            return;
        }

        if (IsVisible)
        {
            AppInfoWindow.Show(this);
            return;
        }

        AppInfoWindow.Show();
    }

    public void AddToBlacklist(string windowTitle)
    {
        _blacklistCoordinator.AddToBlacklist(windowTitle);
    }

    public void AddToTempBlacklist(string windowId)
    {
        _blacklistCoordinator.AddToTemporaryBlacklist(windowId);
    }

    public bool CanResetPreviewSelection => _previewSelectionReset is not null;

    public void ResetPreviewSelection(string windowId)
    {
        if (string.IsNullOrWhiteSpace(windowId))
            return;

        _previewSelectionReset?.ResetSelection(windowId);
    }

    private void ResetAllPreviewsClick(object? sender, RoutedEventArgs e)
    {
        _previewSelectionReset?.ResetAllSelections();
    }

    private void OnSelectionPromptChanged(
        object? sender,
        PreviewSelectionPromptEventArgs eventArgs
    )
    {
        if (StaticData.AppClosing)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (
                _floatingWindowRegistry.TryGet(
                    eventArgs.WindowId,
                    out FloatingWindow floatingWindow
                )
            )
            {
                floatingWindow.SetSelectionPending(eventArgs.IsPending);
            }
        });
    }

    public void SetActivePreview(IFloatingPreviewWindow floatingWindow)
    {
        _previewCoordinator.SetActivePreview(floatingWindow);
    }

    private void SetActivePreviewByWindowId(string windowId)
    {
        if (!_floatingWindowRegistry.TryGet(windowId, out FloatingWindow floatingWindow))
            return;

        SetActivePreview(floatingWindow);
    }

    public void ClearActivePreview(IFloatingPreviewWindow floatingWindow)
    {
        _previewCoordinator.ClearActivePreview(floatingWindow);
    }

    public void NotifyPreviewWindowActivated(string windowId)
    {
        _previewCoordinator.NotifyPreviewWindowActivated(windowId);
    }

    public async Task RenameWindowTitleAsync(string windowId)
    {
        if (string.IsNullOrWhiteSpace(windowId))
            return;

        WindowConfig? windowConfig = ViewModel.WindowsConfigs.FirstOrDefault(config =>
            string.Equals(config.WindowId, windowId, StringComparison.Ordinal)
        );
        if (windowConfig is null)
            return;

        bool isUpdated = await RenameWindow.ShowAndWaitForResultAsync(windowConfig.WindowTitle);
        if (!isUpdated)
            return;

        string renamedTitle = RenameWindow.NewWindowTitle;
        await WinAccessorBase.RenameWindowTitleAsync(windowId, renamedTitle);

        windowConfig.WindowTitle = renamedTitle;

        if (_floatingWindowRegistry.TryGet(windowId, out FloatingWindow floatingWindow))
            floatingWindow.UpdateWindowTitle(renamedTitle);
    }

    private void ShowPreviouslyReportedDependencies()
    {
        IReadOnlyCollection<string> reportedMissing = LinuxDependencies.GetReportedMissing();
        if (reportedMissing.Count == 0)
            return;

        Dispatcher.UIThread.Post(() =>
            _missingDependencyNotificationService.RegisterMissingDependencies(reportedMissing)
        );
    }

    private void OnDependencyMissing(string dependency)
    {
        if (StaticData.AppClosing)
            return;
        if (string.IsNullOrWhiteSpace(dependency))
            return;

        Dispatcher.UIThread.Post(() =>
            _missingDependencyNotificationService.RegisterMissingDependency(dependency)
        );
    }

    private void WindowsConfigsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _floatingWindowRegistry.SynchronizeWithCollectionChange(e, ViewModel.WindowsConfigs);
    }

    private void BlacklistMenuItemClick(object? sender, RoutedEventArgs e)
    {
        AddToBlacklist(((string)((MenuItem)sender!).Tag!));
    }

    private void TempBlacklistMenuItemClick(object? sender, RoutedEventArgs e)
    {
        AddToTempBlacklist((string)((MenuItem)sender!).Tag!);
    }

    private async void RenameMenuItemClick(object? sender, RoutedEventArgs e)
    {
        await RenameWindowTitleAsync((string)((MenuItem)sender!).Tag!);
    }

    public void ApplySettings()
    {
        _floatingWindowRegistry.ApplySettings();
    }

    private void RequestApplicationShutdown()
    {
        if (StaticData.AppClosing)
            return;

        Close();
    }

}
