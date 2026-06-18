using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Factories.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.NoOp;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.Factories;

/// <summary>
/// Windows preview factory. Preview rendering is handled by native thumbnails in the UI layer.
/// </summary>
public sealed class WindowsPreviewFrameProviderFactory : IPreviewFrameProviderFactory
{
    /// <inheritdoc />
    public IPreviewFrameProvider Create(WinAccessorBase accessorBase)
    {
        ArgumentNullException.ThrowIfNull(accessorBase);
        return new NoOpPreviewFrameProvider();
    }
}
