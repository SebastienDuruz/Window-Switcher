using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Utilities;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Runtime;

/// <summary>
/// Tracks currently pressed keys and builds normalized triggered combinations.
/// </summary>
public sealed class KeybindPressedStateTracker
{
    private readonly object _syncRoot = new();
    private readonly HashSet<KeybindModifier> _pressedModifiers = [];
    private readonly HashSet<KeybindPrimaryKey> _pressedPrimaryKeys = [];

    /// <summary>
    /// Processes one global keyboard event.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when a valid combination has just been triggered on key down.
    /// </returns>
    public bool TryGetTriggeredCombination(
        GlobalKeyEventArgs keyEvent,
        out KeyCombination combination
    )
    {
        ArgumentNullException.ThrowIfNull(keyEvent);

        combination = new KeyCombination();
        if (!GlobalKeyEventKeyResolver.TryResolve(keyEvent, out ResolvedGlobalKeyEvent resolvedEvent))
            return false;

        lock (_syncRoot)
        {
            if (resolvedEvent.Modifier.HasValue)
            {
                UpdateModifierState(resolvedEvent.Modifier.Value, resolvedEvent.State);
                return false;
            }

            if (resolvedEvent.PrimaryKey == KeybindPrimaryKey.None)
                return false;

            if (resolvedEvent.State == GlobalKeyState.Up)
            {
                _pressedPrimaryKeys.Remove(resolvedEvent.PrimaryKey);
                return false;
            }

            if (resolvedEvent.IsRepeat)
                return false;

            if (!_pressedPrimaryKeys.Add(resolvedEvent.PrimaryKey))
                return false;

            combination = new KeyCombination
            {
                Ctrl = _pressedModifiers.Contains(KeybindModifier.Ctrl),
                Alt = _pressedModifiers.Contains(KeybindModifier.Alt),
                Shift = _pressedModifiers.Contains(KeybindModifier.Shift),
                Meta = _pressedModifiers.Contains(KeybindModifier.Meta),
                Key = resolvedEvent.PrimaryKey,
            };

            combination = KeyCombinationParser.Normalize(combination);
            return KeyCombinationParser.IsValid(combination);
        }
    }

    /// <summary>
    /// Clears all currently pressed keys.
    /// </summary>
    public void Reset()
    {
        lock (_syncRoot)
        {
            _pressedModifiers.Clear();
            _pressedPrimaryKeys.Clear();
        }
    }

    private void UpdateModifierState(KeybindModifier modifier, GlobalKeyState state)
    {
        if (state == GlobalKeyState.Down)
        {
            _pressedModifiers.Add(modifier);
            return;
        }

        _pressedModifiers.Remove(modifier);
    }
}
