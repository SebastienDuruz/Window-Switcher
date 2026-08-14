using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Linux;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Models;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore;

/// <summary>
/// Maps native Linux input events to typed managed event records.
/// </summary>
internal sealed class EventDecoder
{
    /// <summary>
    /// Decodes one native event into the closest high-level model.
    /// </summary>
    /// <param name="devicePath">Source evdev path.</param>
    /// <param name="native">Native event payload read from the kernel stream.</param>
    /// <returns>A typed event record preserving raw type/code/value fields.</returns>
    public InputEvent Decode(string devicePath, NativeInputEvent native)
    {
        var timestamp = ToTimestamp(native.Seconds, native.Microseconds);

        return native.Type switch
        {
            LinuxInputConstants.EvKey => new KeyEvent(
                timestamp,
                devicePath,
                native.Type,
                native.Code,
                native.Value,
                (KeyCode)native.Code,
                ToKeyState(native.Value)
            ),

            LinuxInputConstants.EvRel => new RelativeAxisEvent(
                timestamp,
                devicePath,
                native.Type,
                native.Code,
                native.Value
            ),

            LinuxInputConstants.EvAbs => new AbsoluteAxisEvent(
                timestamp,
                devicePath,
                native.Type,
                native.Code,
                native.Value
            ),

            LinuxInputConstants.EvSyn => new SyncEvent(
                timestamp,
                devicePath,
                native.Type,
                native.Code,
                native.Value
            ),

            _ => new RawInputEvent(timestamp, devicePath, native.Type, native.Code, native.Value),
        };
    }

    private static DateTimeOffset ToTimestamp(long seconds, long microseconds)
    {
        try
        {
            // Linux timeval uses microseconds; DateTimeOffset ticks are 100 ns units.
            return DateTimeOffset.FromUnixTimeSeconds(seconds).AddTicks(microseconds * 10);
        }
        catch (ArgumentOutOfRangeException)
        {
            // Defensive fallback for malformed timestamps from faulty drivers.
            return DateTimeOffset.UtcNow;
        }
    }

    private static KeyState ToKeyState(int value)
    {
        return value switch
        {
            0 => KeyState.Up,
            1 => KeyState.Down,
            2 => KeyState.Repeat,
            _ => KeyState.Unknown,
        };
    }
}
