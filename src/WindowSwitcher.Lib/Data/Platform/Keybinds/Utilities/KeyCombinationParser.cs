using System.Globalization;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Utilities;

/// <summary>
/// Parses, validates and formats key combinations.
/// </summary>
public static class KeyCombinationParser
{
    private static readonly IReadOnlyDictionary<string, KeybindPrimaryKey> PrimaryKeyAliases =
        new Dictionary<string, KeybindPrimaryKey>(StringComparer.OrdinalIgnoreCase)
        {
            ["ESC"] = KeybindPrimaryKey.Escape,
            ["ESCAPE"] = KeybindPrimaryKey.Escape,
            ["BACK"] = KeybindPrimaryKey.Backspace,
            ["BACKSPACE"] = KeybindPrimaryKey.Backspace,
            ["RETURN"] = KeybindPrimaryKey.Enter,
            ["ENTER"] = KeybindPrimaryKey.Enter,
            ["SPACE"] = KeybindPrimaryKey.Space,
            ["SPACEBAR"] = KeybindPrimaryKey.Space,
            ["CAPITAL"] = KeybindPrimaryKey.CapsLock,
            ["CAPSLOCK"] = KeybindPrimaryKey.CapsLock,
            ["NUMLOCK"] = KeybindPrimaryKey.NumLock,
            ["SCROLL"] = KeybindPrimaryKey.ScrollLock,
            ["SCROLLLOCK"] = KeybindPrimaryKey.ScrollLock,
            ["PRINT"] = KeybindPrimaryKey.PrintScreen,
            ["PRINTSCREEN"] = KeybindPrimaryKey.PrintScreen,
            ["SNAPSHOT"] = KeybindPrimaryKey.PrintScreen,
            ["PRIOR"] = KeybindPrimaryKey.PageUp,
            ["PAGEUP"] = KeybindPrimaryKey.PageUp,
            ["NEXT"] = KeybindPrimaryKey.PageDown,
            ["PAGEDOWN"] = KeybindPrimaryKey.PageDown,
            ["ARROWLEFT"] = KeybindPrimaryKey.Left,
            ["LEFTARROW"] = KeybindPrimaryKey.Left,
            ["ARROWRIGHT"] = KeybindPrimaryKey.Right,
            ["RIGHTARROW"] = KeybindPrimaryKey.Right,
            ["ARROWUP"] = KeybindPrimaryKey.Up,
            ["UPARROW"] = KeybindPrimaryKey.Up,
            ["ARROWDOWN"] = KeybindPrimaryKey.Down,
            ["DOWNARROW"] = KeybindPrimaryKey.Down,
            ["APPS"] = KeybindPrimaryKey.Menu,
            ["MENU"] = KeybindPrimaryKey.Menu,
            ["CONTEXTMENU"] = KeybindPrimaryKey.Menu,
            ["KEYPAD0"] = KeybindPrimaryKey.NumPad0,
            ["KEYPAD1"] = KeybindPrimaryKey.NumPad1,
            ["KEYPAD2"] = KeybindPrimaryKey.NumPad2,
            ["KEYPAD3"] = KeybindPrimaryKey.NumPad3,
            ["KEYPAD4"] = KeybindPrimaryKey.NumPad4,
            ["KEYPAD5"] = KeybindPrimaryKey.NumPad5,
            ["KEYPAD6"] = KeybindPrimaryKey.NumPad6,
            ["KEYPAD7"] = KeybindPrimaryKey.NumPad7,
            ["KEYPAD8"] = KeybindPrimaryKey.NumPad8,
            ["KEYPAD9"] = KeybindPrimaryKey.NumPad9,
            ["KEYPADDECIMAL"] = KeybindPrimaryKey.NumPadDecimal,
            ["KEYPADDOT"] = KeybindPrimaryKey.NumPadDecimal,
            ["DECIMAL"] = KeybindPrimaryKey.NumPadDecimal,
            ["SEPARATOR"] = KeybindPrimaryKey.NumPadDecimal,
            ["KEYPADPLUS"] = KeybindPrimaryKey.NumPadAdd,
            ["ADD"] = KeybindPrimaryKey.NumPadAdd,
            ["KEYPADMINUS"] = KeybindPrimaryKey.NumPadSubtract,
            ["SUBTRACT"] = KeybindPrimaryKey.NumPadSubtract,
            ["KEYPADASTERISK"] = KeybindPrimaryKey.NumPadMultiply,
            ["MULTIPLY"] = KeybindPrimaryKey.NumPadMultiply,
            ["KEYPADSLASH"] = KeybindPrimaryKey.NumPadDivide,
            ["DIVIDE"] = KeybindPrimaryKey.NumPadDivide,
            ["KEYPADENTER"] = KeybindPrimaryKey.NumPadEnter,
            ["NUMPADEQUAL"] = KeybindPrimaryKey.Equal,
            ["NUMPADCOMMA"] = KeybindPrimaryKey.NumPadDecimal,
            ["OEMMINUS"] = KeybindPrimaryKey.Minus,
            ["MINUS"] = KeybindPrimaryKey.Minus,
            ["-"] = KeybindPrimaryKey.Minus,
            ["_"] = KeybindPrimaryKey.Minus,
            ["OEMPLUS"] = KeybindPrimaryKey.Equal,
            ["EQUAL"] = KeybindPrimaryKey.Equal,
            ["EQUALS"] = KeybindPrimaryKey.Equal,
            ["PLUS"] = KeybindPrimaryKey.Equal,
            ["="] = KeybindPrimaryKey.Equal,
            ["LEFTBRACKET"] = KeybindPrimaryKey.LeftBracket,
            ["BRACKETLEFT"] = KeybindPrimaryKey.LeftBracket,
            ["LEFTBRACE"] = KeybindPrimaryKey.LeftBracket,
            ["OEMOPENBRACKETS"] = KeybindPrimaryKey.LeftBracket,
            ["OEM4"] = KeybindPrimaryKey.LeftBracket,
            ["["] = KeybindPrimaryKey.LeftBracket,
            ["{"] = KeybindPrimaryKey.LeftBracket,
            ["RIGHTBRACKET"] = KeybindPrimaryKey.RightBracket,
            ["BRACKETRIGHT"] = KeybindPrimaryKey.RightBracket,
            ["RIGHTBRACE"] = KeybindPrimaryKey.RightBracket,
            ["OEMCLOSEBRACKETS"] = KeybindPrimaryKey.RightBracket,
            ["OEM6"] = KeybindPrimaryKey.RightBracket,
            ["]"] = KeybindPrimaryKey.RightBracket,
            ["}"] = KeybindPrimaryKey.RightBracket,
            ["^"] = KeybindPrimaryKey.RightBracket,
            ["BACKSLASH"] = KeybindPrimaryKey.Backslash,
            ["OEMPIPE"] = KeybindPrimaryKey.Backslash,
            ["OEM5"] = KeybindPrimaryKey.Backslash,
            ["\\"] = KeybindPrimaryKey.Backslash,
            ["|"] = KeybindPrimaryKey.Backslash,
            ["SEMICOLON"] = KeybindPrimaryKey.Semicolon,
            ["OEMSEMICOLON"] = KeybindPrimaryKey.Semicolon,
            ["OEM1"] = KeybindPrimaryKey.Semicolon,
            [";"] = KeybindPrimaryKey.Semicolon,
            [":"] = KeybindPrimaryKey.Semicolon,
            ["APOSTROPHE"] = KeybindPrimaryKey.Apostrophe,
            ["QUOTE"] = KeybindPrimaryKey.Apostrophe,
            ["OEMQUOTES"] = KeybindPrimaryKey.Apostrophe,
            ["OEM7"] = KeybindPrimaryKey.Apostrophe,
            ["'"] = KeybindPrimaryKey.Apostrophe,
            ["\""] = KeybindPrimaryKey.Apostrophe,
            ["GRAVE"] = KeybindPrimaryKey.Grave,
            ["BACKQUOTE"] = KeybindPrimaryKey.Grave,
            ["OEMTILDE"] = KeybindPrimaryKey.Grave,
            ["OEM3"] = KeybindPrimaryKey.Grave,
            ["`"] = KeybindPrimaryKey.Grave,
            ["~"] = KeybindPrimaryKey.Grave,
            ["COMMA"] = KeybindPrimaryKey.Comma,
            ["OEMCOMMA"] = KeybindPrimaryKey.Comma,
            [","] = KeybindPrimaryKey.Comma,
            ["PERIOD"] = KeybindPrimaryKey.Period,
            ["DOT"] = KeybindPrimaryKey.Period,
            ["OEMPERIOD"] = KeybindPrimaryKey.Period,
            ["."] = KeybindPrimaryKey.Period,
            ["SLASH"] = KeybindPrimaryKey.Slash,
            ["OEMQUESTION"] = KeybindPrimaryKey.Slash,
            ["OEM2"] = KeybindPrimaryKey.Slash,
            ["/"] = KeybindPrimaryKey.Slash,
            ["?"] = KeybindPrimaryKey.Slash,
            ["INTLBACKSLASH"] = KeybindPrimaryKey.IntlBackslash,
            ["NONUSBACKSLASH"] = KeybindPrimaryKey.IntlBackslash,
            ["OEMBACKSLASH"] = KeybindPrimaryKey.IntlBackslash,
            ["OEM102"] = KeybindPrimaryKey.IntlBackslash,
            ["ABNTC1"] = KeybindPrimaryKey.IntlBackslash,
            ["<"] = KeybindPrimaryKey.IntlBackslash,
            [">"] = KeybindPrimaryKey.IntlBackslash,
        };

