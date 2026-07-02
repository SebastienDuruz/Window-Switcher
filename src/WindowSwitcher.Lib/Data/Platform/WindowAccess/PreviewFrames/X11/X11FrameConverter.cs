using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.X11;

internal static class X11FrameConverter
{
    public static Bitmap? CreateBitmap(IntPtr imagePtr, ScreenshotRequest request)
    {
        if (imagePtr == IntPtr.Zero)
            return null;

        XImage image = Marshal.PtrToStructure<XImage>(imagePtr);
        if (
            image.Data == IntPtr.Zero
            || image.Width <= 0
            || image.Height <= 0
            || image.BytesPerLine <= 0
        )
            return null;

        if (image.BitsPerPixel is not (16 or 24 or 32))
            return null;
        if (!TryNormalizeColorMasks(image, out ColorMasks colorMasks))
            return null;

        (int width, int height) = CalculateTargetSize(image.Width, image.Height, request);
        if (width <= 0 || height <= 0)
            return null;

        int sourceBytes;
        int destinationStride;
        int destinationBytes;
        try
        {
            sourceBytes = checked(image.BytesPerLine * image.Height);
            destinationStride = checked(width * 4);
            destinationBytes = checked(destinationStride * height);
        }
        catch (OverflowException)
        {
            return null;
        }

        byte[] source = new byte[sourceBytes];
        byte[] destination = new byte[destinationBytes];
        Marshal.Copy(image.Data, source, 0, sourceBytes);

        ConvertToBgra(source, destination, image, colorMasks, width, height, destinationStride);
        return CreateBitmapFromBgra(destination, width, height, destinationStride);
    }

    private static bool TryNormalizeColorMasks(XImage image, out ColorMasks masks)
    {
        masks = default;
        if (image.RedMask != 0 && image.GreenMask != 0 && image.BlueMask != 0)
        {
            masks = new ColorMasks(image.RedMask, image.GreenMask, image.BlueMask);
            return true;
        }

        if (image.BitsPerPixel is 24 or 32 && image.ByteOrder == X11Native.LsbFirst)
        {
            masks = new ColorMasks(
                Red: 0x00ff0000,
                Green: 0x0000ff00,
                Blue: 0x000000ff
            );
            return true;
        }

        if (image.BitsPerPixel is 24 or 32)
        {
            masks = new ColorMasks(
                Red: 0x000000ff,
                Green: 0x0000ff00,
                Blue: 0x00ff0000
            );
            return true;
        }

        return false;
    }

    private static (int Width, int Height) CalculateTargetSize(
        int sourceWidth,
        int sourceHeight,
        ScreenshotRequest request
    )
    {
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

    private static void ConvertToBgra(
        byte[] source,
        byte[] destination,
        XImage image,
        ColorMasks colorMasks,
        int targetWidth,
        int targetHeight,
        int targetStride
    )
    {
        int bytesPerPixel = image.BitsPerPixel / 8;
        int redShift = System.Numerics.BitOperations.TrailingZeroCount(colorMasks.Red);
        int greenShift = System.Numerics.BitOperations.TrailingZeroCount(colorMasks.Green);
        int blueShift = System.Numerics.BitOperations.TrailingZeroCount(colorMasks.Blue);
        ulong redMax = colorMasks.Red >> redShift;
        ulong greenMax = colorMasks.Green >> greenShift;
        ulong blueMax = colorMasks.Blue >> blueShift;

        for (int y = 0; y < targetHeight; y++)
        {
            int sourceY = y * image.Height / targetHeight;
            int sourceRowOffset = sourceY * image.BytesPerLine;
            int destinationRowOffset = y * targetStride;

            for (int x = 0; x < targetWidth; x++)
            {
                int sourceX = x * image.Width / targetWidth;
                int sourceOffset = sourceRowOffset + sourceX * bytesPerPixel;
                ulong pixel = ReadPixel(source, sourceOffset, image.BitsPerPixel, image.ByteOrder);
                int destinationOffset = destinationRowOffset + x * 4;

                destination[destinationOffset] = ScaleMaskedChannel(
                    pixel,
                    colorMasks.Blue,
                    blueShift,
                    blueMax
                );
                destination[destinationOffset + 1] = ScaleMaskedChannel(
                    pixel,
                    colorMasks.Green,
                    greenShift,
                    greenMax
                );
                destination[destinationOffset + 2] = ScaleMaskedChannel(
                    pixel,
                    colorMasks.Red,
                    redShift,
                    redMax
                );
                destination[destinationOffset + 3] = 255;
            }
        }
    }

    private static ulong ReadPixel(byte[] source, int offset, int bitsPerPixel, int byteOrder)
    {
        int bytesPerPixel = bitsPerPixel / 8;
        ulong pixel = 0;

        if (byteOrder == X11Native.LsbFirst)
        {
            for (int index = 0; index < bytesPerPixel; index++)
                pixel |= (ulong)source[offset + index] << (index * 8);
        }
        else
        {
            for (int index = 0; index < bytesPerPixel; index++)
                pixel = (pixel << 8) | source[offset + index];
        }

        return pixel;
    }

    private static byte ScaleMaskedChannel(ulong pixel, ulong mask, int shift, ulong max)
    {
        if (mask == 0 || max == 0)
            return 0;

        ulong value = (pixel & mask) >> shift;
        return (byte)Math.Min(255, value * 255 / max);
    }

    private static Bitmap? CreateBitmapFromBgra(
        byte[] bytes,
        int widthPx,
        int heightPx,
        int sourceStride
    )
    {
        try
        {
            var bitmap = new WriteableBitmap(
                new PixelSize(widthPx, heightPx),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Opaque
            );
            using ILockedFramebuffer framebuffer = bitmap.Lock();
            IntPtr destination = framebuffer.Address;
            if (destination == IntPtr.Zero)
            {
                bitmap.Dispose();
                return null;
            }

            int destinationStride = framebuffer.RowBytes;
            if (destinationStride == sourceStride)
            {
                Marshal.Copy(bytes, 0, destination, bytes.Length);
            }
            else
            {
                for (int row = 0; row < heightPx; row++)
                {
                    Marshal.Copy(
                        bytes,
                        row * sourceStride,
                        IntPtr.Add(destination, row * destinationStride),
                        sourceStride
                    );
                }
            }

            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private readonly record struct ColorMasks(ulong Red, ulong Green, ulong Blue);
}
