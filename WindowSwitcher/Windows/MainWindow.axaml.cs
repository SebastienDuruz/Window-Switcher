using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using WindowSwitcher.Hosting;
using WindowSwitcher.ViewModels;
using WindowSwitcher.Windows.Abstractions;
using WindowSwitcherLib.Data;
using WindowSwitcherLib.Data.Platform.SystemInfo.Abstractions;
using WindowSwitcherLib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcherLib.Data.Platform.WindowAccess.Factories;
using WindowSwitcherLib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowConfig = WindowSwitcherLib.Models.WindowConfig;

namespace WindowSwitcher.Windows;

public partial class MainWindow : Window, IFloatingWindowHost
{
    private WinAccessorBase WinAccessorBase { get; } = AccessorFactory.GetAccessor();
    private IPreviewFrameProvider PreviewFrameProvider { get; }
    private readonly Dictionary<string, FloatingWindow> _floatingWindows = new(
        StringComparer.Ordinal
    );
    private PrefixesWindow PrefixesWindow { get; }
    private PrefixesWindow BlacklistWindow { get; }
    private SettingsWindow SettingsWindow { get; }
    private AppInfoWindow AppInfoWindow { get; }
    private RenameWindow RenameWindow { get; }
    private IFloatingPreviewWindow? _activePreviewWindow;
    private WindowListViewModel ViewModel { get; }
    private readonly IDependencyNotificationService _dependencyNotificationService;
    private readonly HashSet<string> _missingDependenciesShown = new(
        StringComparer.OrdinalIgnoreCase
    );

    public MainWindow()
    {
        InitializeComponent();
        _dependencyNotificationService =
            AppServiceProvider.GetRequiredService<IDependencyNotificationService>();
        _dependencyNotificationService.DependencyMissing += OnDependencyMissing;

        PreviewFrameProvider = PreviewFactory.Create(WinAccessorBase);

        ViewModel = new WindowListViewModel(WinAccessorBase);
        DataContext = ViewModel;
        Title = StaticData.AppName;

        PrefixesWindow = new PrefixesWindow(
            ConfigFileAccessor
                .GetInstance()
                .ReadConfig(config => config.WhitelistPrefixes.ToList()),
            StaticData.PrefixWindowType.whitelist,
            "Prefixes"
        );
        BlacklistWindow = new PrefixesWindow(
            ConfigFileAccessor
                .GetInstance()
                .ReadConfig(config => config.BlacklistPrefixes.ToList()),
            StaticData.PrefixWindowType.blacklist,
            "Blacklist"
        );
        SettingsWindow = new SettingsWindow(ApplySettings);
        AppInfoWindow = new AppInfoWindow { Title = $"About {StaticData.AppName}" };
        RenameWindow = new RenameWindow();

        ViewModel.WindowsConfigs.CollectionChanged += WindowsConfigsChanged;
        InitializeFloatingWindows(ViewModel.WindowsConfigs);
        ShowPreviouslyReportedDependencies();

        if (ConfigFileAccessor.GetInstance().ReadConfig(config => config.StartMinimized))
            Dispatcher.UIThread.Post(
                () =>
                {
                    this.WindowState = WindowState.Minimized;
                },
                DispatcherPriority.Background
            );
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        StaticData.AppClosing = true;
        _dependencyNotificationService.DependencyMissing -= OnDependencyMissing;
        ViewModel.WindowsConfigs.CollectionChanged -= WindowsConfigsChanged;
        PreviewFrameProvider.Dispose();
        PrefixesWindow.Close();
        BlacklistWindow.Close();
        SettingsWindow.Close();
        AppInfoWindow.Close();
        RenameWindow.Close();
        foreach (FloatingWindow floatingWindow in _floatingWindows.Values.ToList())
            floatingWindow.Close();
        _floatingWindows.Clear();
        ConfigFileAccessor.GetInstance().WriteUserSettings();
        ViewModel.Dispose();
        base.OnClosing(e);
    }

    private void OpenDataFolderClick(object? sender, RoutedEventArgs e)
    {
        Process.Start(
            new ProcessStartInfo { FileName = StaticData.DataFolder, UseShellExecute = true }
        );
    }

    private void OpenPrefixesWindowClick(object? sender, RoutedEventArgs e)
    {
        PrefixesWindow.Show();
    }

    private void OpenBlacklistWindowClick(object? sender, RoutedEventArgs e)
    {
        BlacklistWindow.Show();
    }

    private void OpenSettingsWindowClick(object? sender, RoutedEventArgs e)
    {
        SettingsWindow.RefreshPendingValues();
        SettingsWindow.Show();
    }

    private void OpenAppInfoWindowClick(object? sender, RoutedEventArgs e)
    {
        AppInfoWindow.Refresh();
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
        windowTitle = windowTitle.ToLowerInvariant();
        if (BlacklistWindow.HasPrefixStartingWith(windowTitle))
            return;

        _ = BlacklistWindow.TryAddPrefix(windowTitle);
    }

