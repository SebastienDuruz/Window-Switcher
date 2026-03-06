namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Models;

/// <summary>
/// Result when registering a keybind.
/// </summary>
public enum KeybindRegistrationResult
{
    /// <summary>
    /// Binding has been added.
    /// </summary>
    Added,

    /// <summary>
    /// Binding already exists on the same target.
    /// </summary>
    Duplicate,

    /// <summary>
    /// Binding conflicts with another target.
    /// </summary>
    Conflict,

    /// <summary>
    /// Binding is invalid.
    /// </summary>
    Invalid,
}
