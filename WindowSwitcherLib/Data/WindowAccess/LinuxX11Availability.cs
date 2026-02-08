using System;
using System.Runtime.InteropServices;

namespace WindowSwitcherLib.Data.WindowAccess;

public static class LinuxX11Availability
{
    public static bool IsAvailable()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return false;

        if (!NativeLibrary.TryLoad("libX11.so.6", out IntPtr x11))
            return false;
        NativeLibrary.Free(x11);

        IntPtr display = XOpenDisplay(IntPtr.Zero);
        if (display == IntPtr.Zero)
            return false;

        _ = XCloseDisplay(display);
        return true;
    }

    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(IntPtr displayName);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(IntPtr display);
}

