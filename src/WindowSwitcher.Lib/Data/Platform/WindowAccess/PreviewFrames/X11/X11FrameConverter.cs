using System.Numerics;
using System.Runtime.InteropServices;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.X11;

internal static class X11FrameConverter
{
    internal const int MaximumOutputFrameBytes = 16 * 1024 * 1024;

    public static unsafe NativeBgraPreviewFrame? CreateFrame(
        IntPtr imagePtr,
        ScreenshotRequest request,
        NativeFrameBufferPool bufferPool
    )
    {
        ArgumentNullException.ThrowIfNull(bufferPool);
        if (imagePtr == IntPtr.Zero)
            return null;

        XImage image = Marshal.PtrToStructure<XImage>(imagePtr);
        if (!TryValidateImage(image, out ColorLayout layout, out int sourceBytesPerPixel))
            return null;

        (int width, int height) = CalculateTargetSize(image.Width, image.Height, request);
        if (!TryCalculateOutputLayout(width, height, out int destinationStride, out int length))
            return null;

        NativeFrameBufferPool.Lease? lease = bufferPool.TryRent(length);
        if (lease is null)
            return null;

        try
        {
            byte* source = (byte*)image.Data;
            byte* destination = (byte*)lease.Pointer;
            if (
                width == image.Width
                && height == image.Height
                && IsDirectCopyCompatible(image, layout)
            )
            {
                CopyRows(
                    source,
                    image.BytesPerLine,
                    destination,
                    destinationStride,
                    destinationStride,
                    height
                );
            }
            else
            {
                ConvertNearestNeighbor(
                    source,
                    image,
                    sourceBytesPerPixel,
                    layout,
                    destination,
                    width,
                    height,
                    destinationStride
                );
            }

            return new NativeBgraPreviewFrame(
                lease.Pointer,
                length,
                width,
                height,
                destinationStride,
                lease.Dispose
            );
        }
        catch
        {
            lease.Dispose();
            return null;
        }
    }

    private static bool TryValidateImage(
        XImage image,
        out ColorLayout layout,
        out int bytesPerPixel
    )
    {
        layout = default;
        bytesPerPixel = 0;
        if (
            image.Data == IntPtr.Zero
            || image.Width <= 0
            || image.Height <= 0
            || image.BytesPerLine <= 0
            || image.BitsPerPixel is not (16 or 24 or 32)
            || image.ByteOrder is not (X11Native.LsbFirst or X11Native.MsbFirst)
        )
            return false;

        bytesPerPixel = image.BitsPerPixel / 8;
        try
        {
            if (image.BytesPerLine < checked(image.Width * bytesPerPixel))
                return false;
            _ = checked(image.BytesPerLine * image.Height);
        }
        catch (OverflowException)
        {
            return false;
        }

        ulong redMask = image.RedMask;
        ulong greenMask = image.GreenMask;
        ulong blueMask = image.BlueMask;
        if (redMask == 0 || greenMask == 0 || blueMask == 0)
        {
            if (image.BitsPerPixel is not (24 or 32))
                return false;
            redMask = 0x00ff0000;
            greenMask = 0x0000ff00;
            blueMask = 0x000000ff;
        }

        ulong validBits = (1UL << image.BitsPerPixel) - 1;
        if (
            ((redMask | greenMask | blueMask) & ~validBits) != 0
            || (redMask & greenMask) != 0
            || (redMask & blueMask) != 0
            || (greenMask & blueMask) != 0
            || !TryCreateChannel(redMask, out ChannelLayout red)
            || !TryCreateChannel(greenMask, out ChannelLayout green)
            || !TryCreateChannel(blueMask, out ChannelLayout blue)
        )
            return false;

        layout = new ColorLayout(red, green, blue);
        return true;
    }

    private static bool TryCreateChannel(ulong mask, out ChannelLayout channel)
    {
        int shift = BitOperations.TrailingZeroCount(mask);
        ulong maximum = mask >> shift;
        channel = new ChannelLayout(mask, shift, maximum);
        return maximum != 0 && (maximum & (maximum + 1)) == 0;
    }

