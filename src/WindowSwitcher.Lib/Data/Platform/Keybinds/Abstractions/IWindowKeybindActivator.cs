namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;

/// <summary>
/// Activates a runtime window target from a stable target id.
/// </summary>
public interface IWindowKeybindActivator
{
    /// <summary>
    /// Raised after a window has been activated through a keybind target.
    /// Event data contains the activated <c>WindowId</c>.
    /// </summary>
    event EventHandler<string>? WindowActivated;

    /// <summary>
    /// Tries to activate a matching runtime window for the target id.
    /// </summary>
    bool TryActivateTarget(string targetId);
}
