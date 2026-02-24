using WindowSwitcherLib.Data.Platform.SystemInfo.Abstractions;

namespace WindowSwitcherLib.Data.Platform.Policies;

/// <summary>
/// Linux preview policy using screenshot control in the floating window.
/// </summary>
public sealed class LinuxFloatingPreviewPolicy : IFloatingPreviewPolicy
{
    public bool UseNativeThumbnailPreview => false;

    public bool ShowScreenshotControl => true;

    public bool RefreshScreenshotWhenDeselected => true;
}
