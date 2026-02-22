using WindowSwitcherLib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcherLib.Data.Platform.WindowAccess.Factories.Abstractions;
using WindowSwitcherLib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;

namespace WindowSwitcherLib.Data.Platform.WindowAccess.Factories;

/// <summary>
/// Backward-compatible static entry point for obtaining preview providers.
/// </summary>
public static class PreviewFactory
{
    private static IPreviewFrameProviderFactory _current = new RuntimePreviewFrameProviderFactory();

    /// <summary>
    /// Gets or sets the underlying preview provider factory implementation.
    /// </summary>
    public static IPreviewFrameProviderFactory Current
    {
        get => _current;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _current = value;
        }
    }

    /// <summary>
    /// Creates a preview provider using <see cref="Current"/>.
    /// </summary>
    public static IPreviewFrameProvider Create(WinAccessorBase accessorBase)
    {
        return Current.Create(accessorBase);
    }
}
