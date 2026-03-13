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
    /// Consume the current event now, but keep it buffered so it can be replayed later if needed.
    /// </summary>
    Buffer,

    /// <summary>
    /// Consume the current event and do not replay it.
    /// </summary>
    Consume,
}