    private static bool TryCalculateOutputLayout(
        int width,
        int height,
        out int stride,
        out int length
    )
    {
        stride = 0;
        length = 0;
        if (width <= 0 || height <= 0)
            return false;
        try
        {
            stride = checked(width * 4);
            length = checked(stride * height);
            return length <= MaximumOutputFrameBytes;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool IsDirectCopyCompatible(XImage image, ColorLayout layout) =>
        image.BitsPerPixel == 32
        && image.ByteOrder == X11Native.LsbFirst
        && layout.Red.Mask == 0x00ff0000
        && layout.Green.Mask == 0x0000ff00
        && layout.Blue.Mask == 0x000000ff;

    private static unsafe void CopyRows(
        byte* source,
        int sourceStride,
        byte* destination,
        int destinationStride,
        int rowBytes,
        int height
    )
    {
        for (int row = 0; row < height; row++)
        {
            Buffer.MemoryCopy(
                source + row * sourceStride,
                destination + row * destinationStride,
                destinationStride,
                rowBytes
            );
        }
    }

    private static unsafe void ConvertNearestNeighbor(
        byte* source,
        XImage image,
        int sourceBytesPerPixel,
        ColorLayout layout,
        byte* destination,
        int targetWidth,
        int targetHeight,
        int destinationStride
    )
    {
        for (int y = 0; y < targetHeight; y++)
        {
            int sourceY = (int)((long)y * image.Height / targetHeight);
            byte* sourceRow = source + sourceY * image.BytesPerLine;
            byte* destinationRow = destination + y * destinationStride;
            for (int x = 0; x < targetWidth; x++)
            {
                int sourceX = (int)((long)x * image.Width / targetWidth);
                ulong pixel = ReadPixel(
                    sourceRow + sourceX * sourceBytesPerPixel,
                    sourceBytesPerPixel,
                    image.ByteOrder
                );
                byte* output = destinationRow + x * 4;
                output[0] = ScaleChannel(pixel, layout.Blue);
                output[1] = ScaleChannel(pixel, layout.Green);
                output[2] = ScaleChannel(pixel, layout.Red);
                output[3] = 255;
            }
        }
    }

    private static unsafe ulong ReadPixel(byte* source, int byteCount, int byteOrder)
    {
        ulong value = 0;
        if (byteOrder == X11Native.LsbFirst)
        {
            for (int index = 0; index < byteCount; index++)
                value |= (ulong)source[index] << (index * 8);
            return value;
        }

        for (int index = 0; index < byteCount; index++)
            value = (value << 8) | source[index];
        return value;
    }

    private static byte ScaleChannel(ulong pixel, ChannelLayout channel)
    {
        ulong value = (pixel & channel.Mask) >> channel.Shift;
        return (byte)(value * 255 / channel.Maximum);
    }

    internal static (int Width, int Height) CalculateTargetSize(
        int sourceWidth,
        int sourceHeight,
        ScreenshotRequest request
    )
    {
        if (sourceWidth <= 0 || sourceHeight <= 0)
            return default;
        int? maxWidth = request.MaxWidthPx is > 0 ? request.MaxWidthPx : null;
        int? maxHeight = request.MaxHeightPx is > 0 ? request.MaxHeightPx : null;
        if (maxWidth is null && maxHeight is null)
            return (sourceWidth, sourceHeight);

        double widthRatio = maxWidth is null ? 1d : (double)maxWidth.Value / sourceWidth;
        double heightRatio = maxHeight is null ? 1d : (double)maxHeight.Value / sourceHeight;
        double ratio = Math.Min(widthRatio, heightRatio);
        if (ratio >= 1d)
            return (sourceWidth, sourceHeight);
        return (
            Math.Max(1, (int)Math.Round(sourceWidth * ratio)),
            Math.Max(1, (int)Math.Round(sourceHeight * ratio))
        );
    }

    private readonly record struct ChannelLayout(ulong Mask, int Shift, ulong Maximum);
    private readonly record struct ColorLayout(
        ChannelLayout Red,
        ChannelLayout Green,
        ChannelLayout Blue
    );
}