    public void AddToTempBlacklist(string windowId)
    {
        ViewModel.TempWindowIdsBlacklist.Add(windowId);
    }

    public void SetActivePreview(IFloatingPreviewWindow floatingWindow)
    {
        if (_activePreviewWindow == floatingWindow)
            return;

        _activePreviewWindow?.SetPreviewHighlight(false);
        _activePreviewWindow = floatingWindow;
        _activePreviewWindow.SetPreviewHighlight(true);
    }

    public void ClearActivePreview(IFloatingPreviewWindow floatingWindow)
    {
        if (_activePreviewWindow != floatingWindow)
            return;

        _activePreviewWindow.SetPreviewHighlight(false);
        _activePreviewWindow = null;
    }

    public async Task RenameWindowTitleAsync(string windowId)
    {
        if (!_floatingWindows.TryGetValue(windowId, out FloatingWindow? floatingWindow))
            return;

        bool isUpdated = await RenameWindow.ShowAndWaitForResultAsync(
            floatingWindow.WindowConfig.WindowTitle
        );
        if (!isUpdated)
            return;

        string renamedTitle = RenameWindow.NewWindowTitle;
        WinAccessorBase.RenameWindowTitle(windowId, renamedTitle);

        floatingWindow.UpdateWindowTitle(renamedTitle);
        WindowConfig? viewModelConfig = ViewModel.WindowsConfigs.FirstOrDefault(config =>
            string.Equals(config.WindowId, windowId, StringComparison.Ordinal)
        );
        if (viewModelConfig is not null)
            viewModelConfig.WindowTitle = renamedTitle;
    }

    private void ShowPreviouslyReportedDependencies()
    {
        foreach (string dependency in _dependencyNotificationService.GetReportedMissing())
            OnDependencyMissing(dependency);
    }

    private void OnDependencyMissing(string dependency)
    {
        if (StaticData.AppClosing)
            return;
        if (!_missingDependenciesShown.Add(dependency))
            return;

        Dispatcher.UIThread.Post(() => ShowDependencyMissingDialog(dependency));
    }

    private void ShowDependencyMissingDialog(string dependency)
    {
        var message = $"Missing dependency: {dependency}\nInstall it and restart the app.";

        var okButton = new Button
        {
            Content = "OK",
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var panel = new StackPanel { Margin = new Thickness(12), Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(okButton);

        var dialog = new Window
        {
            Title = "Missing dependency",
            CanResize = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel,
        };

        okButton.Click += (_, _) => dialog.Close();

        if (IsVisible)
            _ = dialog.ShowDialog(this);
        else
            dialog.Show();
    }

    private void InitializeFloatingWindows(IEnumerable<WindowConfig> windows)
    {
        foreach (WindowConfig window in windows)
        {
            if (_floatingWindows.ContainsKey(window.WindowId))
                continue;

            _floatingWindows[window.WindowId] = new FloatingWindow(
                window,
                WinAccessorBase,
                PreviewFrameProvider,
                this
            );
        }
    }

    private void WindowsConfigsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            CloseAllFloatingWindows();
            InitializeFloatingWindows(ViewModel.WindowsConfigs);
            return;
        }

        if (e.NewItems is not null)
        {
            foreach (WindowConfig window in e.NewItems.OfType<WindowConfig>())
            {
                if (_floatingWindows.ContainsKey(window.WindowId))
                    continue;

                _floatingWindows[window.WindowId] = new FloatingWindow(
                    window,
                    WinAccessorBase,
                    PreviewFrameProvider,
                    this
                );
            }
        }

        if (e.OldItems is not null)
        {
            foreach (WindowConfig window in e.OldItems.OfType<WindowConfig>())
                CloseFloatingWindow(window.WindowId);
        }
    }

    private void CloseAllFloatingWindows()
    {
        foreach (FloatingWindow window in _floatingWindows.Values.ToList())
            window.RequestCloseFromHost();
        _floatingWindows.Clear();
    }

    private void CloseFloatingWindow(string windowId)
    {
        if (!_floatingWindows.Remove(windowId, out FloatingWindow? window))
            return;

        window.RequestCloseFromHost();
    }

    private void BlacklistMenuItemClick(object? sender, RoutedEventArgs e)
    {
        AddToBlacklist(((string)((MenuItem)sender!).Tag!));
    }

    private void TempBlacklistMenuItemClick(object? sender, RoutedEventArgs e)
    {
        AddToTempBlacklist((string)((MenuItem)sender!).Tag!);
    }

    public void ApplySettings()
    {
        foreach (FloatingWindow floatingWindow in _floatingWindows.Values.ToList())
            floatingWindow.ApplySettings();
    }
}
