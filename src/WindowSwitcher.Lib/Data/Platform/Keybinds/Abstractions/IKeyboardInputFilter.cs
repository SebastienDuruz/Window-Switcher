using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;

/// <summary>
/// Evaluates captured keyboard events and decides whether they must be consumed
/// or forwarded to the operating system.
/// </summary>
public interface IKeyboardInputFilter
{
    /// <summary>
    /// Processes one captured keyboard event.
    /// </summary>
    KeyboardFilterDecision ProcessEvent(GlobalKeyEventArgs keyEvent);

    /// <summary>
    /// Clears any pressed-key or active-combination state.
    /// </summary>
    void Reset();
}
