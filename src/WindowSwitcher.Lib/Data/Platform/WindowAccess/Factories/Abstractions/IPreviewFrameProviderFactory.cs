using WindowSwitcherLib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcherLib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;

namespace WindowSwitcherLib.Data.Platform.WindowAccess.Factories.Abstractions;

/// <summary>
/// Creates preview frame providers for a given accessor.
/// </summary>
public interface IPreviewFrameProviderFactory
{
    /// <summary>
    /// Creates the preview frame provider instance.
    /// </summary>
    IPreviewFrameProvider Create(WinAccessorBase accessorBase);
}
