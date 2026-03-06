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
    private readonly Func<IReadOnlyCollection<WindowConfig>> _selectedClientsProvider;
    private bool _isSynchronizingSelection;
    private KeyCombination? _capturedCombination;

    [ObservableProperty]
    private ObservableCollection<KeybindTargetOption> _actionTargets = [];

    [ObservableProperty]
    private KeybindTargetOption? _selectedActionTarget;

    [ObservableProperty]
    private ObservableCollection<KeybindTargetOption> _clientTargets = [];

    [ObservableProperty]
    private KeybindTargetOption? _selectedClientTarget;

    [ObservableProperty]
    private ObservableCollection<KeybindBindingOption> _bindings = [];

    [ObservableProperty]
    private KeybindBindingOption? _selectedBinding;

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
    public IRelayCommand RemoveBindingCommand { get; }

    public bool CanConfirmCapture => IsCapturing && _capturedCombination is not null;

    public KeybindSettingsViewModel(
        IWindowKeybindManager keybindManager,
        Func<IReadOnlyCollection<WindowConfig>> selectedClientsProvider
    )
    {
        ArgumentNullException.ThrowIfNull(keybindManager);
        ArgumentNullException.ThrowIfNull(selectedClientsProvider);

        _keybindManager = keybindManager;
        _selectedClientsProvider = selectedClientsProvider;

        RefreshTargetsCommand = new RelayCommand(RefreshTargets);
        BeginCaptureCommand = new RelayCommand(BeginCapture);
        ConfirmCaptureCommand = new RelayCommand(ConfirmCapture);
        CancelCaptureCommand = new RelayCommand(CancelCapture);
        RemoveBindingCommand = new RelayCommand(RemoveBinding);

        RefreshTargets();
    }

    public void RefreshTargets()
    {
        string? previousTargetId = GetSelectedTarget()?.TargetId;

        IReadOnlyCollection<WindowKeybindTargetConfig> persistedTargets = _keybindManager.GetTargets();

        Dictionary<string, KeybindTargetOption> actionsById = BuildActionTargets(persistedTargets);
        List<KeybindTargetOption> clientTargets = BuildClientTargets(persistedTargets);

        ActionTargets = new ObservableCollection<KeybindTargetOption>(actionsById.Values);
        ClientTargets = new ObservableCollection<KeybindTargetOption>(clientTargets);

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
            OnPropertyChanged(nameof(CanConfirmCapture));
            return true;
        }

        _capturedCombination = KeyCombinationParser.Normalize(combination);
        CapturePreview = KeyCombinationParser.ToCanonicalString(_capturedCombination);
        StatusMessage = string.Empty;
        OnPropertyChanged(nameof(CanConfirmCapture));
        return true;
    }

    partial void OnSelectedActionTargetChanged(KeybindTargetOption? value)
    {
        if (_isSynchronizingSelection)
            return;

        if (value is not null)
        {
            _isSynchronizingSelection = true;
            SelectedClientTarget = null;
            _isSynchronizingSelection = false;
        }

        HandleTargetSelectionChanged();
    }

    partial void OnSelectedClientTargetChanged(KeybindTargetOption? value)
    {
        if (_isSynchronizingSelection)
            return;

        if (value is not null)
        {
            _isSynchronizingSelection = true;
            SelectedActionTarget = null;
            _isSynchronizingSelection = false;
        }

        HandleTargetSelectionChanged();
    }

    partial void OnIsCapturingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanConfirmCapture));
    }

    private static string BuildDisplayName(WindowConfig window)
    {
        string title = string.IsNullOrWhiteSpace(window.WindowTitle)
            ? "(untitled)"
            : window.WindowTitle.Trim();
        string process = string.IsNullOrWhiteSpace(window.ProcessName)
            ? "unknown"
            : window.ProcessName.Trim();

        return $"{title} ({process})";
    }

    private static Dictionary<string, KeybindTargetOption> BuildActionTargets(
        IEnumerable<WindowKeybindTargetConfig> persistedTargets
    )
    {
        ArgumentNullException.ThrowIfNull(persistedTargets);

        var actionsById = new Dictionary<string, KeybindTargetOption>(StringComparer.Ordinal)
        {
            [KeybindBuiltInTargets.NextClientTargetId] = new KeybindTargetOption(
                KeybindBuiltInTargets.NextClientTargetId,
                KeybindBuiltInTargets.NextClientDisplayName
            ),
            [KeybindBuiltInTargets.PreviousClientTargetId] = new KeybindTargetOption(
                KeybindBuiltInTargets.PreviousClientTargetId,
                KeybindBuiltInTargets.PreviousClientDisplayName
            ),
        };

        foreach (WindowKeybindTargetConfig target in persistedTargets)
        {
            if (!KeybindBuiltInTargets.IsBuiltInTarget(target.TargetId))
                continue;

            if (
                actionsById.TryGetValue(target.TargetId, out _)
                && !string.IsNullOrWhiteSpace(target.DisplayLabel)
            )
            {
                actionsById[target.TargetId] = new KeybindTargetOption(
                    target.TargetId,
                    target.DisplayLabel
                );
                continue;
            }

            if (!actionsById.ContainsKey(target.TargetId))
            {
                actionsById[target.TargetId] = new KeybindTargetOption(
                    target.TargetId,
                    target.DisplayLabel
                );
            }
        }

        return actionsById;
    }

    private List<KeybindTargetOption> BuildClientTargets(
        IReadOnlyCollection<WindowKeybindTargetConfig> persistedTargets
    )
    {
        ArgumentNullException.ThrowIfNull(persistedTargets);

        List<KeybindTargetOption> runtimeTargets = _selectedClientsProvider()
            .Select(runtimeWindow =>
            {
                string targetId = WindowTargetKeyFactory.Create(runtimeWindow);
                if (string.IsNullOrWhiteSpace(targetId))
                    return null;

                string displayName = BuildDisplayName(runtimeWindow);
                return new KeybindTargetOption(targetId, displayName);
            })
            .Where(target => target is not null)
            .Select(target => target!)
            .OrderBy(target => target.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        HashSet<string> knownTargetIds = runtimeTargets
            .Select(target => target.TargetId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (WindowKeybindTargetConfig target in persistedTargets)
        {
            if (string.IsNullOrWhiteSpace(target.TargetId))
                continue;
            if (KeybindBuiltInTargets.IsBuiltInTarget(target.TargetId))
                continue;
            if (!knownTargetIds.Add(target.TargetId))
                continue;

            runtimeTargets.Add(new KeybindTargetOption(target.TargetId, target.DisplayLabel));
        }

        return runtimeTargets;
    }

    private void HandleTargetSelectionChanged()
    {
        LoadBindingsForSelectedTarget();
        StatusMessage = string.Empty;
        CancelCapture();
    }

    private void SelectTargetById(string? targetId)
    {
        KeybindTargetOption? selectedAction = ActionTargets.FirstOrDefault(target =>
            string.Equals(target.TargetId, targetId, StringComparison.Ordinal)
        );
        KeybindTargetOption? selectedClient = selectedAction is null
            ? ClientTargets.FirstOrDefault(target =>
                string.Equals(target.TargetId, targetId, StringComparison.Ordinal)
            )
            : null;

        _isSynchronizingSelection = true;
        SelectedActionTarget = selectedAction;
        SelectedClientTarget = selectedClient;
        _isSynchronizingSelection = false;

        LoadBindingsForSelectedTarget();
        StatusMessage = string.Empty;
        CancelCapture();
    }

    private KeybindTargetOption? GetSelectedTarget()
    {
        return SelectedActionTarget ?? SelectedClientTarget;
    }

    private void BeginCapture()
    {
        KeybindTargetOption? selectedTarget = GetSelectedTarget();
        if (selectedTarget is null)
        {
            StatusMessage = "Select an action or a client target first.";
            return;
        }

        _capturedCombination = null;
        IsCapturing = true;
        CapturePreview = "Press the shortcut to capture";
        StatusMessage = string.Empty;
        OnPropertyChanged(nameof(CanConfirmCapture));
    }

    private void ConfirmCapture()
    {
        KeybindTargetOption? selectedTarget = GetSelectedTarget();
        if (selectedTarget is null || _capturedCombination is null)
            return;

        KeybindRegistrationResult result = _keybindManager.TryAddBinding(
            selectedTarget.TargetId,
            selectedTarget.DisplayName,
            _capturedCombination,
            out string message
        );
        if (result != KeybindRegistrationResult.Added)
        {
            StatusMessage = message;
            return;
        }

        LoadBindingsForSelectedTarget();
        CancelCapture();
        StatusMessage = "Shortcut added.";
    }

    private void CancelCapture()
    {
        _capturedCombination = null;
        IsCapturing = false;
        CapturePreview = "Press the shortcut to capture";
        OnPropertyChanged(nameof(CanConfirmCapture));
    }

    private void RemoveBinding()
    {
        KeybindTargetOption? selectedTarget = GetSelectedTarget();
        if (selectedTarget is null || SelectedBinding is null)
            return;

        bool removed = _keybindManager.RemoveBinding(
            selectedTarget.TargetId,
            SelectedBinding.Combination
        );
        if (!removed)
        {
            StatusMessage = "Unable to remove this shortcut.";
            return;
        }

        LoadBindingsForSelectedTarget();
        StatusMessage = "Shortcut removed.";
    }

    private void LoadBindingsForSelectedTarget()
    {
        KeybindTargetOption? selectedTarget = GetSelectedTarget();
        if (selectedTarget is null)
        {
            Bindings = [];
            SelectedBinding = null;
            return;
        }

        Bindings = new ObservableCollection<KeybindBindingOption>(
            _keybindManager
                .GetBindingsForTarget(selectedTarget.TargetId)
                .OrderBy(binding => KeyCombinationParser.ToCanonicalString(binding.Combination))
                .Select(binding => new KeybindBindingOption(binding))
        );
        SelectedBinding = null;
    }
}

public sealed class KeybindTargetOption(string targetId, string displayName)
{
    public string TargetId { get; } = targetId;
    public string DisplayName { get; } = displayName;
}

public sealed class KeybindBindingOption(WindowKeybindBinding binding)
{
    public KeyCombination Combination { get; } = binding.Combination.Clone();
    public bool Enabled { get; } = binding.Enabled;
    public string Display { get; } = KeyCombinationParser.ToCanonicalString(binding.Combination);
}
