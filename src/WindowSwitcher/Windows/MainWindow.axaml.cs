using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Serilog;
using WindowSwitcher.Controls;
using WindowSwitcher.Hosting;
using WindowSwitcher.Lib.Data;
using WindowSwitcher.Lib.Data.Platform.Commands.Dependencies;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;
using WindowSwitcher.Lib.Data.Platform.SystemInfo.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcher.Lib.Data.Updates.Abstractions;
using WindowSwitcher.Theming;
using WindowSwitcher.ViewModels;
using WindowSwitcher.ViewModels.Abstractions;
using WindowSwitcher.Windows.Abstractions;
using WindowSwitcher.Windows.Services;
using WindowConfig = WindowSwitcher.Lib.Models.WindowConfig;

namespace WindowSwitcher.Windows;

public partial class MainWindow : Window, IFloatingWindowHost
{
    private static readonly ILogger Logger = Log.ForContext<MainWindow>();

    private WinAccessorBase WinAccessorBase { get; }
    private IPreviewFrameProvider PreviewFrameProvider { get; }
    private readonly IPreviewSelectionReset? _previewSelectionReset;
    private readonly FloatingWindowRegistry _floatingWindowRegistry;
    private SettingsWindow SettingsWindow { get; }
    private FiltersViewModel Filters { get; }
    private RenameWindow RenameWindow { get; }
    private WindowListViewModel ViewModel { get; }
    private readonly MainWindowViewModel _mainWindowViewModel;
    private readonly IWindowKeybindActivator _windowKeybindActivator;
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

        WinAccessorBase = AppServiceProvider.GetRequiredService<WinAccessorBase>();
        PreviewFrameProvider = AppServiceProvider.GetRequiredService<IPreviewFrameProvider>();
        _previewSelectionReset = PreviewFrameProvider as IPreviewSelectionReset;
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
        Filters = new FiltersViewModel(
            new PrefixListViewModel(
                new PrefixListService(
                    _configurationService.GetWhitelistPrefixes().ToList(),
                    StaticData.PrefixWindowType.whitelist
                )
            ),
            new PrefixListViewModel(
                new PrefixListService(
                    _configurationService.GetBlacklistPrefixes().ToList(),
                    StaticData.PrefixWindowType.blacklist
                )
            )
        );
        SettingsWindow = CreateSettingsWindow(dispatcher);
        RenameWindow = new RenameWindow();
        _mainWindowViewModel = new MainWindowViewModel(
            ViewModel,
            openSettings: () => SettingsWindow.ShowLastPage(this),
            openFilters: () => SettingsWindow.ShowPage(SettingsPage.Filters, this),
            openConfigFolder: () => OpenFolder(StaticData.DataFolder),
            openLogsFolder: () => OpenFolder(StaticData.LogFolder),
            addToBlacklist: value => AddToBlacklist(value ?? string.Empty),
            addToTemporaryBlacklist: value => AddToTempBlacklist(value ?? string.Empty),
            renameWindow: windowId => RenameWindowTitleAsync(windowId ?? string.Empty),
            bringPreviewHere: windowId => BringPreviewHere(windowId ?? string.Empty),
            activateWindow: windowId => ActivateWindowAsync(windowId ?? string.Empty)
        );
        DataContext = _mainWindowViewModel;
        Title = StaticData.AppName;
        WindowsList.ContainerPrepared += OnWindowListContainerPrepared;