    /// <summary>
    /// Attempts to parse a text expression such as <c>Alt+Shift+F2</c>.
    /// </summary>
    public static bool TryParse(string? text, out KeyCombination combination)
    {
        combination = new KeyCombination();
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string[] tokens = text.Split('+', StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
            return false;

        var parsed = new KeyCombination();

        foreach (string token in tokens)
        {
            if (string.IsNullOrWhiteSpace(token))
                return false;

            if (TryApplyModifierToken(parsed, token))
                continue;

            if (!TryParsePrimaryToken(token, out KeybindPrimaryKey key))
                return false;
            if (parsed.Key != KeybindPrimaryKey.None)
                return false;

            parsed.Key = key;
        }

        if (!IsValid(parsed))
            return false;

        combination = Normalize(parsed);
        return true;
    }

    /// <summary>
    /// Normalizes a combination.
    /// </summary>
    public static KeyCombination Normalize(KeyCombination combination)
    {
        ArgumentNullException.ThrowIfNull(combination);

        return new KeyCombination
        {
            Ctrl = combination.Ctrl,
            Alt = combination.Alt,
            Shift = combination.Shift,
            Meta = combination.Meta,
            Key = combination.Key,
        };
    }

    /// <summary>
    /// Returns whether a combination is valid.
    /// </summary>
    public static bool IsValid(KeyCombination? combination)
    {
        if (combination is null)
            return false;

        return combination.Key != KeybindPrimaryKey.None;
    }

    /// <summary>
    /// Converts a combination to canonical display form.
    /// </summary>
    public static string ToCanonicalString(KeyCombination? combination)
    {
        if (!IsValid(combination))
            return string.Empty;

        var tokens = new List<string>(capacity: 5);
        if (combination!.Ctrl)
            tokens.Add("Ctrl");
        if (combination.Alt)
            tokens.Add("Alt");
        if (combination.Shift)
            tokens.Add("Shift");
        if (combination.Meta)
            tokens.Add("Meta");

        tokens.Add(ToPrimaryDisplayToken(combination.Key));
        return string.Join('+', tokens);
    }

    /// <summary>
    /// Attempts to parse one primary key token.
    /// </summary>
    public static bool TryParsePrimaryToken(string token, out KeybindPrimaryKey key)
    {
        ArgumentNullException.ThrowIfNull(token);

        key = KeybindPrimaryKey.None;
        string normalizedToken = token.Trim();
        if (normalizedToken.Length == 0)
            return false;
        string normalizedTokenUpper = normalizedToken.ToUpperInvariant();

        if (normalizedTokenUpper.Length == 1)
        {
            char value = normalizedTokenUpper[0];
            if (value is >= 'A' and <= 'Z')
            {
                key = (KeybindPrimaryKey)((int)KeybindPrimaryKey.A + (value - 'A'));
                return true;
            }

            if (value is >= '0' and <= '9')
            {
                key = (KeybindPrimaryKey)((int)KeybindPrimaryKey.D0 + (value - '0'));
                return true;
            }
        }

        if (normalizedTokenUpper.StartsWith("F", StringComparison.Ordinal))
        {
            if (
                int.TryParse(
                    normalizedTokenUpper[1..],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int functionNumber
                )
            )
            {
                if (functionNumber is >= 1 and <= 24)
                {
                    key = (KeybindPrimaryKey)((int)KeybindPrimaryKey.F1 + (functionNumber - 1));
                    return true;
                }
            }
        }

        if (normalizedTokenUpper.StartsWith("D", StringComparison.Ordinal))
        {
            if (normalizedTokenUpper.Length == 2 && normalizedTokenUpper[1] is >= '0' and <= '9')
            {
                key = (KeybindPrimaryKey)((int)KeybindPrimaryKey.D0 + (normalizedTokenUpper[1] - '0'));
                return true;
            }
        }

        if (normalizedTokenUpper.StartsWith("DIGIT", StringComparison.Ordinal))
        {
            if (normalizedTokenUpper.Length == 6 && normalizedTokenUpper[5] is >= '0' and <= '9')
            {
                key = (KeybindPrimaryKey)((int)KeybindPrimaryKey.D0 + (normalizedTokenUpper[5] - '0'));
                return true;
            }
        }

        if (TryParseNamedPrimaryKey(normalizedToken, out key))
            return true;

        return false;
    }

    /// <summary>
    /// Converts a primary key to its canonical display token.
    /// </summary>
    public static string ToPrimaryDisplayToken(KeybindPrimaryKey key)
    {
        if (key is >= KeybindPrimaryKey.A and <= KeybindPrimaryKey.Z)
            return ((char)('A' + (int)(key - KeybindPrimaryKey.A))).ToString();

        if (key is >= KeybindPrimaryKey.D0 and <= KeybindPrimaryKey.D9)
            return ((char)('0' + (int)(key - KeybindPrimaryKey.D0))).ToString();

        if (key is >= KeybindPrimaryKey.F1 and <= KeybindPrimaryKey.F24)
            return $"F{(int)(key - KeybindPrimaryKey.F1) + 1}";

        return key switch
        {
            KeybindPrimaryKey.None => string.Empty,
            KeybindPrimaryKey.Escape => "Esc",
            KeybindPrimaryKey.Tab => "Tab",
            KeybindPrimaryKey.Backspace => "Backspace",
            KeybindPrimaryKey.Enter => "Enter",
            KeybindPrimaryKey.Space => "Space",
            KeybindPrimaryKey.CapsLock => "CapsLock",
            KeybindPrimaryKey.NumLock => "NumLock",
            KeybindPrimaryKey.ScrollLock => "ScrollLock",
            KeybindPrimaryKey.Pause => "Pause",
            KeybindPrimaryKey.PrintScreen => "PrintScreen",
            KeybindPrimaryKey.Insert => "Insert",
            KeybindPrimaryKey.Delete => "Delete",
            KeybindPrimaryKey.Home => "Home",
            KeybindPrimaryKey.End => "End",
            KeybindPrimaryKey.PageUp => "PageUp",
            KeybindPrimaryKey.PageDown => "PageDown",
            KeybindPrimaryKey.Left => "Left",
            KeybindPrimaryKey.Up => "Up",
            KeybindPrimaryKey.Right => "Right",
            KeybindPrimaryKey.Down => "Down",
            KeybindPrimaryKey.Menu => "Menu",
            KeybindPrimaryKey.NumPad0 => "NumPad0",
            KeybindPrimaryKey.NumPad1 => "NumPad1",
            KeybindPrimaryKey.NumPad2 => "NumPad2",
            KeybindPrimaryKey.NumPad3 => "NumPad3",
            KeybindPrimaryKey.NumPad4 => "NumPad4",
            KeybindPrimaryKey.NumPad5 => "NumPad5",
            KeybindPrimaryKey.NumPad6 => "NumPad6",
            KeybindPrimaryKey.NumPad7 => "NumPad7",
            KeybindPrimaryKey.NumPad8 => "NumPad8",
            KeybindPrimaryKey.NumPad9 => "NumPad9",
            KeybindPrimaryKey.NumPadDecimal => "NumPadDecimal",
            KeybindPrimaryKey.NumPadAdd => "NumPadAdd",
            KeybindPrimaryKey.NumPadSubtract => "NumPadSubtract",
            KeybindPrimaryKey.NumPadMultiply => "NumPadMultiply",
            KeybindPrimaryKey.NumPadDivide => "NumPadDivide",
            KeybindPrimaryKey.NumPadEnter => "NumPadEnter",
            KeybindPrimaryKey.Minus => "Minus",
            KeybindPrimaryKey.Equal => "Equal",
            KeybindPrimaryKey.LeftBracket => "LeftBracket",
            KeybindPrimaryKey.RightBracket => "RightBracket",
            KeybindPrimaryKey.Backslash => "Backslash",
            KeybindPrimaryKey.Semicolon => "Semicolon",
            KeybindPrimaryKey.Apostrophe => "Apostrophe",
            KeybindPrimaryKey.Grave => "Grave",
            KeybindPrimaryKey.Comma => "Comma",
            KeybindPrimaryKey.Period => "Period",
            KeybindPrimaryKey.Slash => "Slash",
            KeybindPrimaryKey.IntlBackslash => "IntlBackslash",
            _ => key.ToString(),
        };
    }

    private static bool TryParseNamedPrimaryKey(string token, out KeybindPrimaryKey key)
    {
        if (IsDigitsOnly(token))
        {
            key = KeybindPrimaryKey.None;
            return false;
        }

        if (
            Enum.TryParse(token, ignoreCase: true, out key)
            && Enum.IsDefined(key)
            && key != KeybindPrimaryKey.None
        )
            return true;

        if (PrimaryKeyAliases.TryGetValue(token, out key))
            return true;

        string condensedToken = token
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);

        if (
            !string.Equals(condensedToken, token, StringComparison.Ordinal)
            && (
                (
                    Enum.TryParse(condensedToken, ignoreCase: true, out key)
                    && Enum.IsDefined(key)
                    && key != KeybindPrimaryKey.None
                )
                || PrimaryKeyAliases.TryGetValue(condensedToken, out key)
            )
        )
            return true;

        key = KeybindPrimaryKey.None;
        return false;
    }

    private static bool IsDigitsOnly(string value)
    {
        if (value.Length == 0)
            return false;

        foreach (char character in value)
        {
            if (!char.IsDigit(character))
                return false;
        }

        return true;
    }

    private static bool TryApplyModifierToken(KeyCombination combination, string token)
    {
        string normalizedToken = token.Trim().ToUpperInvariant();
        switch (normalizedToken)
        {
            case "CTRL":
            case "CONTROL":
                if (combination.Ctrl)
                    return false;
                combination.Ctrl = true;
                return true;
            case "ALT":
                if (combination.Alt)
                    return false;
                combination.Alt = true;
                return true;
            case "SHIFT":
                if (combination.Shift)
                    return false;
                combination.Shift = true;
                return true;
            case "META":
            case "WIN":
            case "WINDOWS":
            case "SUPER":
            case "CMD":
            case "COMMAND":
                if (combination.Meta)
                    return false;
                combination.Meta = true;
                return true;
            default:
                return false;
        }
    }
}
