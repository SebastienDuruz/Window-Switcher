using System.Runtime.InteropServices;
using Serilog;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.X11;

internal static class X11Native
{
    public const int ZPixmap = 2;
    public const int LsbFirst = 0;
    public const int MsbFirst = 1;
    public const int CompositeRedirectAutomatic = 0;
    public const int DamageReportNonEmpty = 3;
    public const int IpcPrivate = 0;
    public const int IpcCreat = 0x200;
    public const int IpcRmId = 0;
    public const int Success = 0;
    public const int PropModeReplace = 0;
    public const int ClientMessage = 33;
    public const nint SubstructureNotifyMask = 1 << 19;
    public const nint SubstructureRedirectMask = 1 << 20;
    public const ulong AllPlanes = ulong.MaxValue;
    public const int PictOpSrc = 1;
    public const string RenderFilterBilinear = "bilinear";

    private static readonly ILogger Logger = Log.ForContext(typeof(X11Native));
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
        catch (Exception exception)
        {
            // Availability checks and capture creation will fall back safely.
            Logger.Warning(
                exception,
                "X11 thread/error handler initialization failed; falling back"
            );
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

    [DllImport("libX11.so.6")]
    public static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport("libX11.so.6", CharSet = CharSet.Ansi)]
    public static extern IntPtr XInternAtom(IntPtr display, string atomName, int onlyIfExists);

    [DllImport("libX11.so.6")]
    public static extern IntPtr XGetSelectionOwner(IntPtr display, IntPtr selection);

    [DllImport("libX11.so.6")]
    public static extern int XSync(IntPtr display, int discard);

    [DllImport("libX11.so.6")]
    public static extern int XFlush(IntPtr display);

    [DllImport("libX11.so.6")]
    public static extern int XFree(IntPtr data);

    [DllImport("libX11.so.6")]
    public static extern int XGetWindowProperty(
        IntPtr display,
        IntPtr window,
        IntPtr property,
        nint longOffset,
        nint longLength,
        int delete,
        IntPtr requestedType,
        out IntPtr actualType,
        out int actualFormat,
        out nuint itemCount,
        out nuint bytesAfter,
        out IntPtr propertyData
    );

    [DllImport("libX11.so.6")]
    public static extern int XChangeProperty(
        IntPtr display,
        IntPtr window,
        IntPtr property,
        IntPtr type,
        int format,
        int mode,
        byte[] data,
        int elementCount
    );

    [DllImport("libX11.so.6")]
    public static extern int XSendEvent(
        IntPtr display,
        IntPtr window,
        int propagate,
        nint eventMask,
        ref XClientMessageEvent sendEvent
    );

    [DllImport("libX11.so.6")]
    public static extern IntPtr XCreatePixmap(
        IntPtr display,
        IntPtr drawable,
        uint width,
        uint height,
        uint depth
    );

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

    [DllImport("libXrender.so.1")]
    public static extern int XRenderQueryExtension(
        IntPtr display,
        out int eventBase,
        out int errorBase
    );

    [DllImport("libXrender.so.1")]
    public static extern int XRenderQueryVersion(
        IntPtr display,
        out int majorVersion,
        out int minorVersion
    );

    [DllImport("libXrender.so.1")]
    public static extern IntPtr XRenderFindVisualFormat(IntPtr display, IntPtr visual);

    [DllImport("libXrender.so.1")]
    public static extern IntPtr XRenderCreatePicture(
        IntPtr display,
        IntPtr drawable,
        IntPtr format,
        ulong valueMask,
        IntPtr attributes
    );

    [DllImport("libXrender.so.1")]
    public static extern void XRenderFreePicture(IntPtr display, IntPtr picture);

    [DllImport("libXrender.so.1")]
    public static extern void XRenderSetPictureTransform(
        IntPtr display,
        IntPtr picture,
        ref XTransform transform
    );

    [DllImport("libXrender.so.1", CharSet = CharSet.Ansi)]
    public static extern void XRenderSetPictureFilter(
        IntPtr display,
        IntPtr picture,
        string filter,
        IntPtr parameters,
        int parameterCount
    );

    [DllImport("libXrender.so.1")]
    public static extern void XRenderComposite(
        IntPtr display,
        int op,
        IntPtr source,
        IntPtr mask,
        IntPtr destination,
        int sourceX,
        int sourceY,
        int maskX,
        int maskY,
        int destinationX,
        int destinationY,
        uint width,
        uint height
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

[StructLayout(LayoutKind.Sequential)]
internal struct XClientMessageEvent
{
    public int Type;
    public nuint Serial;
    public int SendEvent;
    public IntPtr Display;
    public IntPtr Window;
    public IntPtr MessageType;
    public int Format;
    public XClientMessageData Data;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XClientMessageData
{
    public nint Data0;
    public nint Data1;
    public nint Data2;
    public nint Data3;
    public nint Data4;
}

/// <summary>
/// XRender projective transform: a 3x3 matrix of 16.16 fixed-point values, row-major.
/// It maps destination coordinates to source coordinates.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct XTransform
{
    public int Matrix11;
    public int Matrix12;
    public int Matrix13;
    public int Matrix21;
    public int Matrix22;
    public int Matrix23;
    public int Matrix31;
    public int Matrix32;
    public int Matrix33;
}

[StructLayout(LayoutKind.Sequential, Size = 192)]
internal struct XEvent
{
    public int Type;
}
