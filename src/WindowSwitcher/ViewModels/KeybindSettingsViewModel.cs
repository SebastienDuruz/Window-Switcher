using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Utilities;
using WindowSwitcher.Lib.Models;
using WindowSwitcher.Windows.Keybinds;

namespace WindowSwitcher.ViewModels;

public partial class KeybindSettingsViewModel : ObservableObject
{
    private readonly IWindowKeybindManager _keybindManager;
    private readonly IWindowKeybindTargetCatalogService _targetCatalogService;
    private readonly Func<IReadOnlyCollection<WindowConfig>> _selectedClientsProvider;
    private readonly RelayCommand _beginCaptureCommand;
    private readonly RelayCommand _confirmCaptureCommand;
    private readonly RelayCommand _cancelCaptureCommand;
    private readonly RelayCommand _removeShortcutCommand;
    private KeyCombination? _capturedCombination;

    [ObservableProperty]
    private ObservableCollection<KeybindTargetDescriptor> _actionTargets = [];

    [ObservableProperty]
    private ObservableCollection<KeybindTargetDescriptor> _clientTargets = [];

    [ObservableProperty]
    private KeybindTargetDescriptor? _selectedTarget;

    [ObservableProperty]
    private ObservableCollection<KeybindShortcutOption> _shortcuts = [];

    [ObservableProperty]
    private KeybindShortcutOption? _selectedShortcut;

    [ObservableProperty]
    private bool _isCapturing;

    [ObservableProperty]
    private string _capturePreview = "Press the shortcut to capture";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public IRelayCommand RefreshTargetsCommand { get; }
    public IRelayCommand BeginCaptureCommand { get; }
    public IRelayCommand ConfirmCaptureCommand { get; }
    public IRelayCommand CancelCaptureCommand { get; }
    public IRelayCommand RemoveShortcutCommand { get; }

    public bool CanConfirmCapture => IsCapturing && _capturedCombination is not null;

    public KeybindTargetDescriptor? SelectedActionTarget
    {
        get => SelectedTarget?.IsBuiltIn == true ? SelectedTarget : null;
        set
        {
            if (value is not null)
                SelectedTarget = value;
        }
    }

    public KeybindTargetDescriptor? SelectedClientTarget
    {
        get => SelectedTarget?.IsBuiltIn == false ? SelectedTarget : null;
        set
        {
            if (value is not null)
                SelectedTarget = value;
        }
    }

    public KeybindSettingsViewModel(
        IWindowKeybindManager keybindManager,
        IWindowKeybindTargetCatalogService targetCatalogService,
        Func<IReadOnlyCollection<WindowConfig>> selectedClientsProvider
    )
    {
        ArgumentNullException.ThrowIfNull(keybindManager);
        ArgumentNullException.ThrowIfNull(targetCatalogService);
        ArgumentNullException.ThrowIfNull(selectedClientsProvider);

        _keybindManager = keybindManager;
        _targetCatalogService = targetCatalogService;
        _selectedClientsProvider = selectedClientsProvider;

        RefreshTargetsCommand = new RelayCommand(RefreshTargets);
        _beginCaptureCommand = new RelayCommand(BeginCapture, CanBeginCapture);
        _confirmCaptureCommand = new RelayCommand(ConfirmCapture, () => CanConfirmCapture);
        _cancelCaptureCommand = new RelayCommand(CancelCapture, CanCancelCapture);
        _removeShortcutCommand = new RelayCommand(RemoveShortcut, CanRemoveShortcut);
        BeginCaptureCommand = _beginCaptureCommand;
        ConfirmCaptureCommand = _confirmCaptureCommand;
        CancelCaptureCommand = _cancelCaptureCommand;
        RemoveShortcutCommand = _removeShortcutCommand;

        RefreshTargets();
    }

    public void RefreshTargets()
    {
        string? previousTargetId = SelectedTarget?.TargetId;

        KeybindTargetCatalogSnapshot targetCatalog = _targetCatalogService.GetTargets(
            _selectedClientsProvider()
        );

        ActionTargets = new ObservableCollection<KeybindTargetDescriptor>(targetCatalog.ActionTargets);
        ClientTargets = new ObservableCollection<KeybindTargetDescriptor>(targetCatalog.ClientTargets);

        SelectTargetById(previousTargetId);
    }

    public bool TryCaptureKey(
        Key key,
        PhysicalKey physicalKey,
        string? keySymbol,
        KeyModifiers modifiers)
    {
        if (!IsCapturing)
            return false;

        if (
            !AvaloniaKeybindCaptureMapper.TryCreate(
                key,
                physicalKey,
                keySymbol,
                modifiers,
                out KeyCombination combination,
                out string message
            )
        )
        {
            _capturedCombination = null;
            CapturePreview = message;
            StatusMessage = message;
            UpdateCommandStates();
            return true;
        }

        _capturedCombination = KeyCombinationParser.Normalize(combination);
        CapturePreview = KeyCombinationParser.ToCanonicalString(_capturedCombination);
        StatusMessage = string.Empty;
        UpdateCommandStates();
        return true;
    }

