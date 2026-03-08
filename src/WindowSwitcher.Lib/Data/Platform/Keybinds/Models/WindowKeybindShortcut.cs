namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Models;

/// <summary>
/// Configured keybind shortcut for one target.
/// </summary>
public sealed class WindowKeybindShortcut
{
    /// <summary>
    /// Gets or sets the bound key combination.
    /// </summary>
    public KeyCombination Combination { get; set; } = new();
}
