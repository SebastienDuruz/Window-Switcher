using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Utilities;
using WindowSwitcher.Lib.Models;
using WindowSwitcher.ViewModels.Abstractions;

namespace WindowSwitcher.ViewModels;

/// <summary>
/// Backs the Keybinds page: one table row per target (built-in actions, then client
/// windows) with its shortcuts, an inline capture to add a shortcut, and a status line.
/// </summary>
public partial class KeybindSettingsViewModel : ObservableObject
{
    private readonly IWindowKeybindManager _keybindManager;
    private readonly IWindowKeybindTargetCatalogService _targetCatalogService;
    private readonly IGlobalKeyboardStartupStatusService _globalKeyboardStartupStatusService;
    private readonly IViewModelDispatcher _dispatcher;
    private readonly Func<IReadOnlyCollection<WindowConfig>> _selectedClientsProvider;
    private KeybindTargetRowViewModel? _capturingRow;
    private KeyCombination? _capturedCombination;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasClientRows))]
    private ObservableCollection<KeybindTargetRowViewModel> _actionRows = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasClientRows))]
    private ObservableCollection<KeybindTargetRowViewModel> _clientRows = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStatusSuccess))]
    [NotifyPropertyChangedFor(nameof(IsStatusError))]
    private KeybindStatusKind _statusKind;

    [ObservableProperty]
    private bool _showGlobalKeyboardSetupBanner;

    [ObservableProperty]
    private string _globalKeyboardSetupTitle = string.Empty;

    [ObservableProperty]
    private string _globalKeyboardSetupMessage = string.Empty;

    [ObservableProperty]
    private string _globalKeyboardSetupCommand = string.Empty;

    [ObservableProperty]
    private bool _showGlobalKeyboardSetupCommand;

    [ObservableProperty]
    private string _globalKeyboardSetupGuidance = string.Empty;

    public IRelayCommand RefreshTargetsCommand { get; }

    public bool HasClientRows => ClientRows.Count > 0;

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    public bool IsStatusSuccess => StatusKind == KeybindStatusKind.Success;

    public bool IsStatusError => StatusKind == KeybindStatusKind.Error;

    /// <summary>
    /// Gets the row waiting for a shortcut, if any.
    /// </summary>
    public KeybindTargetRowViewModel? CapturingRow => _capturingRow;

    public KeybindSettingsViewModel(
        IWindowKeybindManager keybindManager,
        IWindowKeybindTargetCatalogService targetCatalogService,
        IGlobalKeyboardStartupStatusService globalKeyboardStartupStatusService,
        IViewModelDispatcher dispatcher,
        Func<IReadOnlyCollection<WindowConfig>> selectedClientsProvider
    )
    {
        ArgumentNullException.ThrowIfNull(keybindManager);
        ArgumentNullException.ThrowIfNull(targetCatalogService);
        ArgumentNullException.ThrowIfNull(globalKeyboardStartupStatusService);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(selectedClientsProvider);

        _keybindManager = keybindManager;
        _targetCatalogService = targetCatalogService;
        _globalKeyboardStartupStatusService = globalKeyboardStartupStatusService;
        _dispatcher = dispatcher;
        _selectedClientsProvider = selectedClientsProvider;

        RefreshTargetsCommand = new RelayCommand(RefreshTargets);

        _globalKeyboardStartupStatusService.StatusChanged += OnGlobalKeyboardStartupStatusChanged;
        ApplyGlobalKeyboardStartupStatus(_globalKeyboardStartupStatusService.Current);
        RefreshTargets();
    }

    private void OnGlobalKeyboardStartupStatusChanged(object? sender, EventArgs e)
    {
        GlobalKeyboardStartupStatus status = _globalKeyboardStartupStatusService.Current;
        if (_dispatcher.CheckAccess())
        {
            ApplyGlobalKeyboardStartupStatus(status);
            return;
        }

        _dispatcher.Post(() => ApplyGlobalKeyboardStartupStatus(status));
    }

    /// <summary>
    /// Rebuilds the rows from the current targets and their shortcuts; any capture in
    /// progress is cancelled.
    /// </summary>
    public void RefreshTargets()
    {
        CancelCapture();

        KeybindTargetCatalogSnapshot targetCatalog = _targetCatalogService.GetTargets(
            _selectedClientsProvider()
        );

        ActionRows = new ObservableCollection<KeybindTargetRowViewModel>(
            targetCatalog.ActionTargets.Select(CreateRow)
        );
        ClientRows = new ObservableCollection<KeybindTargetRowViewModel>(
            targetCatalog.ClientTargets.Select(CreateRow)
        );
    }

    /// <summary>
    /// Feeds a key press to the row waiting for a shortcut. Returns <see langword="false"/>
    /// when no capture is in progress, so the key keeps its normal behavior.
    /// </summary>
    public bool TryCaptureKey(KeybindCaptureResult captureResult)
    {
        ArgumentNullException.ThrowIfNull(captureResult);

        KeybindTargetRowViewModel? row = _capturingRow;
        if (row is null)
            return false;

        if (!captureResult.Succeeded || captureResult.Combination is null)
        {
            _capturedCombination = null;
            row.HasCapturedCombination = false;
            row.CapturePreview = captureResult.Message;
            return true;
        }

        _capturedCombination = KeyCombinationParser.Normalize(captureResult.Combination);
        row.HasCapturedCombination = true;
        row.CapturePreview = KeyCombinationParser.ToCanonicalString(_capturedCombination);
        return true;
    }

    private KeybindTargetRowViewModel CreateRow(KeybindTargetDescriptor target)
    {
        return new KeybindTargetRowViewModel(
            target,
            LoadShortcuts(target),
            BeginCapture,
            ConfirmCapture,
            CancelCapture,
            RemoveShortcut
        );
    }

    private IEnumerable<KeybindShortcutOption> LoadShortcuts(KeybindTargetDescriptor target)
    {
        return _keybindManager
            .GetShortcutsForTarget(target.TargetId)
            .OrderBy(
                shortcut => KeyCombinationParser.ToCanonicalString(shortcut.Combination),
                StringComparer.Ordinal
            )
            .Select(shortcut => new KeybindShortcutOption(shortcut))
            .ToArray();
    }

    private void BeginCapture(KeybindTargetRowViewModel row)
    {
        CancelCapture();

        _capturingRow = row;
        row.IsCapturing = true;
        row.CapturePreview = KeybindTargetRowViewModel.CapturePrompt;
        row.HasCapturedCombination = false;
        SetStatus(string.Empty, KeybindStatusKind.None);
        OnPropertyChanged(nameof(CapturingRow));
    }

    private void ConfirmCapture()
    {
        KeybindTargetRowViewModel? row = _capturingRow;
        if (row is null || _capturedCombination is null)
            return;

        KeybindShortcutAddResult result = _keybindManager.AddShortcut(
            row.Target.TargetId,
            row.Target.DisplayName,
            _capturedCombination
        );
        if (!result.Succeeded)
        {
            SetStatus(result.Message, KeybindStatusKind.Error);
            return;
        }

        row.ReplaceShortcuts(LoadShortcuts(row.Target));
        CancelCapture();
        SetStatus($"Shortcut added to {row.DisplayName}.", KeybindStatusKind.Success);
    }

    private void CancelCapture()
    {
        KeybindTargetRowViewModel? row = _capturingRow;
        _capturingRow = null;
        _capturedCombination = null;
        if (row is null)
            return;

        row.IsCapturing = false;
        row.HasCapturedCombination = false;
        row.CapturePreview = KeybindTargetRowViewModel.CapturePrompt;
        OnPropertyChanged(nameof(CapturingRow));
    }

    private void RemoveShortcut(KeybindTargetRowViewModel row, KeybindShortcutOption shortcut)
    {
        if (!_keybindManager.RemoveShortcut(row.Target.TargetId, shortcut.Combination))
        {
            SetStatus("Unable to remove this shortcut.", KeybindStatusKind.Error);
            return;
        }

        row.ReplaceShortcuts(LoadShortcuts(row.Target));
        SetStatus($"Shortcut removed from {row.DisplayName}.", KeybindStatusKind.Success);
    }

    private void SetStatus(string message, KeybindStatusKind kind)
    {
        StatusMessage = message;
        StatusKind = kind;
    }

    private void ApplyGlobalKeyboardStartupStatus(GlobalKeyboardStartupStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        ShowGlobalKeyboardSetupBanner = status.HasIssue;
        if (!status.HasIssue)
        {
            GlobalKeyboardSetupTitle = string.Empty;
            GlobalKeyboardSetupMessage = string.Empty;
            GlobalKeyboardSetupCommand = string.Empty;
            ShowGlobalKeyboardSetupCommand = false;
            GlobalKeyboardSetupGuidance = string.Empty;
            return;
        }

        GlobalKeyboardSetupTitle =
            status.IssueKind == GlobalKeyboardStartupIssueKind.LinuxInputAccessDenied
                ? "Linux input access required"
                : "Global shortcuts unavailable";
        GlobalKeyboardSetupMessage = status.Message;
        GlobalKeyboardSetupCommand = status.RemediationCommand ?? string.Empty;
        ShowGlobalKeyboardSetupCommand = !string.IsNullOrWhiteSpace(status.RemediationCommand);
        GlobalKeyboardSetupGuidance = status.Guidance ?? string.Empty;
    }
}
