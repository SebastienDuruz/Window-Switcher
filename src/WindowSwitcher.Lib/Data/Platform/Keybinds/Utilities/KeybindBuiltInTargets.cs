namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Utilities;

/// <summary>
/// Built-in keybind targets that are not tied to one specific window id.
/// </summary>
public static class KeybindBuiltInTargets
{
    /// <summary>
    /// Target id for cycling to the next client.
    /// </summary>
    public const string NextClientTargetId = "action:next-client";

    /// <summary>
    /// Target id for cycling to the previous client.
    /// </summary>
    public const string PreviousClientTargetId = "action:previous-client";

    /// <summary>
    /// Target id for focusing the currently active client.
    /// </summary>
    public const string FocusActiveClientTargetId = "action:focus-active-client";

    /// <summary>
    /// Display label for next selected client action.
    /// </summary>
    public const string NextClientDisplayName = "Next client";

    /// <summary>
    /// Display label for previous selected client action.
    /// </summary>
    public const string PreviousClientDisplayName = "Previous client";

    /// <summary>
    /// Display label for focus active client action.
    /// </summary>
    public const string FocusActiveClientDisplayName = "Focus active client";

    /// <summary>
    /// Returns whether a target id is one of the built-in action targets.
    /// </summary>
    public static bool IsBuiltInTarget(string? targetId)
    {
        if (string.IsNullOrWhiteSpace(targetId))
            return false;

        string normalized = targetId.Trim().ToLowerInvariant();
        return normalized == NextClientTargetId
            || normalized == PreviousClientTargetId
            || normalized == FocusActiveClientTargetId;
    }
}
