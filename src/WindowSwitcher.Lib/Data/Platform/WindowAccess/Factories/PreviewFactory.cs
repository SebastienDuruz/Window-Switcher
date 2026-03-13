using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Factories.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.Factories;

/// <summary>
/// Backward-compatible static entry point for obtaining preview providers.
/// </summary>
public static class PreviewFactory
{
    /// <summary>
    /// Gets or sets the underlying preview provider factory implementation.
    /// </summary>
    public static IPreviewFrameProviderFactory Current
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = new RuntimePreviewFrameProviderFactory();

    /// <summary>
    /// Creates a preview provider using <see cref="Current"/>.
    /// </summary>
    public static IPreviewFrameProvider Create(WinAccessorBase accessorBase)
    {
        return Current.Create(accessorBase);
    }
}
