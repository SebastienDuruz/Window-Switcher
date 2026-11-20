using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Models;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Windows;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Runtime;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Utilities;
using Xunit;

namespace WindowSwitcher.Tests.Platform.Keybinds;

/// <summary>
/// Checks that a shortcut stored as text resolves to the same key on every platform listener.
/// Expected codes come from <c>linux/input-event-codes.h</c> and the Windows virtual-key table,
/// not from the production enums, so a wrong enum value is caught as well.
/// </summary>
public sealed class KeyMappingConsistencyTests
{
    private static readonly IReadOnlyList<KeybindPrimaryKey> PrimaryKeys =
        Enum.GetValues<KeybindPrimaryKey>().Where(key => key != KeybindPrimaryKey.None).ToArray();

    public static TheoryData<KeybindPrimaryKey> AllPrimaryKeys => new(PrimaryKeys);

    [Theory]
    [MemberData(nameof(AllPrimaryKeys))]
    public void DisplayToken_ParsesBackToTheSameKey(KeybindPrimaryKey key)
    {
        string token = KeyCombinationParser.ToPrimaryDisplayToken(key);

        Assert.True(KeyCombinationParser.TryParsePrimaryToken(token, out KeybindPrimaryKey parsed));
        Assert.Equal(key, parsed);
    }

    [Fact]
    public void EveryPrimaryKey_IsReachableFromALinuxKeyCode()
    {
        HashSet<KeybindPrimaryKey> reachable = Enum.GetValues<KeyCode>()
            .Select(code => ResolveLinux((ushort)code))
            .Where(resolved => resolved is { Modifier: null })
            .Select(resolved => resolved!.Value.PrimaryKey)
            .ToHashSet();

        Assert.Empty(PrimaryKeys.Except(reachable));
    }

    [Fact]
    public void EveryPrimaryKey_IsReachableThroughTheWindowsHook()
    {
        var processor = new WindowsKeyboardHookProcessor(_ => null, Serilog.Core.Logger.None);
        HashSet<KeybindPrimaryKey> reachable = Enumerable
            .Range(0, 256)
            .SelectMany(virtualKey =>
                new[] { 0u, WindowsKeyboardHookProcessor.LlkhfExtended }.Select(flags =>
                    processor.TryCreateEvent(
                        WindowsKeyboardHookProcessor.WmKeyDown,
                        (uint)virtualKey,
                        scanCode: 0,
                        flags
                    )
                )
            )
            .Select(keyEvent =>
                GlobalKeyEventKeyResolver.TryResolve(keyEvent!, out ResolvedGlobalKeyEvent resolved)
                    ? resolved
                    : (ResolvedGlobalKeyEvent?)null
            )
            .Where(resolved => resolved is { Modifier: null })
            .Select(resolved => resolved!.Value.PrimaryKey)
            .ToHashSet();

        Assert.Empty(PrimaryKeys.Except(reachable));
    }

    [Theory]
    [InlineData(false, KeybindPrimaryKey.Enter)]
    [InlineData(true, KeybindPrimaryKey.NumPadEnter)]
    public void WindowsReturnKey_IsSplitByTheExtendedFlag(
        bool isExtended,
        KeybindPrimaryKey expected
    )
    {
        Assert.Equal(expected, ResolveWindows(0x0D, isExtended)?.PrimaryKey);
    }

