using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;

/// <summary>
/// Manages persisted keybind definitions and conflict detection.
/// </summary>
public interface IWindowKeybindManager
{
    /// <summary>
    /// Returns all configured targets and their shortcuts.
    /// </summary>
    IReadOnlyCollection<WindowKeybindTargetConfig> GetTargets();

    /// <summary>
    /// Returns shortcuts for one target.
    /// </summary>
    IReadOnlyCollection<WindowKeybindShortcut> GetShortcutsForTarget(string targetId);

    /// <summary>
    /// Creates or updates a target metadata entry.
    /// </summary>
    void UpsertTarget(string targetId, string displayLabel);

    /// <summary>
    /// Registers a binding for the target.
    /// </summary>
    KeybindShortcutAddResult AddShortcut(
        string targetId,
        string displayLabel,
        KeyCombination combination
    );

    /// <summary>
    /// Removes a binding from one target.
    /// </summary>
    bool RemoveShortcut(string targetId, KeyCombination combination);

    /// <summary>
    /// Resolves target id for an active combination.
    /// </summary>
    bool TryResolveTarget(KeyCombination combination, out string targetId);
}
