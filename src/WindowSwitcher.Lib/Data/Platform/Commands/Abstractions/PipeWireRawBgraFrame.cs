namespace WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;

/// <summary>
/// Represents one mapped BGRA frame returned by a PipeWire-backed GStreamer pipeline.
/// </summary>
public sealed class PipeWireRawBgraFrame : IDisposable
{
    private readonly Action _dispose;
    private bool _disposed;

    internal PipeWireRawBgraFrame(
        IntPtr data,
        int length,
        int widthPx,
        int heightPx,
        Action dispose
    )
    {
        ArgumentNullException.ThrowIfNull(dispose);

        Data = data;
        Length = length;
        WidthPx = widthPx;
        HeightPx = heightPx;
        _dispose = dispose;
    }

    /// <summary>
    /// Gets a pointer to the mapped BGRA bytes.
    /// </summary>
    public IntPtr Data { get; }

    /// <summary>
    /// Gets the number of valid bytes in <see cref="Data" />.
    /// </summary>
    public int Length { get; }

    /// <summary>
    /// Gets the frame width in pixels.
    /// </summary>
    public int WidthPx { get; }

    /// <summary>
    /// Gets the frame height in pixels.
    /// </summary>
    public int HeightPx { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _dispose();
    }
}
