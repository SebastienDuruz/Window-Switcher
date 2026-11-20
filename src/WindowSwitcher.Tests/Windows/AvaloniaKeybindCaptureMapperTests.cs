using Avalonia.Input;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;
using WindowSwitcher.ViewModels;
using WindowSwitcher.Windows.Keybinds;
using Xunit;

namespace WindowSwitcher.Tests.Windows;

public sealed class AvaloniaKeybindCaptureMapperTests
{
    [Fact]
    public void EveryPrimaryKey_CanBeCapturedFromAPhysicalKey()
    {
        HashSet<KeybindPrimaryKey> captured = Enum.GetValues<PhysicalKey>()
            .Select(physicalKey =>
                AvaloniaKeybindCaptureMapper.Create(
                    Key.None,
                    physicalKey,
                    keySymbol: null,
                    KeyModifiers.None
                )
            )
            .Where(result => result.Succeeded)
            .Select(result => result.Combination!.Key)
            .ToHashSet();

        KeybindPrimaryKey[] missing = Enum.GetValues<KeybindPrimaryKey>()
            .Where(key => key != KeybindPrimaryKey.None && !captured.Contains(key))
            .ToArray();
        Assert.Empty(missing);
    }

    [Fact]
    public void Create_PrefersPhysicalKey_SoShortcutsDoNotDependOnKeyboardLayout()
    {
        // On an AZERTY layout the physical Q position produces the logical A key.
        KeybindCaptureResult result = AvaloniaKeybindCaptureMapper.Create(
            Key.A,
            PhysicalKey.Q,
            keySymbol: "a",
            KeyModifiers.None
        );

        Assert.True(result.Succeeded);
        Assert.Equal(KeybindPrimaryKey.Q, result.Combination!.Key);
    }

    [Fact]
    public void Create_FallsBackToLogicalKey_WhenPhysicalKeyIsUnknown()
    {
        KeybindCaptureResult result = AvaloniaKeybindCaptureMapper.Create(
            Key.F5,
            PhysicalKey.None,
            keySymbol: null,
            KeyModifiers.None
        );

        Assert.Equal(KeybindPrimaryKey.F5, result.Combination!.Key);
    }

    [Fact]
    public void Create_FallsBackToKeySymbol_WhenKeysAreNotRecognized()
    {
        KeybindCaptureResult result = AvaloniaKeybindCaptureMapper.Create(
            Key.None,
            PhysicalKey.None,
            keySymbol: " / ",
            KeyModifiers.None
        );

        Assert.Equal(KeybindPrimaryKey.Slash, result.Combination!.Key);
    }

    [Fact]
    public void Create_CopiesEveryModifier()
    {
        KeybindCaptureResult result = AvaloniaKeybindCaptureMapper.Create(
            Key.K,
            PhysicalKey.K,
            keySymbol: "k",
            KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Meta
        );

        KeyCombination combination = result.Combination!;
        Assert.True(combination.Ctrl);
        Assert.True(combination.Alt);
        Assert.True(combination.Shift);
        Assert.True(combination.Meta);
        Assert.Equal(KeybindPrimaryKey.K, combination.Key);
    }

    [Theory]
    [InlineData(Key.LeftCtrl, PhysicalKey.None)]
    [InlineData(Key.RWin, PhysicalKey.None)]
    [InlineData(Key.None, PhysicalKey.ShiftLeft)]
    [InlineData(Key.None, PhysicalKey.AltRight)]
    [InlineData(Key.A, PhysicalKey.MetaLeft)]
    public void Create_RejectsModifierOnlyKeys(Key key, PhysicalKey physicalKey)
    {
        KeybindCaptureResult result = AvaloniaKeybindCaptureMapper.Create(
            key,
            physicalKey,
            keySymbol: null,
            KeyModifiers.Control
        );

        Assert.False(result.Succeeded);
        Assert.Null(result.Combination);
        Assert.Equal("A shortcut must contain a primary key.", result.Message);
    }

    [Fact]
    public void Create_RejectsUnsupportedKeys()
    {
        KeybindCaptureResult result = AvaloniaKeybindCaptureMapper.Create(
            Key.VolumeUp,
            PhysicalKey.AudioVolumeUp,
            keySymbol: null,
            KeyModifiers.None
        );

        Assert.False(result.Succeeded);
        Assert.Equal("Unsupported key. Use any non-modifier keyboard key.", result.Message);
    }
}
