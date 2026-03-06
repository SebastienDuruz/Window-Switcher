using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Utilities;
using Xunit;

namespace WindowSwitcher.Tests.Platform.Keybinds;

public sealed class KeyCombinationParserTests
{
    [Theory]
    [InlineData("Ctrl+A", "Ctrl+A")]
    [InlineData("alt+b", "Alt+B")]
    [InlineData("F1", "F1")]
    [InlineData("Alt+F1", "Alt+F1")]
    [InlineData("Shift+Alt+F2", "Alt+Shift+F2")]
    [InlineData("Alt+Shift+F2", "Alt+Shift+F2")]
    [InlineData("Ctrl+OemQuestion", "Ctrl+Slash")]
    [InlineData("Shift+?", "Shift+Slash")]
    [InlineData("Alt+LeftBrace", "Alt+LeftBracket")]
    [InlineData("Ctrl+Oem102", "Ctrl+IntlBackslash")]
    [InlineData("Ctrl+^", "Ctrl+RightBracket")]
    [InlineData("Ctrl+Return", "Ctrl+Enter")]
    [InlineData("Ctrl+PageDown", "Ctrl+PageDown")]
    [InlineData("Ctrl+Keypad1", "Ctrl+NumPad1")]
    public void TryParse_ReturnsCanonicalDisplay(string text, string expectedCanonical)
    {
        bool parsed = KeyCombinationParser.TryParse(text, out KeyCombination combination);

        Assert.True(parsed);
        Assert.Equal(expectedCanonical, KeyCombinationParser.ToCanonicalString(combination));
    }

    [Theory]
    [InlineData("Esc", KeybindPrimaryKey.Escape)]
    [InlineData("Back", KeybindPrimaryKey.Backspace)]
    [InlineData("Backquote", KeybindPrimaryKey.Grave)]
    [InlineData("BracketLeft", KeybindPrimaryKey.LeftBracket)]
    [InlineData("ContextMenu", KeybindPrimaryKey.Menu)]
    [InlineData("RightBrace", KeybindPrimaryKey.RightBracket)]
    [InlineData("Oem6", KeybindPrimaryKey.RightBracket)]
    [InlineData("Dot", KeybindPrimaryKey.Period)]
    [InlineData("OEM5", KeybindPrimaryKey.Backslash)]
    [InlineData("KeypadSlash", KeybindPrimaryKey.NumPadDivide)]
    [InlineData("IntlBackslash", KeybindPrimaryKey.IntlBackslash)]
    public void TryParsePrimaryToken_ParsesAliases(string token, KeybindPrimaryKey expectedKey)
    {
        bool parsed = KeyCombinationParser.TryParsePrimaryToken(token, out KeybindPrimaryKey key);

        Assert.True(parsed);
        Assert.Equal(expectedKey, key);
    }

    [Fact]
    public void TryParse_RejectsModifierOnlyCombination()
    {
        bool parsed = KeyCombinationParser.TryParse("Ctrl+Alt+Shift", out _);

        Assert.False(parsed);
    }

    [Fact]
    public void Equality_IgnoresInputOrderAfterNormalization()
    {
        Assert.True(KeyCombinationParser.TryParse("Alt+Shift+F2", out KeyCombination left));
        Assert.True(KeyCombinationParser.TryParse("Shift+Alt+F2", out KeyCombination right));

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }
}
