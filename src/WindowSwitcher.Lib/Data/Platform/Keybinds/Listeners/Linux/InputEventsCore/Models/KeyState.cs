namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Models;

/// <summary>
/// Normalized key state derived from Linux <c>EV_KEY</c> event values.
/// </summary>
public enum KeyState
{
    /// <summary>
    /// Value is not recognized by this decoder.
    /// </summary>
    Unknown = -1,

    /// <summary>
    /// Key was released (<c>value = 0</c>).
    /// </summary>
    Up = 0,

    /// <summary>
    /// Key was pressed (<c>value = 1</c>).
    /// </summary>
    Down = 1,

    /// <summary>
    /// Key repeat event while held down (<c>value = 2</c>).
    /// </summary>
    Repeat = 2,
}
