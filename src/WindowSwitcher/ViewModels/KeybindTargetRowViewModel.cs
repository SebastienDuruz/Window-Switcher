using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;

namespace WindowSwitcher.ViewModels;

/// <summary>
/// One row of the Keybinds table: a target, its shortcuts and its inline capture.
/// The owning <see cref="KeybindSettingsViewModel"/> performs every change; the row only
/// exposes commands and the state to display.
/// </summary>
public sealed partial class KeybindTargetRowViewModel : ObservableObject
{
    internal const string CapturePrompt = "Press the shortcut…";

    private readonly RelayCommand _beginCaptureCommand;
    private readonly RelayCommand _confirmCaptureCommand;

    [ObservableProperty]
    private bool _isCapturing;

    [ObservableProperty]
    private string _capturePreview = CapturePrompt;

    [ObservableProperty]
    private bool _hasCapturedCombination;

    internal KeybindTargetRowViewModel(
        KeybindTargetDescriptor target,
        IEnumerable<KeybindShortcutOption> shortcuts,
        Action<KeybindTargetRowViewModel> beginCapture,
        Action confirmCapture,
        Action cancelCapture,
        Action<KeybindTargetRowViewModel, KeybindShortcutOption> removeShortcut
    )
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(shortcuts);
        ArgumentNullException.ThrowIfNull(beginCapture);
        ArgumentNullException.ThrowIfNull(confirmCapture);
        ArgumentNullException.ThrowIfNull(cancelCapture);
        ArgumentNullException.ThrowIfNull(removeShortcut);

        Target = target;
        Shortcuts = new ObservableCollection<KeybindShortcutOption>(shortcuts);
        _beginCaptureCommand = new RelayCommand(() => beginCapture(this), () => !IsCapturing);
        _confirmCaptureCommand = new RelayCommand(confirmCapture, () => HasCapturedCombination);
        BeginCaptureCommand = _beginCaptureCommand;
        ConfirmCaptureCommand = _confirmCaptureCommand;
        CancelCaptureCommand = new RelayCommand(cancelCapture);
        RemoveShortcutCommand = new RelayCommand<KeybindShortcutOption>(option =>
        {
            if (option is not null)
                removeShortcut(this, option);
        });
    }

    public KeybindTargetDescriptor Target { get; }

    public string DisplayName => Target.DisplayName;

    public ObservableCollection<KeybindShortcutOption> Shortcuts { get; }

    public IRelayCommand BeginCaptureCommand { get; }
    public IRelayCommand ConfirmCaptureCommand { get; }
    public IRelayCommand CancelCaptureCommand { get; }

    /// <summary>
    /// Removes the shortcut passed as the command parameter.
    /// </summary>
    public IRelayCommand<KeybindShortcutOption> RemoveShortcutCommand { get; }

    internal void ReplaceShortcuts(IEnumerable<KeybindShortcutOption> shortcuts)
    {
        ArgumentNullException.ThrowIfNull(shortcuts);

        KeybindShortcutOption[] items = shortcuts.ToArray();
        Shortcuts.Clear();
        foreach (KeybindShortcutOption shortcut in items)
            Shortcuts.Add(shortcut);
    }

    partial void OnIsCapturingChanged(bool value)
    {
        _beginCaptureCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasCapturedCombinationChanged(bool value)
    {
        _confirmCaptureCommand.NotifyCanExecuteChanged();
    }
}
