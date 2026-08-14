using System.Collections.Frozen;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Models;

/// <summary>
/// Immutable capability snapshot read from evdev ioctl bitsets for one input device.
/// </summary>
internal sealed record InputDeviceCapabilities
{
    /// <summary>
    /// Empty capability instance used when probing fails or data is unavailable.
    /// </summary>
    public static InputDeviceCapabilities Empty { get; } = new();

    /// <summary>
    /// Supported event types (for example <c>EV_KEY</c>, <c>EV_REL</c>, <c>EV_ABS</c>).
    /// </summary>
    public IReadOnlySet<ushort> EventTypes { get; init; }

    /// <summary>
    /// Supported key/button codes when <c>EV_KEY</c> is present.
    /// </summary>
    public IReadOnlySet<ushort> KeyCodes { get; init; }

    /// <summary>
    /// Supported relative axes when <c>EV_REL</c> is present.
    /// </summary>
    public IReadOnlySet<ushort> RelativeAxes { get; init; }

    /// <summary>
    /// Supported absolute axes when <c>EV_ABS</c> is present.
    /// </summary>
    public IReadOnlySet<ushort> AbsoluteAxes { get; init; }

    /// <summary>
    /// Creates an immutable capability snapshot.
    /// </summary>
    /// <param name="eventTypes">Event type codes; <see langword="null"/> produces an empty set.</param>
    /// <param name="keyCodes">Key/button codes; <see langword="null"/> produces an empty set.</param>
    /// <param name="relativeAxes">Relative axis codes; <see langword="null"/> produces an empty set.</param>
    /// <param name="absoluteAxes">Absolute axis codes; <see langword="null"/> produces an empty set.</param>
    public InputDeviceCapabilities(
        IEnumerable<ushort>? eventTypes = null,
        IEnumerable<ushort>? keyCodes = null,
        IEnumerable<ushort>? relativeAxes = null,
        IEnumerable<ushort>? absoluteAxes = null
    )
    {
        // Frozen sets avoid accidental mutation and provide efficient repeated lookups.
        EventTypes = (eventTypes ?? []).ToFrozenSet();
        KeyCodes = (keyCodes ?? []).ToFrozenSet();
        RelativeAxes = (relativeAxes ?? []).ToFrozenSet();
        AbsoluteAxes = (absoluteAxes ?? []).ToFrozenSet();
    }
}