    partial void OnSelectedShortcutChanged(KeybindShortcutOption? value)
    {
        UpdateCommandStates();
    }

    partial void OnIsCapturingChanged(bool value)
    {
        UpdateCommandStates();
    }

    partial void OnSelectedTargetChanged(KeybindTargetDescriptor? value)
    {
        OnPropertyChanged(nameof(SelectedActionTarget));
        OnPropertyChanged(nameof(SelectedClientTarget));
        LoadShortcutsForSelectedTarget();
        StatusMessage = string.Empty;
        CancelCapture();
        UpdateCommandStates();
    }

    private void SelectTargetById(string? targetId)
    {
        SelectedTarget =
            ActionTargets.FirstOrDefault(target =>
                string.Equals(target.TargetId, targetId, StringComparison.Ordinal)
            )
            ?? ClientTargets.FirstOrDefault(target =>
                string.Equals(target.TargetId, targetId, StringComparison.Ordinal)
            );
    }

    private void BeginCapture()
    {
        KeybindTargetDescriptor? selectedTarget = SelectedTarget;
        if (selectedTarget is null)
        {
            StatusMessage = "Select an action or a client target first.";
            return;
        }

        _capturedCombination = null;
        IsCapturing = true;
        CapturePreview = "Press the shortcut to capture";
        StatusMessage = string.Empty;
    }

    private void ConfirmCapture()
    {
        KeybindTargetDescriptor? selectedTarget = SelectedTarget;
        if (selectedTarget is null || _capturedCombination is null)
            return;

        KeybindShortcutAddResult result = _keybindManager.AddShortcut(
            selectedTarget.TargetId,
            selectedTarget.DisplayName,
            _capturedCombination
        );
        if (!result.Succeeded)
        {
            StatusMessage = result.Message;
            return;
        }

        LoadShortcutsForSelectedTarget();
        CancelCapture();
        StatusMessage = "Shortcut added.";
    }

    private void CancelCapture()
    {
        _capturedCombination = null;
        IsCapturing = false;
        CapturePreview = "Press the shortcut to capture";
        UpdateCommandStates();
    }

    private void RemoveShortcut()
    {
        KeybindTargetDescriptor? selectedTarget = SelectedTarget;
        if (selectedTarget is null || SelectedShortcut is null)
            return;

        bool removed = _keybindManager.RemoveShortcut(
            selectedTarget.TargetId,
            SelectedShortcut.Combination
        );
        if (!removed)
        {
            StatusMessage = "Unable to remove this shortcut.";
            return;
        }

        LoadShortcutsForSelectedTarget();
        StatusMessage = "Shortcut removed.";
    }

    private void LoadShortcutsForSelectedTarget()
    {
        KeybindTargetDescriptor? selectedTarget = SelectedTarget;
        if (selectedTarget is null)
        {
            Shortcuts = [];
            SelectedShortcut = null;
            return;
        }

        Shortcuts = new ObservableCollection<KeybindShortcutOption>(
            _keybindManager
                .GetShortcutsForTarget(selectedTarget.TargetId)
                .OrderBy(shortcut => KeyCombinationParser.ToCanonicalString(shortcut.Combination))
                .Select(shortcut => new KeybindShortcutOption(shortcut))
        );
        SelectedShortcut = null;
    }

    private bool CanBeginCapture()
    {
        return SelectedTarget is not null && !IsCapturing;
    }

    private bool CanCancelCapture()
    {
        return IsCapturing;
    }

    private bool CanRemoveShortcut()
    {
        return SelectedTarget is not null && SelectedShortcut is not null;
    }

    private void UpdateCommandStates()
    {
        OnPropertyChanged(nameof(CanConfirmCapture));
        _beginCaptureCommand.NotifyCanExecuteChanged();
        _confirmCaptureCommand.NotifyCanExecuteChanged();
        _cancelCaptureCommand.NotifyCanExecuteChanged();
        _removeShortcutCommand.NotifyCanExecuteChanged();
    }
}

public sealed class KeybindShortcutOption
{
    public KeybindShortcutOption(WindowKeybindShortcut shortcut)
    {
        ArgumentNullException.ThrowIfNull(shortcut);

        Combination = shortcut.Combination.Clone();
        Display = KeyCombinationParser.ToCanonicalString(shortcut.Combination);
    }

    public KeyCombination Combination { get; }

    public string Display { get; }
}
