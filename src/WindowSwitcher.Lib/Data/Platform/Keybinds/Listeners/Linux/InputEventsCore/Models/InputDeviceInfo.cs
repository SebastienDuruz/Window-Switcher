namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Models;

/// <summary>
/// Describes one Linux evdev node and metadata obtained via ioctl probing.
/// </summary>
internal sealed record InputDeviceInfo
{
    /// <summary>
    /// Absolute device path, typically <c>/dev/input/eventX</c>.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Human-readable device name from <c>EVIOCGNAME</c>. Empty when unavailable.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Physical topology path from <c>EVIOCGPHYS</c>. Empty when unavailable.
    /// </summary>
    public string Phys { get; init; } = string.Empty;

    /// <summary>
    /// Unique identifier string from <c>EVIOCGUNIQ</c>. Empty when unavailable.
    /// </summary>
    public string Uniq { get; init; } = string.Empty;

    /// <summary>
    /// USB/vendor identifier from <c>EVIOCGID</c>, or <c>0</c> when not provided by the kernel.
    /// </summary>
    public ushort VendorId { get; init; }

    /// <summary>
    /// Product identifier from <c>EVIOCGID</c>, or <c>0</c> when not provided by the kernel.
    /// </summary>
    public ushort ProductId { get; init; }

    /// <summary>
    /// Device version from <c>EVIOCGID</c>, or <c>0</c> when not provided by the kernel.
    /// </summary>
    public ushort Version { get; init; }

    /// <summary>
    /// Bus type from <c>EVIOCGID</c>, or <c>0</c> when not provided by the kernel.
    /// </summary>
    public ushort BusType { get; init; }

    /// <summary>
    /// Derived high-level kind inferred from capabilities.
    /// </summary>
    public DeviceKind Kind { get; init; } = DeviceKind.Other;

    /// <summary>
    /// Capability bitsets decoded from <c>EVIOCGBIT</c>.
    /// </summary>
    public InputDeviceCapabilities Caps { get; init; } = InputDeviceCapabilities.Empty;

    /// <summary>
    /// Indicates whether the current process can open the device node for reading.
    /// </summary>
    public bool IsAccessible { get; init; } = true;

    /// <summary>
    /// Access failure description when <see cref="IsAccessible"/> is <see langword="false"/>.
    /// </summary>
    public string? AccessError { get; init; }
}
