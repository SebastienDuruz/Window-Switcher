using System.Globalization;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Utilities;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Runtime;

internal static class GlobalKeyEventKeyResolver
{
    public static bool TryResolve(
        GlobalKeyEventArgs keyEvent,
        out ResolvedGlobalKeyEvent resolvedEvent
    )
    {
        ArgumentNullException.ThrowIfNull(keyEvent);

        resolvedEvent = default;

        if (string.Equals(keyEvent.Platform, "Windows", StringComparison.OrdinalIgnoreCase))
            return TryResolveWindows(keyEvent, out resolvedEvent);

        if (string.Equals(keyEvent.Platform, "Linux", StringComparison.OrdinalIgnoreCase))
            return TryResolveLinux(keyEvent, out resolvedEvent);

        return false;
    }

    private static bool TryResolveWindows(
        GlobalKeyEventArgs keyEvent,
        out ResolvedGlobalKeyEvent resolvedEvent
    )
    {
        resolvedEvent = default;
        if (!TryParseWindowsVirtualKeyCode(keyEvent.KeyCode, out int virtualKeyCode))
            return false;

        KeybindModifier? modifier = virtualKeyCode switch
        {
            0x10 or 0xA0 or 0xA1 => KeybindModifier.Shift,
            0x11 or 0xA2 or 0xA3 => KeybindModifier.Ctrl,
            0x12 or 0xA4 or 0xA5 => KeybindModifier.Alt,
            0x5B or 0x5C => KeybindModifier.Meta,
            _ => null,
        };

        if (modifier.HasValue)
        {
            resolvedEvent = new ResolvedGlobalKeyEvent(
                keyEvent.State,
                keyEvent.IsRepeat,
                modifier,
                KeybindPrimaryKey.None
            );
            return true;
        }

        if (TryResolveWindowsPrimaryKey(virtualKeyCode, out KeybindPrimaryKey primaryKey))
        {
            resolvedEvent = new ResolvedGlobalKeyEvent(
                keyEvent.State,
                keyEvent.IsRepeat,
                Modifier: null,
                PrimaryKey: primaryKey
            );
            return true;
        }

        return false;
    }

    private static bool TryResolveLinux(
        GlobalKeyEventArgs keyEvent,
        out ResolvedGlobalKeyEvent resolvedEvent
    )
    {
        resolvedEvent = default;
        if (string.IsNullOrWhiteSpace(keyEvent.KeyName))
            return false;

        string keyName = keyEvent.KeyName.Trim();
        KeybindModifier? modifier = keyName switch
        {
            "LeftShift" or "RightShift" => KeybindModifier.Shift,
            "LeftCtrl" or "RightCtrl" => KeybindModifier.Ctrl,
            "LeftAlt" or "RightAlt" => KeybindModifier.Alt,
            "LeftMeta" or "RightMeta" => KeybindModifier.Meta,
            _ => null,
        };

        if (modifier.HasValue)
        {
            resolvedEvent = new ResolvedGlobalKeyEvent(
                keyEvent.State,
                keyEvent.IsRepeat,
                modifier,
                KeybindPrimaryKey.None
            );
            return true;
        }

        if (!KeyCombinationParser.TryParsePrimaryToken(keyName, out KeybindPrimaryKey primaryKey))
            return false;

        resolvedEvent = new ResolvedGlobalKeyEvent(
            keyEvent.State,
            keyEvent.IsRepeat,
            Modifier: null,
            PrimaryKey: primaryKey
        );
        return true;
    }

    private static bool TryParseWindowsVirtualKeyCode(string keyCode, out int virtualKeyCode)
    {
        virtualKeyCode = 0;
        if (string.IsNullOrWhiteSpace(keyCode))
            return false;
        if (!keyCode.StartsWith("VK_", StringComparison.OrdinalIgnoreCase))
            return false;

        return int.TryParse(
            keyCode[3..],
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out virtualKeyCode
        );
    }

    private static bool TryResolveWindowsPrimaryKey(
        int virtualKeyCode,
        out KeybindPrimaryKey primaryKey
    )
    {
        if (virtualKeyCode is >= 0x41 and <= 0x5A)
        {
            primaryKey = (KeybindPrimaryKey)((int)KeybindPrimaryKey.A + (virtualKeyCode - 0x41));
            return true;
        }

        if (virtualKeyCode is >= 0x30 and <= 0x39)
        {
            primaryKey = (KeybindPrimaryKey)((int)KeybindPrimaryKey.D0 + (virtualKeyCode - 0x30));
            return true;
        }

        if (virtualKeyCode is >= 0x70 and <= 0x87)
        {
            primaryKey = (KeybindPrimaryKey)((int)KeybindPrimaryKey.F1 + (virtualKeyCode - 0x70));
            return true;
        }

        if (virtualKeyCode is >= 0x60 and <= 0x69)
        {
            primaryKey = (KeybindPrimaryKey)(
                (int)KeybindPrimaryKey.NumPad0 + (virtualKeyCode - 0x60)
            );
            return true;
        }

        primaryKey = virtualKeyCode switch
        {
            0x08 => KeybindPrimaryKey.Backspace,
            0x09 => KeybindPrimaryKey.Tab,
            0x0D => KeybindPrimaryKey.Enter,
            0x13 => KeybindPrimaryKey.Pause,
            0x14 => KeybindPrimaryKey.CapsLock,
            0x1B => KeybindPrimaryKey.Escape,
            0x20 => KeybindPrimaryKey.Space,
            0x21 => KeybindPrimaryKey.PageUp,
            0x22 => KeybindPrimaryKey.PageDown,
            0x23 => KeybindPrimaryKey.End,
            0x24 => KeybindPrimaryKey.Home,
            0x25 => KeybindPrimaryKey.Left,
            0x26 => KeybindPrimaryKey.Up,
            0x27 => KeybindPrimaryKey.Right,
            0x28 => KeybindPrimaryKey.Down,
            0x2C => KeybindPrimaryKey.PrintScreen,
            0x2D => KeybindPrimaryKey.Insert,
            0x2E => KeybindPrimaryKey.Delete,
            0x5D => KeybindPrimaryKey.Menu,
            0x6A => KeybindPrimaryKey.NumPadMultiply,
            0x6B => KeybindPrimaryKey.NumPadAdd,
            0x6C => KeybindPrimaryKey.NumPadDecimal,
            0x6D => KeybindPrimaryKey.NumPadSubtract,
            0x6E => KeybindPrimaryKey.NumPadDecimal,
            0x6F => KeybindPrimaryKey.NumPadDivide,
            0x90 => KeybindPrimaryKey.NumLock,
            0x91 => KeybindPrimaryKey.ScrollLock,
            0xBA => KeybindPrimaryKey.Semicolon,
            0xBB => KeybindPrimaryKey.Equal,
            0xBC => KeybindPrimaryKey.Comma,
            0xBD => KeybindPrimaryKey.Minus,
            0xBE => KeybindPrimaryKey.Period,
            0xBF => KeybindPrimaryKey.Slash,
            0xC0 => KeybindPrimaryKey.Grave,
            0xDB => KeybindPrimaryKey.LeftBracket,
            0xDC => KeybindPrimaryKey.Backslash,
            0xDD => KeybindPrimaryKey.RightBracket,
            0xDE => KeybindPrimaryKey.Apostrophe,
            0xE2 => KeybindPrimaryKey.IntlBackslash,
            _ => KeybindPrimaryKey.None,
        };

        return primaryKey != KeybindPrimaryKey.None;
    }
}
