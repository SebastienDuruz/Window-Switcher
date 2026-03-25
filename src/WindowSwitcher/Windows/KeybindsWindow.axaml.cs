using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using WindowSwitcher.Hosting;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;
using WindowSwitcher.Lib.Models;
using WindowSwitcher.ViewModels;
using WindowSwitcher.Windows.Services;

namespace WindowSwitcher.Windows;

public partial class KeybindsWindow : Window
{
    private readonly UtilityWindowService _windowService = new();
    private readonly KeybindSettingsViewModel _viewModel;

    public KeybindsWindow()
        : this(() => Array.Empty<WindowConfig>()) { }

    public KeybindsWindow(Func<IReadOnlyCollection<WindowConfig>> selectedClientsProvider)
    {
        ArgumentNullException.ThrowIfNull(selectedClientsProvider);

        InitializeComponent();

        IWindowKeybindManager keybindManager =
            AppServiceProvider.GetRequiredService<IWindowKeybindManager>();
        IWindowKeybindTargetCatalogService targetCatalogService =
            AppServiceProvider.GetRequiredService<IWindowKeybindTargetCatalogService>();
        IGlobalKeyboardStartupStatusService globalKeyboardStartupStatusService =
            AppServiceProvider.GetRequiredService<IGlobalKeyboardStartupStatusService>();

        _viewModel = new KeybindSettingsViewModel(
            keybindManager,
            targetCatalogService,
            globalKeyboardStartupStatusService,
            selectedClientsProvider
        );
        DataContext = _viewModel;

        Closing += OnClosing;
        Opened += (_, _) => _viewModel.RefreshTargets();
    }

    public void RefreshData()
    {
        _viewModel.RefreshTargets();
    }

    private void WindowKeyDown(object? sender, KeyEventArgs e)
    {
        bool handled = _viewModel.TryCaptureKey(e.Key, e.PhysicalKey, e.KeySymbol, e.KeyModifiers);
        if (handled)
            e.Handled = true;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _windowService.HandleClosing(this, e, hideWhenCanceled: true, hideWhenAllowed: true);
    }
}