    [Theory]
    [InlineData(1, KeybindPrimaryKey.Escape)]
    [InlineData(2, KeybindPrimaryKey.D1)]
    [InlineData(11, KeybindPrimaryKey.D0)]
    [InlineData(12, KeybindPrimaryKey.Minus)]
    [InlineData(13, KeybindPrimaryKey.Equal)]
    [InlineData(14, KeybindPrimaryKey.Backspace)]
    [InlineData(15, KeybindPrimaryKey.Tab)]
    [InlineData(16, KeybindPrimaryKey.Q)]
    [InlineData(26, KeybindPrimaryKey.LeftBracket)]
    [InlineData(27, KeybindPrimaryKey.RightBracket)]
    [InlineData(28, KeybindPrimaryKey.Enter)]
    [InlineData(39, KeybindPrimaryKey.Semicolon)]
    [InlineData(40, KeybindPrimaryKey.Apostrophe)]
    [InlineData(41, KeybindPrimaryKey.Grave)]
    [InlineData(43, KeybindPrimaryKey.Backslash)]
    [InlineData(51, KeybindPrimaryKey.Comma)]
    [InlineData(52, KeybindPrimaryKey.Period)]
    [InlineData(53, KeybindPrimaryKey.Slash)]
    [InlineData(55, KeybindPrimaryKey.NumPadMultiply)]
    [InlineData(57, KeybindPrimaryKey.Space)]
    [InlineData(58, KeybindPrimaryKey.CapsLock)]
    [InlineData(59, KeybindPrimaryKey.F1)]
    [InlineData(68, KeybindPrimaryKey.F10)]
    [InlineData(69, KeybindPrimaryKey.NumLock)]
    [InlineData(70, KeybindPrimaryKey.ScrollLock)]
    [InlineData(71, KeybindPrimaryKey.NumPad7)]
    [InlineData(74, KeybindPrimaryKey.NumPadSubtract)]
    [InlineData(78, KeybindPrimaryKey.NumPadAdd)]
    [InlineData(82, KeybindPrimaryKey.NumPad0)]
    [InlineData(83, KeybindPrimaryKey.NumPadDecimal)]
    [InlineData(86, KeybindPrimaryKey.IntlBackslash)]
    [InlineData(87, KeybindPrimaryKey.F11)]
    [InlineData(88, KeybindPrimaryKey.F12)]
    [InlineData(96, KeybindPrimaryKey.NumPadEnter)]
    [InlineData(98, KeybindPrimaryKey.NumPadDivide)]
    [InlineData(99, KeybindPrimaryKey.PrintScreen)]
    [InlineData(102, KeybindPrimaryKey.Home)]
    [InlineData(103, KeybindPrimaryKey.Up)]
    [InlineData(104, KeybindPrimaryKey.PageUp)]
    [InlineData(105, KeybindPrimaryKey.Left)]
    [InlineData(106, KeybindPrimaryKey.Right)]
    [InlineData(107, KeybindPrimaryKey.End)]
    [InlineData(108, KeybindPrimaryKey.Down)]
    [InlineData(109, KeybindPrimaryKey.PageDown)]
    [InlineData(110, KeybindPrimaryKey.Insert)]
    [InlineData(111, KeybindPrimaryKey.Delete)]
    [InlineData(117, KeybindPrimaryKey.Equal)] // KEY_KPEQUAL, captured as Equal
    [InlineData(119, KeybindPrimaryKey.Pause)]
    [InlineData(121, KeybindPrimaryKey.NumPadDecimal)] // KEY_KPCOMMA, captured as NumPadDecimal
    [InlineData(127, KeybindPrimaryKey.Menu)] // KEY_COMPOSE: the PC context-menu key
    [InlineData(139, KeybindPrimaryKey.Menu)]
    [InlineData(183, KeybindPrimaryKey.F13)]
    [InlineData(194, KeybindPrimaryKey.F24)]
    public void LinuxKernelKeyCode_ResolvesToExpectedPrimaryKey(
        int kernelCode,
        KeybindPrimaryKey expected
    )
    {
        ResolvedGlobalKeyEvent? resolved = ResolveLinux((ushort)kernelCode);

        Assert.NotNull(resolved);
        Assert.Null(resolved.Value.Modifier);
        Assert.Equal(expected, resolved.Value.PrimaryKey);
    }

    [Theory]
    [InlineData(29, "Ctrl")]
    [InlineData(97, "Ctrl")]
    [InlineData(42, "Shift")]
    [InlineData(54, "Shift")]
    [InlineData(56, "Alt")]
    [InlineData(100, "Alt")]
    [InlineData(125, "Meta")]
    [InlineData(126, "Meta")]
    public void LinuxModifierKeyCode_ResolvesToModifier(int kernelCode, string expectedModifier)
    {
        ResolvedGlobalKeyEvent? resolved = ResolveLinux((ushort)kernelCode);

        Assert.NotNull(resolved);
        Assert.Equal(Enum.Parse<KeybindModifier>(expectedModifier), resolved.Value.Modifier);
    }

