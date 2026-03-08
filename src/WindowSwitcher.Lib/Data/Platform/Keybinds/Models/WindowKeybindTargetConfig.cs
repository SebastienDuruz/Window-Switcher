namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Models;

/// <summary>
/// Keybind configuration for one target window identity.
/// </summary>
public sealed class WindowKeybindTargetConfig
{
    /// <summary>
    /// Stable target identifier used for runtime matching.
    /// </summary>
    public string TargetId { get; set; } = string.Empty;

    /// <summary>
    /// Display label used in the UI.
    /// </summary>
    public string DisplayLabel { get; set; } = string.Empty;

    /// <summary>
    /// Configured bindings for this target.
    /// </summary>
    public List<WindowKeybindShortcut> Shortcuts { get; set; } = [];
}
