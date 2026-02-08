using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace WindowSwitcherLib.Data.WindowAccess;

public static class LinuxX11CompositeImageCapture
{
    public static bool TryCaptureWindow(string windowId, out Bitmap? bitmap)
    {
        bitmap = null;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return false;

        if (string.IsNullOrWhiteSpace(windowId))
            return false;

        if (!NativeLibrary.TryLoad("libX11.so.6", out IntPtr x11))
            return false;
        if (!NativeLibrary.TryLoad("libXcomposite.so.1", out IntPtr xcomposite))
        {
            NativeLibrary.Free(x11);
            return false;
        }

        NativeLibrary.Free(x11);
        NativeLibrary.Free(xcomposite);

        IntPtr display = XOpenDisplay(IntPtr.Zero);
        if (display == IntPtr.Zero)
            return false;

        try
        {
            if (!TryParseX11WindowId(windowId, out IntPtr window))
                return false;

            try
            {
                XCompositeRedirectWindow(display, window, CompositeRedirectAutomatic);
            }
            catch
            {
                // Best effort.
            }

            IntPtr pixmap = XCompositeNameWindowPixmap(display, window);
            if (pixmap == IntPtr.Zero)
                return false;

            try
            {
                if (XGetGeometry(display, pixmap, out _, out _, out _, out uint width, out uint height, out _, out _) == 0)
                    return false;
                if (width == 0 || height == 0)
                    return false;

                IntPtr imagePtr = XGetImage(display, pixmap, 0, 0, width, height, AllPlanes, ZPixmap);
                if (imagePtr == IntPtr.Zero)
                    return false;

                try
                {
                    XImage image = Marshal.PtrToStructure<XImage>(imagePtr);
                    if (image.data == IntPtr.Zero)
                        return false;

                    // Fast path for the common TrueColor little-endian case (B,G,R,X in memory).
                    bool standard32 =
                        image.bits_per_pixel == 32
                        && image.byte_order == LsbFirst
                        && image.red_mask == 0x00ff0000UL
                        && image.green_mask == 0x0000ff00UL
                        && image.blue_mask == 0x000000ffUL;

                    bool standard24 =
                        image.bits_per_pixel == 24
                        && image.byte_order == LsbFirst
                        && image.red_mask == 0x00ff0000UL
                        && image.green_mask == 0x0000ff00UL
                        && image.blue_mask == 0x000000ffUL;

                    if (!standard32 && !standard24)
                        return false;

                    var writeable = new WriteableBitmap(
                        new PixelSize((int)width, (int)height),
                        new Vector(96, 96),
                        PixelFormat.Bgra8888,
                        AlphaFormat.Opaque);

                    using (ILockedFramebuffer fb = writeable.Lock())
                    {
                        int destRowBytes = checked((int)width * 4);

                        if (standard32)
                        {
                            var row = new byte[destRowBytes];
                            for (int y = 0; y < (int)height; y++)
                            {
                                IntPtr srcRow = IntPtr.Add(image.data, y * image.bytes_per_line);
                                IntPtr dstRow = IntPtr.Add(fb.Address, y * fb.RowBytes);

                                Marshal.Copy(srcRow, row, 0, destRowBytes);
                                for (int i = 3; i < row.Length; i += 4)
                                    row[i] = 255;

                                Marshal.Copy(row, 0, dstRow, destRowBytes);
                            }
                        }
                        else // standard24
                        {
                            int srcRowBytes = checked((int)width * 3);
                            var src = new byte[srcRowBytes];
                            var dst = new byte[destRowBytes];
                            for (int y = 0; y < (int)height; y++)
                            {
                                IntPtr srcRow = IntPtr.Add(image.data, y * image.bytes_per_line);
                                IntPtr dstRow = IntPtr.Add(fb.Address, y * fb.RowBytes);

                                Marshal.Copy(srcRow, src, 0, srcRowBytes);
                                for (int x = 0, si = 0, di = 0; x < (int)width; x++, si += 3, di += 4)
                                {
                                    dst[di + 0] = src[si + 0];
                                    dst[di + 1] = src[si + 1];
                                    dst[di + 2] = src[si + 2];
                                    dst[di + 3] = 255;
                                }

                                Marshal.Copy(dst, 0, dstRow, destRowBytes);
                            }
                        }
                    }

                    bitmap = writeable;
                    return true;
                }
                finally
                {
                    _ = XDestroyImage(imagePtr);
                }
            }
            finally
            {
                _ = XFreePixmap(display, pixmap);
            }
        }
        finally
        {
            _ = XCloseDisplay(display);
        }
    }

    private static bool TryParseX11WindowId(string value, out IntPtr window)
    {
        window = IntPtr.Zero;
        string trimmed = value.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[2..];

        if (!ulong.TryParse(trimmed, System.Globalization.NumberStyles.HexNumber, provider: null, out ulong parsed))
            return false;

        window = (IntPtr)unchecked((nint)parsed);
        return true;
    }

    private const int CompositeRedirectAutomatic = 1;
    private const int ZPixmap = 2;
    private const int LsbFirst = 0;
    private const ulong AllPlanes = ulong.MaxValue;

    [StructLayout(LayoutKind.Sequential)]
    private struct XImage
    {
        public int width;
        public int height;
        public int xoffset;
        public int format;
        public IntPtr data;
        public int byte_order;
        public int bitmap_unit;
        public int bitmap_bit_order;
        public int bitmap_pad;
        public int depth;
        public int bytes_per_line;
        public int bits_per_pixel;
        public ulong red_mask;
        public ulong green_mask;
        public ulong blue_mask;
        public IntPtr obdata;
    }

    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(IntPtr displayName);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XGetGeometry(
        IntPtr display,
        IntPtr drawable,
        out IntPtr rootReturn,
        out int xReturn,
        out int yReturn,
        out uint widthReturn,
        out uint heightReturn,
        out uint borderWidthReturn,
        out uint depthReturn);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XGetImage(
        IntPtr display,
        IntPtr drawable,
        int x,
        int y,
        uint width,
        uint height,
        ulong planeMask,
        int format);

    [DllImport("libX11.so.6")]
    private static extern int XDestroyImage(IntPtr image);

    [DllImport("libX11.so.6")]
    private static extern int XFreePixmap(IntPtr display, IntPtr pixmap);

    [DllImport("libXcomposite.so.1")]
    private static extern void XCompositeRedirectWindow(IntPtr display, IntPtr window, int update);

    [DllImport("libXcomposite.so.1")]
    private static extern IntPtr XCompositeNameWindowPixmap(IntPtr display, IntPtr window);
}

