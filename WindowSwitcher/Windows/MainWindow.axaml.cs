using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using WindowSwitcher.ViewModels;
using WindowSwitcherLib.Data;
using WindowSwitcherLib.Data.Commands;
using WindowSwitcherLib.Data.FileAccess;
using WindowSwitcherLib.Data.WindowAccess;
using WindowSwitcherLib.Data.WindowAccess.Accessors;
using WindowSwitcherLib.Data.WindowAccess.PreviewFrames;
using WindowConfig = WindowSwitcherLib.Models.WindowConfig;

namespace WindowSwitcher.Windows;

public partial class MainWindow : Window
{
    private WinAccessorBase WinAccessorBase { get; } = WinFactories.GetAccessor();
    private IPreviewFrameProvider PreviewFrameProvider { get; }
    private static List<FloatingWindow> FloatingWindows { get; } = new();
    private PrefixesWindow PrefixesWindow { get; }
    private PrefixesWindow BlacklistWindow { get; }
    private SettingsWindow SettingsWindow { get; }
    private AppInfoWindow AppInfoWindow { get; }
    private RenameWindow RenameWindow { get; }
    private static bool RefreshButtonEnabled { get; set; } = true;
    private FloatingWindow? _activePreviewWindow;
    private WindowListViewModel ViewModel { get; }
    private readonly HashSet<string> _missingDependenciesShown = new(StringComparer.OrdinalIgnoreCase);
    
    public MainWindow()
    {
        InitializeComponent();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            LinuxDependencies.DependencyMissing += OnDependencyMissing;

        PreviewFrameProvider = PreviewProviderFactory.Create(WinAccessorBase);

        ViewModel = new WindowListViewModel(WinAccessorBase);
        DataContext = ViewModel;
        Title = StaticData.AppName;

        PrefixesWindow = new PrefixesWindow(ConfigFileAccessor.GetInstance().ReadConfig(config => config.WhitelistPrefixes.ToList()),
            StaticData.PrefixWindowType.whitelist, "Prefixes");
        BlacklistWindow = new PrefixesWindow(ConfigFileAccessor.GetInstance().ReadConfig(config => config.BlacklistPrefixes.ToList()),
            StaticData.PrefixWindowType.blacklist, "Blacklist");
        SettingsWindow = new SettingsWindow(ApplySettings);
        AppInfoWindow = new AppInfoWindow { Title = $"About {StaticData.AppName}" };
        RenameWindow = new RenameWindow();

        ViewModel.WindowsConfigs.CollectionChanged += WindowsConfigsChanged;
        InitializeFloatingWindows(ViewModel.WindowsConfigs);
        ShowPreviouslyReportedDependencies();

        if (ConfigFileAccessor.GetInstance().ReadConfig(config => config.StartMinimized))
            Dispatcher.UIThread.Post(() =>
            {
                this.WindowState = WindowState.Minimized;
            }, DispatcherPriority.Background);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        StaticData.AppClosing = true;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            LinuxDependencies.DependencyMissing -= OnDependencyMissing;
        ViewModel.WindowsConfigs.CollectionChanged -= WindowsConfigsChanged;
        PreviewFrameProvider.Dispose();
        PrefixesWindow.Close();
        BlacklistWindow.Close();
        SettingsWindow.Close();
        AppInfoWindow.Close();
        RenameWindow.Close();
        foreach(FloatingWindow floatingWindow in FloatingWindows)
            floatingWindow.Close();
        ConfigFileAccessor.GetInstance().WriteUserSettings();
        ViewModel.Dispose();
        base.OnClosing(e);
    }

    private void OpenDataFolderClick(object? sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = StaticData.DataFolder,
            UseShellExecute = true
        });
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
        if (RefreshButtonEnabled)
        {
            windowTitle = windowTitle.ToLower();
            if (!BlacklistWindow.ListToEdit.Any(x => x.StartsWith(windowTitle)))
            {
                BlacklistWindow.ListToEdit.Add(windowTitle);
                BlacklistWindow.AddPrefixToList(windowTitle);
            
                ConfigFileAccessor.GetInstance().SaveBlacklist(BlacklistWindow.ListToEdit);
            }
        }
    }

    public void AddToTempBlacklist(string windowId)
    {
        if (RefreshButtonEnabled)
            if (ViewModel.TempWindowIdsBlacklist.All(x => x != windowId))
                ViewModel.TempWindowIdsBlacklist.Add(windowId);
    }

    public void SetActivePreview(FloatingWindow floatingWindow)
    {
        if (_activePreviewWindow == floatingWindow)
            return;

        _activePreviewWindow?.SetPreviewHighlight(false);
        _activePreviewWindow = floatingWindow;
        _activePreviewWindow.SetPreviewHighlight(true);
    }

    public void ClearActivePreview(FloatingWindow floatingWindow)
    {
        if (_activePreviewWindow != floatingWindow)
            return;

        _activePreviewWindow.SetPreviewHighlight(false);
        _activePreviewWindow = null;
    }

    /// <summary>
    /// TODO : Optimise this method for better user experience (lag in some cases)
    /// </summary>
    /// <param name="windowId"></param>
    /// <returns></returns>
    public async Task RenameWindowTitle(string windowId)
    {
        RenameWindow.Show();
        while (RenameWindow.IsVisible)
            await Task.Delay(500);

        if (RenameWindow.IsUpdated)
        {
            RenameWindow.IsUpdated = false;
            WinAccessorBase.RenameWindowTitle(windowId, RenameWindow.NewWindowTitle);
            await Task.Delay(500); // Give time to windowTitle to be updated
            FloatingWindow window = FloatingWindows.First(x => x.WindowConfig!.WindowId == windowId);
            FloatingWindows.Remove(window);
            StaticData.AppClosing = true;
            window.Close();
            StaticData.AppClosing = false;
        }
    }

    private void ShowPreviouslyReportedDependencies()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        foreach (string dependency in LinuxDependencies.GetReportedMissing())
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
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var panel = new StackPanel
        {
            Margin = new Thickness(12),
            Spacing = 10
        };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(okButton);

        var dialog = new Window
        {
            Title = "Missing dependency",
            CanResize = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel
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
            if (FloatingWindows.All(x => x.WindowConfig!.WindowId != window.WindowId))
                FloatingWindows.Add(new FloatingWindow(window, WinAccessorBase, PreviewFrameProvider, this));
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
                if (FloatingWindows.All(x => x.WindowConfig!.WindowId != window.WindowId))
                    FloatingWindows.Add(new FloatingWindow(window, WinAccessorBase, PreviewFrameProvider, this));
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
        StaticData.AppClosing = true;
        foreach (FloatingWindow window in FloatingWindows.ToList())
        {
            window.Close();
            FloatingWindows.Remove(window);
        }
        StaticData.AppClosing = false;
    }

    private void CloseFloatingWindow(string windowId)
    {
        FloatingWindow? window = FloatingWindows.FirstOrDefault(x => x.WindowConfig?.WindowId == windowId);
        if (window is null)
            return;

        StaticData.AppClosing = true;
        window.Close();
        StaticData.AppClosing = false;
        FloatingWindows.Remove(window);
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
        ViewModel.WindowsConfigs.Clear();
        ViewModel.FetchWindowsWithFilters();
    }
}
