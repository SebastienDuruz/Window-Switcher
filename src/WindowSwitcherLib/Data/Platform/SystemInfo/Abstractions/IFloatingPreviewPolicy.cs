namespace WindowSwitcherLib.Data.Platform.SystemInfo.Abstractions;

/// <summary>
/// Describes preview rendering capabilities for floating windows.
/// </summary>
public interface IFloatingPreviewPolicy
{
    /// <summary>
    /// Indicates whether native window thumbnail preview should be used.
    /// </summary>
    bool UseNativeThumbnailPreview { get; }

    /// <summary>
    /// Indicates whether screenshot control is visible in the floating window.
    /// </summary>
    bool ShowScreenshotControl { get; }

    /// <summary>
    /// Indicates whether screenshot refresh should be forced when preview loses focus.
    /// </summary>
    bool RefreshScreenshotWhenDeselected { get; }
}
