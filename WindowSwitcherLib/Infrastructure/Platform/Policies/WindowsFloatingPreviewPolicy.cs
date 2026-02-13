using WindowSwitcherLib.Application.Platform;

namespace WindowSwitcherLib.Infrastructure.Platform.Policies;

/// <summary>
/// Windows preview policy using native thumbnail rendering.
/// </summary>
public sealed class WindowsFloatingPreviewPolicy : IFloatingPreviewPolicy
{
    public bool UseNativeThumbnailPreview => true;

    public bool ShowScreenshotControl => false;

    public bool RefreshScreenshotWhenDeselected => false;
}
