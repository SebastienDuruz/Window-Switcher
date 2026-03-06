namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Models;

/// <summary>
/// High-level classification inferred from Linux input capabilities.
/// </summary>
public enum DeviceKind
{
    /// <summary>
    /// Device exposes keyboard-like key codes.
    /// </summary>
    Keyboard,

    /// <summary>
    /// Device exposes relative pointer motion and mouse buttons.
    /// </summary>
    Mouse,

    /// <summary>
    /// Device does not match keyboard or mouse heuristics.
    /// </summary>
    Other
}
