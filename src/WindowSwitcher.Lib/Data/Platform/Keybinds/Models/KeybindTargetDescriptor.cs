namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Models;

/// <summary>
/// Describes one selectable keybind target for the UI.
/// </summary>
public sealed class KeybindTargetDescriptor
{
    /// <summary>
    /// Creates a target descriptor.
    /// </summary>
    public KeybindTargetDescriptor(string targetId, string displayName, bool isBuiltIn)
    {
        ArgumentNullException.ThrowIfNull(targetId);
        ArgumentNullException.ThrowIfNull(displayName);

        TargetId = targetId;
        DisplayName = displayName;
        IsBuiltIn = isBuiltIn;
    }

    /// <summary>
    /// Gets the stable target identifier.
    /// </summary>
    public string TargetId { get; }

    /// <summary>
    /// Gets the display label shown in the UI.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets whether the target is one of the built-in actions.
    /// </summary>
    public bool IsBuiltIn { get; }
}
