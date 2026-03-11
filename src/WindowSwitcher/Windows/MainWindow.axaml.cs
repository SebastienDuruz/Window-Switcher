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
using WindowSwitcher.Lib.Data;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;
using WindowSwitcher.Lib.Data.Platform.SystemInfo.Abstractions;
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
    private readonly FloatingWindowRegistry _floatingWindowRegistry;
    private FiltersWindow FiltersWindow { get; }
    private SettingsWindow SettingsWindow { get; }
    private KeybindsWindow KeybindsWindow { get; }
    private AppInfoWindow AppInfoWindow { get; }
    private RenameWindow RenameWindow { get; }
    private IFloatingPreviewWindow? _activePreviewWindow;
    private WindowListViewModel ViewModel { get; }
    private readonly IDependencyNotificationService _dependencyNotificationService;
    private readonly IWindowKeybindActivator _windowKeybindActivator;
    private readonly HashSet<string> _missingDependenciesShown = new(
        StringComparer.OrdinalIgnoreCase
    );

    public MainWindow()
    {
        InitializeComponent();
        _dependencyNotificationService =
            AppServiceProvider.GetRequiredService<IDependencyNotificationService>();
        _dependencyNotificationService.DependencyMissing += OnDependencyMissing;
        _windowKeybindActivator = AppServiceProvider.GetRequiredService<IWindowKeybindActivator>();
        _windowKeybindActivator.WindowActivated += OnWindowKeybindActivated;

        PreviewFrameProvider = PreviewFactory.Create(WinAccessorBase);
        _floatingWindowRegistry = new FloatingWindowRegistry(WinAccessorBase, PreviewFrameProvider, this);

        ViewModel = new WindowListViewModel(WinAccessorBase);
        DataContext = ViewModel;
        Title = StaticData.AppName;

        FiltersWindow = new FiltersWindow(
            ConfigFileAccessor
                .GetInstance()
                .ReadConfig(config => config.WhitelistPrefixes.ToList()),
            ConfigFileAccessor
                .GetInstance()
                .ReadConfig(config => config.BlacklistPrefixes.ToList())
        );
        SettingsWindow = new SettingsWindow(ApplySettings);
        KeybindsWindow = new KeybindsWindow(() => ViewModel.WindowsConfigs.ToArray());
        AppInfoWindow = new AppInfoWindow { Title = $"About {StaticData.AppName}" };
        RenameWindow = new RenameWindow();

        ViewModel.WindowsConfigs.CollectionChanged += WindowsConfigsChanged;
        _floatingWindowRegistry.Initialize(ViewModel.WindowsConfigs);
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
        _windowKeybindActivator.WindowActivated -= OnWindowKeybindActivated;
        ViewModel.WindowsConfigs.CollectionChanged -= WindowsConfigsChanged;
        FiltersWindow.Close();
        SettingsWindow.Close();
        KeybindsWindow.Close();
        AppInfoWindow.Close();
        RenameWindow.Close();
        _floatingWindowRegistry.CloseAll();
        PreviewFrameProvider.Dispose();
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
        if (FiltersWindow.HasBlacklistPrefixStartingWith(windowTitle))
            return;

        _ = FiltersWindow.TryAddBlacklistPrefix(windowTitle);
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

    private void OnWindowKeybindActivated(object? sender, string windowId)
    {
        if (string.IsNullOrWhiteSpace(windowId))
            return;

        Dispatcher.UIThread.Post(() => SetActivePreviewByWindowId(windowId));
    }

    private void SetActivePreviewByWindowId(string windowId)
    {
        if (!_floatingWindowRegistry.TryGet(windowId, out FloatingWindow floatingWindow))
            return;

        SetActivePreview(floatingWindow);
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
        WinAccessorBase.RenameWindowTitle(windowId, renamedTitle);

        windowConfig.WindowTitle = renamedTitle;

        if (_floatingWindowRegistry.TryGet(windowId, out FloatingWindow floatingWindow))
            floatingWindow.UpdateWindowTitle(renamedTitle);
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
}