    [Theory]
    [InlineData(113)] // KEY_MUTE
    [InlineData(115)] // KEY_VOLUMEUP
    [InlineData(158)] // KEY_BACK, not Backspace
    [InlineData(164)] // KEY_PLAYPAUSE
    [InlineData(0x110)] // BTN_LEFT
    public void LinuxNonKeyboardCode_DoesNotResolve(int kernelCode)
    {
        Assert.Null(ResolveLinux((ushort)kernelCode));
    }

    [Theory]
    [InlineData(0x41, KeybindPrimaryKey.A)]
    [InlineData(0x5A, KeybindPrimaryKey.Z)]
    [InlineData(0x30, KeybindPrimaryKey.D0)]
    [InlineData(0x39, KeybindPrimaryKey.D9)]
    [InlineData(0x70, KeybindPrimaryKey.F1)]
    [InlineData(0x87, KeybindPrimaryKey.F24)]
    [InlineData(0x08, KeybindPrimaryKey.Backspace)]
    [InlineData(0x0D, KeybindPrimaryKey.Enter)]
    [InlineData(0x1B, KeybindPrimaryKey.Escape)]
    [InlineData(0x13, KeybindPrimaryKey.Pause)]
    [InlineData(0x2C, KeybindPrimaryKey.PrintScreen)]
    [InlineData(0x5D, KeybindPrimaryKey.Menu)]
    [InlineData(0x60, KeybindPrimaryKey.NumPad0)]
    [InlineData(0x69, KeybindPrimaryKey.NumPad9)]
    [InlineData(0x6A, KeybindPrimaryKey.NumPadMultiply)]
    [InlineData(0x6B, KeybindPrimaryKey.NumPadAdd)]
    [InlineData(0x6D, KeybindPrimaryKey.NumPadSubtract)]
    [InlineData(0x6E, KeybindPrimaryKey.NumPadDecimal)]
    [InlineData(0x6F, KeybindPrimaryKey.NumPadDivide)]
    [InlineData(0xBA, KeybindPrimaryKey.Semicolon)]
    [InlineData(0xBB, KeybindPrimaryKey.Equal)]
    [InlineData(0xBC, KeybindPrimaryKey.Comma)]
    [InlineData(0xBD, KeybindPrimaryKey.Minus)]
    [InlineData(0xBE, KeybindPrimaryKey.Period)]
    [InlineData(0xBF, KeybindPrimaryKey.Slash)]
    [InlineData(0xC0, KeybindPrimaryKey.Grave)]
    [InlineData(0xDB, KeybindPrimaryKey.LeftBracket)]
    [InlineData(0xDC, KeybindPrimaryKey.Backslash)]
    [InlineData(0xDD, KeybindPrimaryKey.RightBracket)]
    [InlineData(0xDE, KeybindPrimaryKey.Apostrophe)]
    [InlineData(0xE2, KeybindPrimaryKey.IntlBackslash)]
    public void WindowsVirtualKey_ResolvesToExpectedPrimaryKey(
        int virtualKey,
        KeybindPrimaryKey expected
    )
    {
        ResolvedGlobalKeyEvent? resolved = ResolveWindows(virtualKey);

        Assert.NotNull(resolved);
        Assert.Null(resolved.Value.Modifier);
        Assert.Equal(expected, resolved.Value.PrimaryKey);
    }

    [Theory]
    [InlineData(0x10, "Shift")]
    [InlineData(0xA0, "Shift")]
    [InlineData(0xA1, "Shift")]
    [InlineData(0x11, "Ctrl")]
    [InlineData(0xA2, "Ctrl")]
    [InlineData(0xA3, "Ctrl")]
    [InlineData(0x12, "Alt")]
    [InlineData(0xA4, "Alt")]
    [InlineData(0xA5, "Alt")]
    [InlineData(0x5B, "Meta")]
    [InlineData(0x5C, "Meta")]
    public void WindowsModifierVirtualKey_ResolvesToModifier(
        int virtualKey,
        string expectedModifier
    )
    {
        ResolvedGlobalKeyEvent? resolved = ResolveWindows(virtualKey);

        Assert.NotNull(resolved);
        Assert.Equal(Enum.Parse<KeybindModifier>(expectedModifier), resolved.Value.Modifier);
    }

