namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Models;

/// <summary>
/// Routing choice returned by the keyboard input filter.
/// </summary>
public enum KeyboardEventRouting
{
    /// <summary>
    /// Forward the current event to the operating system.
    /// </summary>
    Forward,

    /// <summary>
    /// Consume the current event so it never reaches the operating system.
    /// </summary>
    Consume,
}
