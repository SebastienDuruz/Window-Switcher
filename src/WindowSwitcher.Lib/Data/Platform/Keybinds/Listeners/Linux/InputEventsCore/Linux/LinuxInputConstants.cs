namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Linux;

/// <summary>
/// Linux input constants used by discovery and decoding logic.
/// </summary>
internal static class LinuxInputConstants
{
    // Event types (EV_*).
    public const ushort EvSyn = 0x00;
    public const ushort EvKey = 0x01;
    public const ushort EvRel = 0x02;
    public const ushort EvAbs = 0x03;

    // Maximum code values used to size capability bitsets.
    public const int EvMax = 0x1f;
    public const int KeyMax = 0x2ff;
    public const int RelMax = 0x0f;
    public const int AbsMax = 0x3f;

    // Representative codes used by keyboard/mouse kind heuristics.
    public const ushort KeyEnter = 28;
    public const ushort KeyA = 30;
    public const ushort KeyZ = 44;
    public const ushort BtnLeft = 0x110;
    public const ushort BtnRight = 0x111;
}
