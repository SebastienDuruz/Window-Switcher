using System;
using System.Runtime.InteropServices;

namespace WindowSwitcherLib.Data.WindowAccess;

public static class LinuxX11OpenGlStreamingSupport
{
    private static readonly Lazy<bool> CachedSupport = new(ComputeIsSupported, isThreadSafe: true);

    public static bool IsSupported() => CachedSupport.Value;

    private static bool ComputeIsSupported()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return false;

        if (!NativeLibrary.TryLoad("libX11.so.6", out IntPtr x11))
            return false;
        if (!NativeLibrary.TryLoad("libXcomposite.so.1", out IntPtr xcomposite))
            return false;
        if (!NativeLibrary.TryLoad("libGL.so.1", out IntPtr gl))
            return false;

        NativeLibrary.Free(x11);
        NativeLibrary.Free(xcomposite);
        NativeLibrary.Free(gl);

        IntPtr display = XOpenDisplay(IntPtr.Zero);
        if (display == IntPtr.Zero)
            return false;

        try
        {
            if (XQueryExtension(display, "Composite", out _, out _, out _) == 0)
                return false;

            int screen = XDefaultScreen(display);
            IntPtr extensionsPtr = glXQueryExtensionsString(display, screen);
            if (extensionsPtr == IntPtr.Zero)
                return false;

            string? extensions = Marshal.PtrToStringAnsi(extensionsPtr);
            if (string.IsNullOrWhiteSpace(extensions))
                return false;

            return extensions.Contains("GLX_EXT_texture_from_pixmap", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
        finally
        {
            _ = XCloseDisplay(display);
        }
    }

    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(IntPtr displayName);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XDefaultScreen(IntPtr display);

    [DllImport("libX11.so.6", CharSet = CharSet.Ansi)]
    private static extern int XQueryExtension(IntPtr display, string name, out int majorOpcode, out int firstEvent, out int firstError);

    [DllImport("libGL.so.1")]
    private static extern IntPtr glXQueryExtensionsString(IntPtr display, int screen);
}
