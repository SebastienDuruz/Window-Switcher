using WindowSwitcher.Lib.Data.Platform.SystemInfo.Abstractions;

namespace WindowSwitcher.Lib.Data.Platform.Policies;

/// <summary>
/// Windows preview policy using native thumbnail rendering.
/// </summary>
public sealed class WindowsFloatingPreviewPolicy : IFloatingPreviewPolicy
{
    public bool UseNativeThumbnailPreview => true;

    public bool ShowScreenshotControl => false;

    public bool RefreshScreenshotWhenDeselected => false;
}
