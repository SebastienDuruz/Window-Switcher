using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.SystemInfo.Abstractions;

/// <summary>
/// Platform adapter for native window thumbnail registration.
/// </summary>
public interface INativeThumbnailRenderer
{
    /// <summary>
    /// Registers a native preview of a source window into a destination window.
    /// </summary>
    /// <param name="destinationWindowHandle">Native handle of the preview host window.</param>
    /// <param name="sourceWindowHandle">Native handle of the window being previewed.</param>
    /// <param name="destinationBounds">Destination rectangle in physical pixels.</param>
    /// <param name="thumbnailHandle">Native thumbnail handle when registration succeeds.</param>
    /// <returns><see langword="true"/> when the thumbnail was registered.</returns>
    bool TryRegister(
        nint destinationWindowHandle,
        nint sourceWindowHandle,
        NativeThumbnailBounds destinationBounds,
        out nint thumbnailHandle
    );

    /// <summary>
    /// Releases a previously registered native thumbnail.
    /// </summary>
    /// <param name="thumbnailHandle">Native thumbnail handle returned by <see cref="TryRegister"/>.</param>
    void Unregister(nint thumbnailHandle);
}
