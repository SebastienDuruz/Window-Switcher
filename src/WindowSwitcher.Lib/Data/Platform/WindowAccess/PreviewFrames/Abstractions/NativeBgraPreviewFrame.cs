namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;

/// <summary>
/// Represents one leased native BGRA preview frame.
/// </summary>
public sealed class NativeBgraPreviewFrame : IDisposable
{
    private readonly Action _release;
    private bool _disposed;

    internal NativeBgraPreviewFrame(
        IntPtr data,
        int length,
        int widthPx,
        int heightPx,
        Action release
    )
    {
        ArgumentNullException.ThrowIfNull(release);

        Data = data;
        Length = length;
        WidthPx = widthPx;
        HeightPx = heightPx;
        _release = release;
    }

    /// <summary>
    /// Gets a pointer to the frame bytes in BGRA order.
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

    /// <summary>
    /// Copies the frame bytes into a native BGRA destination buffer.
    /// </summary>
    /// <param name="destination">Destination buffer pointer.</param>
    /// <param name="destinationStride">Destination row stride in bytes.</param>
    /// <returns><see langword="true" /> when the copy succeeds; otherwise, <see langword="false" />.</returns>
    public bool TryCopyTo(IntPtr destination, int destinationStride)
    {
        if (Data == IntPtr.Zero || destination == IntPtr.Zero || Length <= 0)
            return false;

        int sourceStride;
        int requiredBytes;
        try
        {
            sourceStride = checked(WidthPx * 4);
            requiredBytes = checked(sourceStride * HeightPx);
        }
        catch (OverflowException)
        {
            return false;
        }

        if (Length < requiredBytes || destinationStride < sourceStride)
            return false;

        if (destinationStride == sourceStride)
        {
            NativeMemoryCopy(destination, Data, (UIntPtr)requiredBytes);
            return true;
        }

        for (int row = 0; row < HeightPx; row++)
        {
            NativeMemoryCopy(
                IntPtr.Add(destination, row * destinationStride),
                IntPtr.Add(Data, row * sourceStride),
                (UIntPtr)sourceStride
            );
        }

        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _release();
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "memcpy")]
    private static extern IntPtr NativeMemoryCopy(IntPtr destination, IntPtr source, UIntPtr count);
}
