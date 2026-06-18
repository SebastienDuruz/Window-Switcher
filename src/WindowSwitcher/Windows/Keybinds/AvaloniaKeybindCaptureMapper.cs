using Avalonia.Input;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Utilities;
using WindowSwitcher.ViewModels;

namespace WindowSwitcher.Windows.Keybinds;

internal static class AvaloniaKeybindCaptureMapper
{
    public static KeybindCaptureResult Create(
        Key key,
        PhysicalKey physicalKey,
        string? keySymbol,
        KeyModifiers modifiers)
    {
        return TryCreate(key, physicalKey, keySymbol, modifiers, out KeyCombination combination, out string message)
            ? KeybindCaptureResult.Success(combination)
            : KeybindCaptureResult.Failure(message);
    }

    public static bool TryCreate(
        Key key,
        PhysicalKey physicalKey,
        string? keySymbol,
        KeyModifiers modifiers,
        out KeyCombination combination,
        out string message
    )
    {
        combination = new KeyCombination();
        message = string.Empty;

        if (IsModifierOnlyKey(key) || IsModifierOnlyKey(physicalKey))
        {
            message = "A shortcut must contain a primary key.";
            return false;
        }

        if (!TryMapPrimaryKey(physicalKey, key, keySymbol, out KeybindPrimaryKey primaryKey))
        {
            message = "Unsupported key. Use any non-modifier keyboard key.";
            return false;
        }

        combination = new KeyCombination
        {
            Ctrl = modifiers.HasFlag(KeyModifiers.Control),
            Alt = modifiers.HasFlag(KeyModifiers.Alt),
            Shift = modifiers.HasFlag(KeyModifiers.Shift),
            Meta = modifiers.HasFlag(KeyModifiers.Meta),
            Key = primaryKey,
        };

        return true;
    }

    private static bool IsModifierOnlyKey(Key key)
    {
        return key
            is Key.LeftCtrl
                or Key.RightCtrl
                or Key.LeftShift
                or Key.RightShift
                or Key.LeftAlt
                or Key.RightAlt
                or Key.LWin
                or Key.RWin;
    }

    private static bool IsModifierOnlyKey(PhysicalKey key)
    {
        return key
            is PhysicalKey.ControlLeft
                or PhysicalKey.ControlRight
                or PhysicalKey.ShiftLeft
                or PhysicalKey.ShiftRight
                or PhysicalKey.AltLeft
                or PhysicalKey.AltRight
                or PhysicalKey.MetaLeft
                or PhysicalKey.MetaRight;
    }

    private static bool TryMapPrimaryKey(
        PhysicalKey physicalKey,
        Key logicalKey,
        string? keySymbol,
        out KeybindPrimaryKey primaryKey)
    {
        if (
            physicalKey != PhysicalKey.None
            && KeyCombinationParser.TryParsePrimaryToken(physicalKey.ToString(), out primaryKey)
        )
            return true;

        if (KeyCombinationParser.TryParsePrimaryToken(logicalKey.ToString(), out primaryKey))
            return true;

        if (
            !string.IsNullOrWhiteSpace(keySymbol)
            && KeyCombinationParser.TryParsePrimaryToken(keySymbol.Trim(), out primaryKey)
        )
            return true;

        primaryKey = KeybindPrimaryKey.None;
        return false;
    }
}
