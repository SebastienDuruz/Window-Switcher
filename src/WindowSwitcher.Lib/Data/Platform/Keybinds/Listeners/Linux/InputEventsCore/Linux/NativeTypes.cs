using System.Runtime.InteropServices;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Linux;

/// <summary>
/// Managed layout of Linux <c>struct input_id</c> for <c>EVIOCGID</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeInputId
{
    public ushort BusType;
    public ushort Vendor;
    public ushort Product;
    public ushort Version;
}

/// <summary>
/// Managed layout of Linux <c>struct input_event</c>.
/// </summary>
/// <remarks>
/// On x86_64 this structure is typically 24 bytes:
/// timeval (16) + type (2) + code (2) + value (4).
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeInputEvent
{
    public long Seconds;
    public long Microseconds;
    public ushort Type;
    public ushort Code;
    public int Value;

    /// <summary>
    /// Size of <see cref="NativeInputEvent"/> in bytes for the current runtime architecture.
    /// </summary>
    public static readonly int Size = Marshal.SizeOf<NativeInputEvent>();
}
