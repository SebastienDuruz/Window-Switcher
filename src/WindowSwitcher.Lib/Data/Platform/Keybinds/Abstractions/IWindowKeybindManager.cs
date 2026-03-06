using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;

/// <summary>
/// Manages persisted keybind definitions and conflict detection.
/// </summary>
public interface IWindowKeybindManager
{
    /// <summary>
    /// Returns all configured targets and their bindings.
    /// </summary>
    IReadOnlyCollection<WindowKeybindTargetConfig> GetTargets();

    /// <summary>
    /// Returns bindings for one target.
    /// </summary>
    IReadOnlyCollection<WindowKeybindBinding> GetBindingsForTarget(string targetId);

    /// <summary>
    /// Creates or updates a target metadata entry.
    /// </summary>
    void UpsertTarget(string targetId, string displayLabel);

    /// <summary>
    /// Tries to register a binding for the target.
    /// </summary>
    KeybindRegistrationResult TryAddBinding(
        string targetId,
        string displayLabel,
        KeyCombination combination,
        out string message
    );

    /// <summary>
    /// Removes a binding from one target.
    /// </summary>
    bool RemoveBinding(string targetId, KeyCombination combination);

    /// <summary>
    /// Resolves target id for an active combination.
    /// </summary>
    bool TryResolveTarget(KeyCombination combination, out string targetId);
}
