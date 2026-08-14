using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Models;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Models;

internal static class GlobalKeyboardEventMapper
{
    private const string LinuxPlatform = "Linux";
    private const string WindowsPlatform = "Windows";

    public static GlobalKeyEventArgs FromLinux(
        KeyEvent keyEvent,
        GlobalKeyState state,
        bool isRepeat
    )
    {
        ArgumentNullException.ThrowIfNull(keyEvent);

        return new GlobalKeyEventArgs
        {
            Platform = LinuxPlatform,
            KeyCode = $"KEY_{keyEvent.Code}",
            KeyName = keyEvent.Key.ToString(),
            State = state,
            IsRepeat = isRepeat,
            Timestamp = DateTimeOffset.UtcNow,
            DeviceId = keyEvent.DevicePath,
        };
    }

    public static GlobalKeyEventArgs FromWindows(
        uint virtualKeyCode,
        string? keyName,
        GlobalKeyState state,
        bool isRepeat
    )
    {
        return new GlobalKeyEventArgs
        {
            Platform = WindowsPlatform,
            KeyCode = $"VK_{virtualKeyCode:X2}",
            KeyName = string.IsNullOrWhiteSpace(keyName) ? null : keyName,
            State = state,
            IsRepeat = isRepeat,
            Timestamp = DateTimeOffset.UtcNow,
        };
    }
}
