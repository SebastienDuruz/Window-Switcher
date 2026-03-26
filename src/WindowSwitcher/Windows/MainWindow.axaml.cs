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
    private readonly FloatingWindowRegistry _floatingWindowRegistry;
    private FiltersWindow FiltersWindow { get; }
    private SettingsWindow SettingsWindow { get; }
    private KeybindsWindow KeybindsWindow { get; }
    private AppInfoWindow AppInfoWindow { get; }
    private RenameWindow RenameWindow { get; }
    private IFloatingPreviewWindow? _activePreviewWindow;
    private WindowListViewModel ViewModel { get; }
    private readonly IWindowKeybindActivator _windowKeybindActivator;
    private readonly HashSet<string> _missingDependencies = new(StringComparer.OrdinalIgnoreCase);
    private Window? _missingDependenciesDialog;
    private TextBlock? _missingDependenciesTextBlock;
    private bool _suppressWindowStateHandling;
    private bool _isHiddenToTray;

    public MainWindow()
    {
        InitializeComponent();

        PropertyChanged += OnWindowPropertyChanged;
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
        if (OperatingSystem.IsLinux())
        {
            LinuxDependencies.DependencyMissing += OnDependencyMissing;
            ShowPreviouslyReportedDependencies();
        }

        if (ConfigFileAccessor.GetInstance().ReadConfig(config => config.StartMinimized))
            Dispatcher.UIThread.Post(
                HideToTray,
                DispatcherPriority.Background
            );
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        StaticData.AppClosing = true;
        if (OperatingSystem.IsLinux())
            LinuxDependencies.DependencyMissing -= OnDependencyMissing;
        PropertyChanged -= OnWindowPropertyChanged;
        _windowKeybindActivator.WindowActivated -= OnWindowKeybindActivated;
        ViewModel.WindowsConfigs.CollectionChanged -= WindowsConfigsChanged;
        FiltersWindow.Close();
        SettingsWindow.Close();
        KeybindsWindow.Close();
        AppInfoWindow.Close();
        RenameWindow.Close();
        _missingDependenciesDialog?.Close();
        _floatingWindowRegistry.CloseAll();
        PreviewFrameProvider.Dispose();
        ConfigFileAccessor.GetInstance().WriteUserSettings();
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
        ConfigFileAccessor.GetInstance().ResetFloatingWindowSettings();
    }

    private void ResetUserSettings(object? sender, RoutedEventArgs e)
    {
        ConfigFileAccessor.GetInstance().ResetUserSettings();
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

    public void NotifyPreviewWindowActivated(string windowId)
    {
        if (string.IsNullOrWhiteSpace(windowId))
            return;

        _windowKeybindActivator.NotifyWindowActivated(windowId);
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
        IReadOnlyCollection<string> reportedMissing = LinuxDependencies.GetReportedMissing();
        if (reportedMissing.Count == 0)
            return;

        Dispatcher.UIThread.Post(() => RegisterMissingDependencies(reportedMissing));
    }

    private void OnDependencyMissing(string dependency)
    {
        if (StaticData.AppClosing)
            return;
        if (string.IsNullOrWhiteSpace(dependency))
            return;

        Dispatcher.UIThread.Post(() => RegisterMissingDependency(dependency));
    }

    private void RegisterMissingDependencies(IEnumerable<string> dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);

        bool hasChanges = false;
        foreach (string dependency in dependencies)
        {
            if (string.IsNullOrWhiteSpace(dependency))
                continue;

            hasChanges |= _missingDependencies.Add(dependency);
        }

        if (!hasChanges)
            return;

        ShowOrUpdateDependencyMissingDialog();
    }

    private void RegisterMissingDependency(string dependency)
    {
        if (StaticData.AppClosing)
            return;
        if (!_missingDependencies.Add(dependency))
            return;

        ShowOrUpdateDependencyMissingDialog();
    }

    private void ShowOrUpdateDependencyMissingDialog()
    {
        if (_missingDependencies.Count == 0)
            return;

        if (_missingDependenciesDialog is null || _missingDependenciesTextBlock is null)
            _missingDependenciesDialog = CreateDependencyMissingDialog();

        Window dialog =
            _missingDependenciesDialog
            ?? throw new InvalidOperationException("The dependency dialog was not created.");
        TextBlock textBlock =
            _missingDependenciesTextBlock
            ?? throw new InvalidOperationException("The dependency dialog content was not created.");

        dialog.Title = DependencyNotificationDialogContent.CreateTitle(_missingDependencies.Count);
        textBlock.Text = DependencyNotificationDialogContent.CreateMessage(_missingDependencies);

        if (dialog.IsVisible)
            return;

        if (IsVisible)
            _ = dialog.ShowDialog(this);
        else
            dialog.Show();
    }

    private Window CreateDependencyMissingDialog()
    {
        var okButton = new Button
        {
            Content = "OK",
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var panel = new StackPanel { Margin = new Thickness(12), Spacing = 10 };
        _missingDependenciesTextBlock = new TextBlock { TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(_missingDependenciesTextBlock);
        panel.Children.Add(okButton);

        var dialog = new Window
        {
            CanResize = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel,
        };

        okButton.Click += (_, _) => dialog.Close();
        dialog.Closed += OnMissingDependenciesDialogClosed;
        return dialog;
    }

    private void OnMissingDependenciesDialogClosed(object? sender, EventArgs e)
    {
        Window? dialog = _missingDependenciesDialog;
        if (dialog is null || !ReferenceEquals(sender, dialog))
            return;

        dialog.Closed -= OnMissingDependenciesDialogClosed;
        _missingDependenciesDialog = null;
        _missingDependenciesTextBlock = null;
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
