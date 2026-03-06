using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Models;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;
using Xunit;

namespace WindowSwitcher.Tests.Platform.Input;

public sealed class GlobalKeyboardEventMapperTests
{
    [Fact]
    public void FromLinux_MapsExpectedFields()
    {
        var inputEvent = new KeyEvent(
            DateTimeOffset.UnixEpoch,
            "/dev/input/event7",
            1,
            30,
            1,
            KeyCode.A,
            KeyState.Down
        );
        DateTimeOffset before = DateTimeOffset.UtcNow;

        GlobalKeyEventArgs mapped = GlobalKeyboardEventMapper.FromLinux(
            inputEvent,
            GlobalKeyState.Down,
            isRepeat: false
        );
        DateTimeOffset after = DateTimeOffset.UtcNow;

        Assert.Equal("Linux", mapped.Platform);
        Assert.Equal("KEY_30", mapped.KeyCode);
        Assert.Equal("A", mapped.KeyName);
        Assert.Equal(GlobalKeyState.Down, mapped.State);
        Assert.False(mapped.IsRepeat);
        Assert.Equal("/dev/input/event7", mapped.DeviceId);
        Assert.InRange(mapped.Timestamp, before, after);
    }

    [Fact]
    public void FromWindows_MapsExpectedFields()
    {
        DateTimeOffset before = DateTimeOffset.UtcNow;

        GlobalKeyEventArgs mapped = GlobalKeyboardEventMapper.FromWindows(
            virtualKeyCode: 0x41,
            keyName: "A",
            state: GlobalKeyState.Up,
            isRepeat: true
        );
        DateTimeOffset after = DateTimeOffset.UtcNow;

        Assert.Equal("Windows", mapped.Platform);
        Assert.Equal("VK_41", mapped.KeyCode);
        Assert.Equal("A", mapped.KeyName);
        Assert.Equal(GlobalKeyState.Up, mapped.State);
        Assert.True(mapped.IsRepeat);
        Assert.Null(mapped.DeviceId);
        Assert.InRange(mapped.Timestamp, before, after);
    }
}