        _windowKeybindActivator = AppServiceProvider.GetRequiredService<IWindowKeybindActivator>();
        _previewCoordinator = new FloatingPreviewCoordinator(
            _windowKeybindActivator,
            dispatcher,
            SetActivePreviewByWindowId,
            () => ConfigFileAccessor.GetInstance().ReadConfig(config => config.HideActivePreview)
        );
        _previewCoordinator.ActivePreviewChanged += OnActivePreviewChanged;
        _blacklistCoordinator = new WindowBlacklistCoordinator(
            Filters.Blacklist,
            ViewModel.TempWindowIdsBlacklist
        );
        _missingDependencyNotificationService = new MissingDependencyNotificationService(this);
        _startupUpdateNotificationService = new StartupUpdateNotificationService(
            this,
            SettingsWindow
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
            Dispatcher.UIThread.Post(HideToTray, DispatcherPriority.Background);
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
        _previewCoordinator.ActivePreviewChanged -= OnActivePreviewChanged;
        _previewCoordinator.Dispose();
        ViewModel.WindowsConfigs.CollectionChanged -= WindowsConfigsChanged;
        WindowsList.ContainerPrepared -= OnWindowListContainerPrepared;
        SettingsWindow.Close();
        RenameWindow.Close();
        _missingDependencyNotificationService.Dispose();
        _floatingWindowRegistry.CloseAll();
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

    private SettingsWindow CreateSettingsWindow(IViewModelDispatcher dispatcher)
    {
        var settings = new SettingsViewModel(
            new ConfigFileSettingsRepository(),
            ApplySettings,
            ApplyPreviewHighlightColor
        );
        IWindowKeybindManager keybindManager =
            AppServiceProvider.GetRequiredService<IWindowKeybindManager>();
        var keybinds = new KeybindSettingsViewModel(
            keybindManager,
            AppServiceProvider.GetRequiredService<IWindowKeybindTargetCatalogService>(),
            AppServiceProvider.GetRequiredService<IGlobalKeyboardStartupStatusService>(),
            dispatcher,
            () => ViewModel.WindowsConfigs.ToArray()
        );
        var about = new AppInfoViewModel(
            AppServiceProvider.GetRequiredService<IPlatformAppInfoProvider>(),
            AppServiceProvider.GetRequiredService<IAppUpdateService>(),
            RequestApplicationShutdown
        );
        Action? resetAllPreviews = _previewSelectionReset is null
            ? null
            : _previewSelectionReset.ResetAllSelections;

        return new SettingsWindow(
            settings,
            Filters,
            keybinds,
            about,
            new SettingsResetActions(
                ResetPreviewLayout,
                () => ResetUserSettings(keybindManager),
                resetAllPreviews
            )
        );
    }

    private void ResetPreviewLayout()
    {
        _configurationService.ResetFloatingWindowSettings();
        _floatingWindowRegistry.ResetLayout();
    }

    /// <summary>
    /// Restores the default configuration, then reloads the state that was read from the
    /// previous configuration: preview layout, filter lists and the global keybind catalog. The settings
    /// themselves are reloaded by the settings view model.
    /// </summary>
    private void ResetUserSettings(IWindowKeybindManager keybindManager)
    {
        _configurationService.ResetUserSettings();
        _floatingWindowRegistry.ResetLayout();
        Filters.Whitelist.Reload(_configurationService.GetWhitelistPrefixes());
        Filters.Blacklist.Reload(_configurationService.GetBlacklistPrefixes());
        keybindManager.NotifyConfigurationReplaced();
    }

    private static void ApplyPreviewHighlightColor(string colorValue)
    {
        if (Color.TryParse(colorValue, out Color color))
            HighlightColorApplier.Apply(color);
    }

    private void OnWindowListContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        // Containers are recycled: the menu reads the window from the container when an
        // item is clicked, so one menu per container is enough.
        if (e.Container.ContextMenu is null)
            e.Container.ContextMenu = CreateWindowListContextMenu(e.Container);
    }

