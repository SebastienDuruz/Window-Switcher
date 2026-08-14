using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Runtime;
using Xunit;

namespace WindowSwitcher.Tests.Platform.Keybinds;

public sealed class KeybindPressedStateTrackerTests
{
    [Fact]
    public void TryGetTriggeredCombination_TriggersCtrlA_OnWindowsDownEvents()
    {
        var tracker = new KeybindPressedStateTracker();

        bool ctrlTriggered = tracker.TryGetTriggeredCombination(
            CreateWindowsEvent("VK_11", GlobalKeyState.Down),
            out _
        );
        bool aTriggered = tracker.TryGetTriggeredCombination(
            CreateWindowsEvent("VK_41", GlobalKeyState.Down),
            out KeyCombination combination
        );

        Assert.False(ctrlTriggered);
        Assert.True(aTriggered);
        Assert.True(combination.Ctrl);
        Assert.False(combination.Alt);
        Assert.Equal(KeybindPrimaryKey.A, combination.Key);
    }

    [Fact]
    public void TryGetTriggeredCombination_IgnoresRepeatedDownEvent()
    {
        var tracker = new KeybindPressedStateTracker();

        bool first = tracker.TryGetTriggeredCombination(
            CreateWindowsEvent("VK_70", GlobalKeyState.Down),
            out _
        );
        bool repeated = tracker.TryGetTriggeredCombination(
            CreateWindowsEvent("VK_70", GlobalKeyState.Down, isRepeat: true),
            out _
        );

        Assert.True(first);
        Assert.False(repeated);
    }

    [Fact]
    public void TryGetTriggeredCombination_HandlesLinuxModifierOrder()
    {
        var tracker = new KeybindPressedStateTracker();

        _ = tracker.TryGetTriggeredCombination(
            CreateLinuxEvent("LeftShift", GlobalKeyState.Down),
            out _
        );
        _ = tracker.TryGetTriggeredCombination(
            CreateLinuxEvent("LeftAlt", GlobalKeyState.Down),
            out _
        );
        bool triggered = tracker.TryGetTriggeredCombination(
            CreateLinuxEvent("F2", GlobalKeyState.Down),
            out KeyCombination combination
        );

        Assert.True(triggered);
        Assert.False(combination.Ctrl);
        Assert.True(combination.Alt);
        Assert.True(combination.Shift);
        Assert.Equal(KeybindPrimaryKey.F2, combination.Key);
    }

    [Fact]
    public void TryGetTriggeredCombination_ResolvesWindowsOemQuestion()
    {
        var tracker = new KeybindPressedStateTracker();

        bool triggered = tracker.TryGetTriggeredCombination(
            CreateWindowsEvent("VK_BF", GlobalKeyState.Down),
            out KeyCombination combination
        );

        Assert.True(triggered);
        Assert.Equal(KeybindPrimaryKey.Slash, combination.Key);
    }

    [Fact]
    public void TryGetTriggeredCombination_ResolvesLinuxIntlBackslash()
    {
        var tracker = new KeybindPressedStateTracker();

        bool triggered = tracker.TryGetTriggeredCombination(
            CreateLinuxEvent("IntlBackslash", GlobalKeyState.Down),
            out KeyCombination combination
        );

        Assert.True(triggered);
        Assert.Equal(KeybindPrimaryKey.IntlBackslash, combination.Key);
    }

    [Fact]
    public void TryGetTriggeredCombination_IgnoresUnknownKeys()
    {
        var tracker = new KeybindPressedStateTracker();

        bool triggered = tracker.TryGetTriggeredCombination(
            new GlobalKeyEventArgs
            {
                Platform = "Linux",
                KeyCode = "KEY_999",
                KeyName = "UnknownKey",
                State = GlobalKeyState.Down,
            },
            out _
        );

        Assert.False(triggered);
    }

    private static GlobalKeyEventArgs CreateWindowsEvent(
        string keyCode,
        GlobalKeyState state,
        bool isRepeat = false
    )
    {
        return new GlobalKeyEventArgs
        {
            Platform = "Windows",
            KeyCode = keyCode,
            State = state,
            IsRepeat = isRepeat,
        };
    }

    private static GlobalKeyEventArgs CreateLinuxEvent(string keyName, GlobalKeyState state)
    {
        return new GlobalKeyEventArgs
        {
            Platform = "Linux",
            KeyCode = "KEY",
            KeyName = keyName,
            State = state,
        };
    }
}
