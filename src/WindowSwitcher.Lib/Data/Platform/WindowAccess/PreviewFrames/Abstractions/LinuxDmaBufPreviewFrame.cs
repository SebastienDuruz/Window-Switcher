namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;

/// <summary>
/// Represents a leased, single-plane Linux DMA-BUF preview frame.
/// </summary>
public sealed class LinuxDmaBufPreviewFrame : PreviewFrame
{
    /// <summary>
    /// Value used when the producer did not specify a DRM format modifier.
    /// </summary>
    public const ulong InvalidModifier = ulong.MaxValue;

    private Action? _release;

    internal LinuxDmaBufPreviewFrame(
        int fileDescriptor,
        int offset,
        int stride,
        int widthPx,
        int heightPx,
        uint drmFormat,
        ulong modifier,
        Action release
    )
    {
        ArgumentNullException.ThrowIfNull(release);
        FileDescriptor = fileDescriptor;
        Offset = offset;
        Stride = stride;
        WidthPx = widthPx;
        HeightPx = heightPx;
        DrmFormat = drmFormat;
        Modifier = modifier;
        _release = release;
    }

    /// <summary>
    /// Gets the DMA-BUF file descriptor. The descriptor remains valid until this frame is disposed.
    /// </summary>
    public int FileDescriptor { get; }

    /// <summary>
    /// Gets the byte offset of the first pixel in the DMA-BUF plane.
    /// </summary>
    public int Offset { get; }

    /// <summary>
    /// Gets the number of bytes between two adjacent rows.
    /// </summary>
    public int Stride { get; }

    /// <inheritdoc />
    public override int WidthPx { get; }

    /// <inheritdoc />
    public override int HeightPx { get; }

    /// <summary>
    /// Gets the DRM FourCC pixel format.
    /// </summary>
    public uint DrmFormat { get; }

    /// <summary>
    /// Gets the DRM format modifier, or <see cref="InvalidModifier" /> when implicit.
    /// </summary>
    public ulong Modifier { get; }

    /// <inheritdoc />
    public override void Dispose()
    {
        Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
