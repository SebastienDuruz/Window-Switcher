using WindowSwitcherLib.Application.Platform;

namespace WindowSwitcherLib.Infrastructure.Platform.Policies;

/// <summary>
/// Linux preview policy using screenshot-based rendering.
/// </summary>
public sealed class LinuxFloatingPreviewPolicy : IFloatingPreviewPolicy
{
    public bool UseNativeThumbnailPreview => false;

    public bool ShowScreenshotControl => true;

    public bool RefreshScreenshotWhenDeselected => true;
}
