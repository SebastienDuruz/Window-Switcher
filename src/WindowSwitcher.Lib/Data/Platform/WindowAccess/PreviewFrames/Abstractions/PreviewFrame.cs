namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;

/// <summary>
/// Represents one leased preview frame whose backing storage must remain valid until disposal.
/// </summary>
public abstract class PreviewFrame : IDisposable
{
    /// <summary>
    /// Gets the frame width in pixels.
    /// </summary>
    public abstract int WidthPx { get; }

    /// <summary>
    /// Gets the frame height in pixels.
    /// </summary>
    public abstract int HeightPx { get; }

    /// <inheritdoc />
    public abstract void Dispose();
}
