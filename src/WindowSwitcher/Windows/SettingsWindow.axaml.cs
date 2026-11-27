using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using WindowSwitcher.ViewModels;
using WindowSwitcher.Windows.Keybinds;
using WindowSwitcher.Windows.Services;

namespace WindowSwitcher.Windows;

/// <summary>
/// Single window for every configuration screen: General, Previews, Filters, Keybinds
/// and About, reached through a sidebar.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly UtilityWindowService _windowLifecycle = new();
    private readonly SettingsWindowViewModel _viewModel;
    private readonly KeybindSettingsViewModel _keybinds;
    private readonly AppInfoViewModel _about;

    internal SettingsWindow(
        SettingsViewModel settings,
        FiltersViewModel filters,
        KeybindSettingsViewModel keybinds,
        AppInfoViewModel about,
        SettingsResetActions resetActions
    )
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentNullException.ThrowIfNull(keybinds);
        ArgumentNullException.ThrowIfNull(about);
        ArgumentNullException.ThrowIfNull(resetActions);

        InitializeComponent();

        _keybinds = keybinds;
        _about = about;
        _viewModel = new SettingsWindowViewModel(
            settings,
            resetActions.ResetLayout,
            resetActions.ResetSettings,
            resetActions.ResetAllPreviews,
            ConfirmAsync,
            OnPageShown
        );
        DataContext = _viewModel;
        FiltersPage.DataContext = filters;
        KeybindsPage.DataContext = keybinds;
        AboutPage.DataContext = about;

        Closing += OnClosing;
    }

    /// <summary>
    /// Shows the window on <paramref name="page"/>, owned by <paramref name="owner"/> when
    /// the owner is visible.
    /// </summary>
    public void ShowPage(SettingsPage page, Window? owner = null)
    {
        _viewModel.ShowPage(page);
        ShowWindow(owner);
    }

    /// <summary>
    /// Shows the window on the last page used.
    /// </summary>
    public void ShowLastPage(Window? owner = null)
    {
        ShowPage(_viewModel.SelectedPage, owner);
    }

    public Task<bool> CheckForUpdatesAsync(
        bool force,
        bool showUpToDateMessage,
        CancellationToken cancellationToken = default
    )
    {
        return _about.CheckForUpdatesAsync(force, showUpToDateMessage, cancellationToken);
    }

    private void ShowWindow(Window? owner)
    {
        if (IsVisible)
        {
            Activate();
            return;
        }

        if (owner is { IsVisible: true })
        {
            Show(owner);
            return;
        }

        Show();
    }

    private void OnPageShown(SettingsPage page)
    {
        switch (page)
        {
            case SettingsPage.Keybinds:
                _keybinds.RefreshTargets();
                break;
            case SettingsPage.About:
                _about.Refresh();
                _ = _about.CheckForUpdatesAsync(force: false, showUpToDateMessage: false);
                break;
        }
    }

    private Task<bool> ConfirmAsync(ConfirmationRequest request)
    {
        return ConfirmationWindow.ShowAsync(this, request);
    }

    private void WindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel.SelectedPage != SettingsPage.Keybinds)
            return;

        KeybindCaptureResult captureResult = AvaloniaKeybindCaptureMapper.Create(
            e.Key,
            e.PhysicalKey,
            e.KeySymbol,
            e.KeyModifiers
        );
        if (_keybinds.TryCaptureKey(captureResult))
            e.Handled = true;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _windowLifecycle.HandleClosing(this, e, hideWhenCanceled: true, hideWhenAllowed: true);
    }
}
