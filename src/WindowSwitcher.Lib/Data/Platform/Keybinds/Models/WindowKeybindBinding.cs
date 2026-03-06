namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Models;

/// <summary>
/// Configured keybind binding for one target.
/// </summary>
public sealed class WindowKeybindBinding
{
    /// <summary>
    /// Gets or sets whether this binding is active.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the bound key combination.
    /// </summary>
    public KeyCombination Combination { get; set; } = new();
}
