namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Models;

/// <summary>
/// Base type for decoded events emitted by the input service.
/// </summary>
/// <param name="Timestamp">Event timestamp interpreted as UTC from kernel <c>timeval</c>.</param>
/// <param name="DevicePath">Source device path, usually <c>/dev/input/eventX</c>.</param>
/// <param name="Type">Raw Linux event type code (<c>EV_*</c>).</param>
/// <param name="Code">Raw Linux event code whose meaning depends on <paramref name="Type"/>.</param>
/// <param name="Value">Raw Linux event value whose meaning depends on <paramref name="Type"/> and <paramref name="Code"/>.</param>
internal abstract record InputEvent(
    DateTimeOffset Timestamp,
    string DevicePath,
    ushort Type,
    ushort Code,
    int Value
);

/// <summary>
/// Event that did not map to a specialized higher-level record.
/// </summary>
internal sealed record RawInputEvent(
    DateTimeOffset Timestamp,
    string DevicePath,
    ushort Type,
    ushort Code,
    int Value
) : InputEvent(Timestamp, DevicePath, Type, Code, Value);

/// <summary>
/// Decoded keyboard/button event with normalized key state.
/// </summary>
internal sealed record KeyEvent(
    DateTimeOffset Timestamp,
    string DevicePath,
    ushort Type,
    ushort Code,
    int Value,
    KeyCode Key,
    KeyState State
) : InputEvent(Timestamp, DevicePath, Type, Code, Value);

/// <summary>
/// Synchronization event (<c>EV_SYN</c>) that marks frame boundaries in evdev streams.
/// </summary>
internal sealed record SyncEvent(
    DateTimeOffset Timestamp,
    string DevicePath,
    ushort Type,
    ushort Code,
    int Value
) : InputEvent(Timestamp, DevicePath, Type, Code, Value);

/// <summary>
/// Relative axis event (<c>EV_REL</c>), typically mouse movement or wheel deltas.
/// </summary>
internal sealed record RelativeAxisEvent(
    DateTimeOffset Timestamp,
    string DevicePath,
    ushort Type,
    ushort Code,
    int Value
) : InputEvent(Timestamp, DevicePath, Type, Code, Value);

/// <summary>
/// Absolute axis event (<c>EV_ABS</c>), typically touch or gamepad position updates.
/// </summary>
internal sealed record AbsoluteAxisEvent(
    DateTimeOffset Timestamp,
    string DevicePath,
    ushort Type,
    ushort Code,
    int Value
) : InputEvent(Timestamp, DevicePath, Type, Code, Value);
