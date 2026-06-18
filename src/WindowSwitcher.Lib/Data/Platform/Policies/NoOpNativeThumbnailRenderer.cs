using WindowSwitcher.Lib.Data.Platform.SystemInfo.Abstractions;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.Policies;

/// <summary>
/// No-op native thumbnail renderer for platforms without native thumbnail support.
/// </summary>
public sealed class NoOpNativeThumbnailRenderer : INativeThumbnailRenderer
{
    /// <inheritdoc />
    public bool TryRegister(
        nint destinationWindowHandle,
        nint sourceWindowHandle,
        NativeThumbnailBounds destinationBounds,
        out nint thumbnailHandle
    )
    {
        thumbnailHandle = 0;
        return false;
    }

    /// <inheritdoc />
    public void Unregister(nint thumbnailHandle) { }
}