    [Theory]
    [InlineData(0x01)] // VK_LBUTTON
    [InlineData(0x5F)] // VK_SLEEP
    [InlineData(0xAD)] // VK_VOLUME_MUTE
    [InlineData(0xB3)] // VK_MEDIA_PLAY_PAUSE
    public void WindowsNonKeyboardVirtualKey_DoesNotResolve(int virtualKey)
    {
        Assert.Null(ResolveWindows(virtualKey));
    }

    [Theory]
    [InlineData("VK_")]
    [InlineData("VK_ZZ")]
    [InlineData("KEY_41")]
    [InlineData("")]
    public void WindowsMalformedKeyCode_DoesNotResolve(string keyCode)
    {
        var keyEvent = new GlobalKeyEventArgs
        {
            Platform = "Windows",
            KeyCode = keyCode,
            State = GlobalKeyState.Down,
        };

        Assert.False(GlobalKeyEventKeyResolver.TryResolve(keyEvent, out _));
    }

    [Theory]
    [InlineData("158")] // numeric text would otherwise parse as KEY_BACK
    [InlineData("-1")]
    [InlineData("ContextMenu")] // an Avalonia name, not an evdev name
    [InlineData("UnknownKey")]
    [InlineData("  ")]
    public void LinuxKeyName_ThatIsNotAnEvdevName_DoesNotResolve(string keyName)
    {
        var keyEvent = new GlobalKeyEventArgs
        {
            Platform = "Linux",
            KeyCode = "KEY",
            KeyName = keyName,
            State = GlobalKeyState.Down,
        };

        Assert.False(GlobalKeyEventKeyResolver.TryResolve(keyEvent, out _));
    }

    [Fact]
    public void UnknownPlatform_DoesNotResolve()
    {
        var keyEvent = new GlobalKeyEventArgs
        {
            Platform = "MacOS",
            KeyCode = "VK_41",
            KeyName = "A",
            State = GlobalKeyState.Down,
        };

        Assert.False(GlobalKeyEventKeyResolver.TryResolve(keyEvent, out _));
    }

    [Fact]
    public void Resolve_PreservesStateAndRepeatFlag()
    {
        var keyEvent = new GlobalKeyEventArgs
        {
            Platform = "Windows",
            KeyCode = "VK_41",
            State = GlobalKeyState.Up,
            IsRepeat = true,
        };

        Assert.True(
            GlobalKeyEventKeyResolver.TryResolve(keyEvent, out ResolvedGlobalKeyEvent resolved)
        );
        Assert.Equal(GlobalKeyState.Up, resolved.State);
        Assert.True(resolved.IsRepeat);
    }

    private static ResolvedGlobalKeyEvent? ResolveLinux(ushort kernelCode)
    {
        GlobalKeyEventArgs keyEvent = GlobalKeyboardEventMapper.FromLinux(
            new KeyEvent(
                DateTimeOffset.UnixEpoch,
                "/dev/input/event0",
                Type: 1,
                kernelCode,
                Value: 1,
                (KeyCode)kernelCode,
                KeyState.Down
            ),
            GlobalKeyState.Down,
            isRepeat: false
        );

        return GlobalKeyEventKeyResolver.TryResolve(keyEvent, out ResolvedGlobalKeyEvent resolved)
            ? resolved
            : null;
    }

    private static ResolvedGlobalKeyEvent? ResolveWindows(int virtualKey) =>
        ResolveWindows(virtualKey, isExtended: false);

    private static ResolvedGlobalKeyEvent? ResolveWindows(int virtualKey, bool isExtended)
    {
        GlobalKeyEventArgs keyEvent = GlobalKeyboardEventMapper.FromWindows(
            (uint)virtualKey,
            keyName: null,
            GlobalKeyState.Down,
            isRepeat: false,
            isExtended
        );

        return GlobalKeyEventKeyResolver.TryResolve(keyEvent, out ResolvedGlobalKeyEvent resolved)
            ? resolved
            : null;
    }
}
