using System.Runtime.InteropServices;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.X11;

internal static class X11Native
{
    public const int ZPixmap = 2;
    public const int LsbFirst = 0;
    public const int CompositeRedirectAutomatic = 0;
    public const int DamageReportNonEmpty = 3;
    public const int IpcPrivate = 0;
    public const int IpcCreat = 0x200;
    public const int IpcRmId = 0;
    public const ulong AllPlanes = ulong.MaxValue;

    private static readonly XErrorHandler ErrorHandler = IgnoreXError;
    private static int _initialized;

    public static void EnsureInitialized()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
            return;

        try
        {
            _ = XInitThreads();
            _ = XSetErrorHandler(ErrorHandler);
        }
        catch
        {
            // Availability checks and capture creation will fall back safely.
        }
    }

    private static int IgnoreXError(IntPtr display, IntPtr errorEvent)
    {
        return 0;
    }

    [DllImport("libX11.so.6")]
    public static extern int XInitThreads();

    [DllImport("libX11.so.6")]
    public static extern IntPtr XOpenDisplay(IntPtr displayName);

    [DllImport("libX11.so.6")]
    public static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    public static extern int XDefaultScreen(IntPtr display);

    [DllImport("libX11.so.6", CharSet = CharSet.Ansi)]
    public static extern IntPtr XInternAtom(IntPtr display, string atomName, int onlyIfExists);

    [DllImport("libX11.so.6")]
    public static extern IntPtr XGetSelectionOwner(IntPtr display, IntPtr selection);

    [DllImport("libX11.so.6")]
    public static extern int XSync(IntPtr display, int discard);

    [DllImport("libX11.so.6")]
    public static extern int XFlush(IntPtr display);

    [DllImport("libX11.so.6")]
    public static extern int XFreePixmap(IntPtr display, IntPtr pixmap);

    [DllImport("libX11.so.6")]
    public static extern int XGetWindowAttributes(
        IntPtr display,
        IntPtr window,
        out XWindowAttributes attributes
    );

    [DllImport("libX11.so.6")]
    public static extern int XGetGeometry(
        IntPtr display,
        IntPtr drawable,
        out IntPtr root,
        out int x,
        out int y,
        out uint width,
        out uint height,
        out uint borderWidth,
        out uint depth
    );

    [DllImport("libX11.so.6")]
    public static extern IntPtr XGetImage(
        IntPtr display,
        IntPtr drawable,
        int x,
        int y,
        uint width,
        uint height,
        ulong planeMask,
        int format
    );

    [DllImport("libX11.so.6")]
    public static extern int XDestroyImage(IntPtr image);

    [DllImport("libX11.so.6")]
    public static extern int XPending(IntPtr display);

    [DllImport("libX11.so.6")]
    public static extern int XNextEvent(IntPtr display, out XEvent xevent);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XSetErrorHandler(XErrorHandler handler);

    [DllImport("libXcomposite.so.1")]
    public static extern int XCompositeQueryExtension(
        IntPtr display,
        out int eventBase,
        out int errorBase
    );

    [DllImport("libXcomposite.so.1")]
    public static extern int XCompositeQueryVersion(
        IntPtr display,
        ref int majorVersion,
        ref int minorVersion
    );

    [DllImport("libXcomposite.so.1")]
    public static extern void XCompositeRedirectWindow(IntPtr display, IntPtr window, int update);

    [DllImport("libXcomposite.so.1")]
    public static extern void XCompositeUnredirectWindow(IntPtr display, IntPtr window, int update);

    [DllImport("libXcomposite.so.1")]
    public static extern IntPtr XCompositeNameWindowPixmap(IntPtr display, IntPtr window);

    [DllImport("libXdamage.so.1")]
    public static extern int XDamageQueryExtension(
        IntPtr display,
        out int eventBase,
        out int errorBase
    );

    [DllImport("libXdamage.so.1")]
    public static extern int XDamageQueryVersion(
        IntPtr display,
        ref int majorVersion,
        ref int minorVersion
    );

    [DllImport("libXdamage.so.1")]
    public static extern IntPtr XDamageCreate(IntPtr display, IntPtr drawable, int level);

    [DllImport("libXdamage.so.1")]
    public static extern void XDamageDestroy(IntPtr display, IntPtr damage);

    [DllImport("libXdamage.so.1")]
    public static extern void XDamageSubtract(
        IntPtr display,
        IntPtr damage,
        IntPtr repair,
        IntPtr parts
    );

    [DllImport("libXext.so.6")]
    public static extern int XShmQueryExtension(IntPtr display);

    [DllImport("libXext.so.6")]
    public static extern IntPtr XShmCreateImage(
        IntPtr display,
        IntPtr visual,
        uint depth,
        int format,
        IntPtr data,
        ref XShmSegmentInfo shminfo,
        uint width,
        uint height
    );

    [DllImport("libXext.so.6")]
    public static extern int XShmAttach(IntPtr display, ref XShmSegmentInfo shminfo);

    [DllImport("libXext.so.6")]
    public static extern int XShmDetach(IntPtr display, ref XShmSegmentInfo shminfo);

    [DllImport("libXext.so.6")]
    public static extern int XShmGetImage(
        IntPtr display,
        IntPtr drawable,
        IntPtr image,
        int x,
        int y,
        ulong planeMask
    );

    [DllImport("libc", SetLastError = true)]
    public static extern int shmget(int key, UIntPtr size, int shmflg);

    [DllImport("libc", SetLastError = true)]
    public static extern IntPtr shmat(int shmid, IntPtr shmaddr, int shmflg);

    [DllImport("libc", SetLastError = true)]
    public static extern int shmdt(IntPtr shmaddr);

    [DllImport("libc", SetLastError = true)]
    public static extern int shmctl(int shmid, int cmd, IntPtr buf);

    private delegate int XErrorHandler(IntPtr display, IntPtr errorEvent);
}

[StructLayout(LayoutKind.Sequential)]
internal struct XShmSegmentInfo
{
    public IntPtr ShmSeg;
    public int ShmId;
    public IntPtr ShmAddr;
    public int ReadOnly;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XWindowAttributes
{
    public int X;
    public int Y;
    public int Width;
    public int Height;
    public int BorderWidth;
    public int Depth;
    public IntPtr Visual;
    public IntPtr Root;
    public int Class;
    public int BitGravity;
    public int WinGravity;
    public int BackingStore;
    public ulong BackingPlanes;
    public ulong BackingPixel;
    public int SaveUnder;
    public IntPtr Colormap;
    public int MapInstalled;
    public int MapState;
    public nint AllEventMasks;
    public nint YourEventMask;
    public nint DoNotPropagateMask;
    public int OverrideRedirect;
    public IntPtr Screen;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XImage
{
    public int Width;
    public int Height;
    public int XOffset;
    public int Format;
    public IntPtr Data;
    public int ByteOrder;
    public int BitmapUnit;
    public int BitmapBitOrder;
    public int BitmapPad;
    public int Depth;
    public int BytesPerLine;
    public int BitsPerPixel;
    public ulong RedMask;
    public ulong GreenMask;
    public ulong BlueMask;
}

[StructLayout(LayoutKind.Sequential, Size = 192)]
internal struct XEvent
{
    public int Type;
}
