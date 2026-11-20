using System.Globalization;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Models;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Runtime;

internal static class GlobalKeyEventKeyResolver
{
    private const int VirtualKeyReturn = 0x0D;

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

        if (virtualKeyCode == VirtualKeyReturn && keyEvent.IsExtendedKey)
        {
            resolvedEvent = new ResolvedGlobalKeyEvent(
                keyEvent.State,
                keyEvent.IsRepeat,
                Modifier: null,
                PrimaryKey: KeybindPrimaryKey.NumPadEnter
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
        if (!TryParseLinuxKeyCode(keyEvent.KeyName, out KeyCode keyCode))
            return false;

        KeybindModifier? modifier = keyCode switch
        {
            KeyCode.LeftShift or KeyCode.RightShift => KeybindModifier.Shift,
            KeyCode.LeftCtrl or KeyCode.RightCtrl => KeybindModifier.Ctrl,
            KeyCode.LeftAlt or KeyCode.RightAlt => KeybindModifier.Alt,
            KeyCode.LeftMeta or KeyCode.RightMeta => KeybindModifier.Meta,
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

        KeybindPrimaryKey primaryKey = ResolveLinuxPrimaryKey(keyCode);
        if (primaryKey == KeybindPrimaryKey.None)
            return false;

        resolvedEvent = new ResolvedGlobalKeyEvent(
            keyEvent.State,
            keyEvent.IsRepeat,
            Modifier: null,
            PrimaryKey: primaryKey
        );
        return true;
    }

    /// <summary>
    /// Reads the evdev key named by <see cref="GlobalKeyboardEventMapper.FromLinux"/>.
    /// </summary>
    private static bool TryParseLinuxKeyCode(string? keyName, out KeyCode keyCode)
    {
        keyCode = KeyCode.Reserved;
        if (string.IsNullOrWhiteSpace(keyName))
            return false;

        string trimmedName = keyName.Trim();
        // Enum.TryParse also accepts numeric strings; only key names are valid here.
        if (char.IsDigit(trimmedName[0]) || trimmedName[0] is '-' or '+')
            return false;

        return Enum.TryParse(trimmedName, ignoreCase: false, out keyCode)
            && Enum.IsDefined(keyCode);
    }

    /// <summary>
    /// Maps evdev key codes to primary keys. The table is explicit so that multimedia keys whose
    /// names look like keyboard keys (KEY_BACK, for instance) are never mistaken for them.
    /// </summary>
    private static KeybindPrimaryKey ResolveLinuxPrimaryKey(KeyCode keyCode)
    {
        if (keyCode is >= KeyCode.F1 and <= KeyCode.F10)
            return (KeybindPrimaryKey)((int)KeybindPrimaryKey.F1 + (keyCode - KeyCode.F1));

        if (keyCode is >= KeyCode.F13 and <= KeyCode.F24)
            return (KeybindPrimaryKey)((int)KeybindPrimaryKey.F13 + (keyCode - KeyCode.F13));

        if (keyCode is >= KeyCode.Digit1 and <= KeyCode.Digit9)
            return (KeybindPrimaryKey)((int)KeybindPrimaryKey.D1 + (keyCode - KeyCode.Digit1));

        return keyCode switch
        {
            KeyCode.A => KeybindPrimaryKey.A,
            KeyCode.B => KeybindPrimaryKey.B,
            KeyCode.C => KeybindPrimaryKey.C,
            KeyCode.D => KeybindPrimaryKey.D,
            KeyCode.E => KeybindPrimaryKey.E,
            KeyCode.F => KeybindPrimaryKey.F,
            KeyCode.G => KeybindPrimaryKey.G,
            KeyCode.H => KeybindPrimaryKey.H,
            KeyCode.I => KeybindPrimaryKey.I,
            KeyCode.J => KeybindPrimaryKey.J,
            KeyCode.K => KeybindPrimaryKey.K,
            KeyCode.L => KeybindPrimaryKey.L,
            KeyCode.M => KeybindPrimaryKey.M,
            KeyCode.N => KeybindPrimaryKey.N,
            KeyCode.O => KeybindPrimaryKey.O,
            KeyCode.P => KeybindPrimaryKey.P,
            KeyCode.Q => KeybindPrimaryKey.Q,
            KeyCode.R => KeybindPrimaryKey.R,
            KeyCode.S => KeybindPrimaryKey.S,
            KeyCode.T => KeybindPrimaryKey.T,
            KeyCode.U => KeybindPrimaryKey.U,
            KeyCode.V => KeybindPrimaryKey.V,
            KeyCode.W => KeybindPrimaryKey.W,
            KeyCode.X => KeybindPrimaryKey.X,
            KeyCode.Y => KeybindPrimaryKey.Y,
            KeyCode.Z => KeybindPrimaryKey.Z,
            KeyCode.Digit0 => KeybindPrimaryKey.D0,
            KeyCode.F11 => KeybindPrimaryKey.F11,
            KeyCode.F12 => KeybindPrimaryKey.F12,
            KeyCode.Esc => KeybindPrimaryKey.Escape,
            KeyCode.Tab => KeybindPrimaryKey.Tab,
            KeyCode.Backspace => KeybindPrimaryKey.Backspace,
            KeyCode.Enter => KeybindPrimaryKey.Enter,
            KeyCode.Space => KeybindPrimaryKey.Space,
            KeyCode.CapsLock => KeybindPrimaryKey.CapsLock,
            KeyCode.NumLock => KeybindPrimaryKey.NumLock,
            KeyCode.ScrollLock => KeybindPrimaryKey.ScrollLock,
            KeyCode.Pause => KeybindPrimaryKey.Pause,
            // KEY_SYSRQ is the PrtSc key; xkb also maps KEY_PRINT to the Print keysym.
            KeyCode.PrintScreen or KeyCode.Print => KeybindPrimaryKey.PrintScreen,
            KeyCode.Insert => KeybindPrimaryKey.Insert,
            KeyCode.Delete => KeybindPrimaryKey.Delete,
            KeyCode.Home => KeybindPrimaryKey.Home,
            KeyCode.End => KeybindPrimaryKey.End,
            KeyCode.PageUp => KeybindPrimaryKey.PageUp,
            KeyCode.PageDown => KeybindPrimaryKey.PageDown,
            KeyCode.ArrowLeft => KeybindPrimaryKey.Left,
            KeyCode.ArrowUp => KeybindPrimaryKey.Up,
            KeyCode.ArrowRight => KeybindPrimaryKey.Right,
            KeyCode.ArrowDown => KeybindPrimaryKey.Down,
            // The PC context-menu key sends KEY_COMPOSE; KEY_MENU comes from other keyboards.
            KeyCode.Compose or KeyCode.Menu => KeybindPrimaryKey.Menu,
            KeyCode.Keypad0 => KeybindPrimaryKey.NumPad0,
            KeyCode.Keypad1 => KeybindPrimaryKey.NumPad1,
            KeyCode.Keypad2 => KeybindPrimaryKey.NumPad2,
            KeyCode.Keypad3 => KeybindPrimaryKey.NumPad3,
            KeyCode.Keypad4 => KeybindPrimaryKey.NumPad4,
            KeyCode.Keypad5 => KeybindPrimaryKey.NumPad5,
            KeyCode.Keypad6 => KeybindPrimaryKey.NumPad6,
            KeyCode.Keypad7 => KeybindPrimaryKey.NumPad7,
            KeyCode.Keypad8 => KeybindPrimaryKey.NumPad8,
            KeyCode.Keypad9 => KeybindPrimaryKey.NumPad9,
            // Capture maps the Avalonia NumPadComma and NumPadEqual keys the same way.
            KeyCode.KeypadDot or KeyCode.KeypadComma => KeybindPrimaryKey.NumPadDecimal,
            KeyCode.KeypadEqual => KeybindPrimaryKey.Equal,
            KeyCode.KeypadPlus => KeybindPrimaryKey.NumPadAdd,
            KeyCode.KeypadMinus => KeybindPrimaryKey.NumPadSubtract,
            KeyCode.KeypadAsterisk => KeybindPrimaryKey.NumPadMultiply,
            KeyCode.KeypadSlash => KeybindPrimaryKey.NumPadDivide,
            KeyCode.KeypadEnter => KeybindPrimaryKey.NumPadEnter,
            KeyCode.Minus => KeybindPrimaryKey.Minus,
            KeyCode.Equal => KeybindPrimaryKey.Equal,
            KeyCode.LeftBrace => KeybindPrimaryKey.LeftBracket,
            KeyCode.RightBrace => KeybindPrimaryKey.RightBracket,
            KeyCode.Backslash => KeybindPrimaryKey.Backslash,
            KeyCode.Semicolon => KeybindPrimaryKey.Semicolon,
            KeyCode.Apostrophe => KeybindPrimaryKey.Apostrophe,
            KeyCode.Grave => KeybindPrimaryKey.Grave,
            KeyCode.Comma => KeybindPrimaryKey.Comma,
            KeyCode.Dot => KeybindPrimaryKey.Period,
            KeyCode.Slash => KeybindPrimaryKey.Slash,
            KeyCode.IntlBackslash => KeybindPrimaryKey.IntlBackslash,
            _ => KeybindPrimaryKey.None,
        };
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