    private ContextMenu CreateWindowListContextMenu(Control container)
    {
        var menu = new ContextMenu();
        MenuItem bringPreviewHere = CreateWindowListMenuItem(
            "Bring preview here",
            container,
            window => _mainWindowViewModel.BringPreviewHereCommand.Execute(window.WindowId)
        );
        var separator = new Separator();
        menu.Items.Add(bringPreviewHere);
        menu.Items.Add(separator);
        // Without floating previews there is no preview to bring back.
        menu.Opening += (_, _) =>
        {
            bool previewsShown = ConfigFileAccessor
                .GetInstance()
                .ReadConfig(config => config.ShowFloatingWindows);
            bringPreviewHere.IsVisible = previewsShown;
            separator.IsVisible = previewsShown;
        };
        menu.Items.Add(
            CreateWindowListMenuItem(
                "Add to blacklist",
                container,
                window => _mainWindowViewModel.AddToBlacklistCommand.Execute(window.WindowTitle)
            )
        );
        menu.Items.Add(
            CreateWindowListMenuItem(
                "Add to temp blacklist",
                container,
                window =>
                    _mainWindowViewModel.AddToTemporaryBlacklistCommand.Execute(window.WindowId)
            )
        );
        menu.Items.Add(
            CreateWindowListMenuItem(
                "Rename window",
                container,
                window => _mainWindowViewModel.RenameWindowCommand.Execute(window.WindowId)
            )
        );
        return menu;
    }

    /// <summary>
    /// Activates the window from the main list, the same way as a click on its preview.
    /// </summary>
    private Task ActivateWindowAsync(string windowId)
    {
        return _floatingWindowRegistry.TryGet(windowId, out FloatingWindow floatingWindow)
            ? floatingWindow.ActivateFromHostAsync()
            : Task.CompletedTask;
    }

    private void BringPreviewHere(string windowId)
    {
        if (!_floatingWindowRegistry.TryGet(windowId, out FloatingWindow floatingWindow))
            return;

        _previewCoordinator.RevealPreview(floatingWindow);
        Size frameSize = FrameSize ?? ClientSize;
        floatingWindow.MoveNextTo(
            new PixelRect(Position, PixelSize.FromSize(frameSize, DesktopScaling))
        );
    }

    private static MenuItem CreateWindowListMenuItem(
        string header,
        Control container,
        Action<WindowConfig> execute
    )
    {
        return new MenuItem
        {
            Header = header,
            Command = new ContextMenuCommand(() =>
            {
                if (container.DataContext is WindowConfig window)
                    execute(window);
            }),
        };
    }

    private static void OpenFolder(string folder)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Folder {Folder} could not be opened", folder);
        }
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

    private void OnSelectionPromptChanged(object? sender, PreviewSelectionPromptEventArgs eventArgs)
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

    private void OnActivePreviewChanged(object? sender, IFloatingPreviewWindow? preview)
    {
        ViewModel.ActiveWindowId = (preview as FloatingWindow)?.WindowConfig.WindowId;
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

        // The preview holds the saved settings of the window, original title included.
        FloatingWindow? floatingWindow = _floatingWindowRegistry.TryGet(
            windowId,
            out FloatingWindow preview
        )
            ? preview
            : null;
        string? storedOriginal = floatingWindow?.WindowConfig.OriginalWindowTitle;
        string titleBeforeRename = windowConfig.WindowTitle;

        bool isUpdated = await RenameWindow.ShowAndWaitForResultAsync(
            titleBeforeRename,
            storedOriginal
        );
        if (!isUpdated)
            return;

        string renamedTitle = RenameWindow.NewWindowTitle;
        bool renamed = await WinAccessorBase.TryRenameWindowAsync(windowId, renamedTitle);
        if (!renamed)
            return;

        string originalTitle = OriginalTitleTracker.Resolve(
            titleBeforeRename,
            storedOriginal,
            renamedTitle
        );
        windowConfig.WindowTitle = renamedTitle;
        windowConfig.OriginalWindowTitle = originalTitle;
        floatingWindow?.UpdateWindowTitle(renamedTitle, originalTitle);
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

    public void ApplySettings()
    {
        _floatingWindowRegistry.ApplySettings();
        _previewCoordinator.ApplySettings();
    }

    private void RequestApplicationShutdown()
    {
        if (StaticData.AppClosing)
            return;

        Close();
    }
}
