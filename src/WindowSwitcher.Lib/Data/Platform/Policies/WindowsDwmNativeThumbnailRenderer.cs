using System.Runtime.Versioning;
using WindowSwitcher.Lib.Data.Platform.Interop;
using WindowSwitcher.Lib.Data.Platform.SystemInfo.Abstractions;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.Policies;

/// <summary>
/// Windows DWM implementation for native window thumbnail rendering.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDwmNativeThumbnailRenderer : INativeThumbnailRenderer
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
        if (destinationWindowHandle == 0 || sourceWindowHandle == 0)
            return false;

        int result = DwmFunctions.DwmRegisterThumbnail(
            destinationWindowHandle,
            sourceWindowHandle,
            out nint registeredThumbnailHandle
        );
        if (result != 0)
            return false;

        var properties = new DwmFunctions.DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags =
                DwmFunctions.DWM_TNP_SOURCECLIENTAREAONLY
                | DwmFunctions.DWM_TNP_VISIBLE
                | DwmFunctions.DWM_TNP_OPACITY
                | DwmFunctions.DWM_TNP_RECTDESTINATION,
            fSourceClientAreaOnly = false,
            fVisible = true,
            opacity = 255,
            rcDestination = new DwmFunctions.Rect
            {
                Left = destinationBounds.Left,
                Top = destinationBounds.Top,
                Right = destinationBounds.Right,
                Bottom = destinationBounds.Bottom,
            },
        };

        DwmFunctions.DwmUpdateThumbnailProperties(registeredThumbnailHandle, ref properties);
        thumbnailHandle = registeredThumbnailHandle;
        return true;
    }

    /// <inheritdoc />
    public void Unregister(nint thumbnailHandle)
    {
        if (thumbnailHandle == 0)
            return;

        DwmFunctions.DwmUnregisterThumbnail(thumbnailHandle);
    }
}
