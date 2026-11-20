using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.X11;
using Xunit;

namespace WindowSwitcher.Tests.Platform.X11;

/// <summary>
/// A real X11 window owned by the test: override-redirect (no window manager involvement) and
/// placed off-screen, so running the suite on a desktop session shows nothing. Its left half is
/// red and its right half blue, which lets tests check orientation and colour after capture.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class X11TestWindow : IDisposable
{
    public const uint Red = 0xFF0000;
    public const uint Blue = 0x0000FF;
    private const nint CwOverrideRedirect = 1 << 9;

    private readonly IntPtr _display;
    private IntPtr _window;

    private X11TestWindow(IntPtr display, IntPtr window, int width, int height)
    {
        _display = display;
        _window = window;
        Width = width;
        Height = height;
    }

    public int Width { get; private set; }
    public int Height { get; private set; }
    public uint Id => unchecked((uint)_window.ToInt64());
    public string IdText => FormattableString.Invariant($"0x{Id:x8}");

    /// <summary>
    /// Opens a dedicated connection and creates the window, or skips the test without X server.
    /// </summary>
    public static X11TestWindow Create(int width, int height)
    {
        if (
            !OperatingSystem.IsLinux()
            || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))
        )
            Assert.Skip("Requires an X11 server (DISPLAY), for example Xvfb.");

        X11Native.EnsureInitialized();
        IntPtr display = X11Native.XOpenDisplay(IntPtr.Zero);
        if (display == IntPtr.Zero)
            Assert.Skip("The X11 server named by DISPLAY cannot be opened.");
        if (IsRootlessXwaylandDesktop(display))
        {
            _ = X11Native.XCloseDisplay(display);
            Assert.Skip(
                "Rootless Xwayland swaps window buffers after each frame, so a named composite "
                    + "pixmap goes stale. The application captures Wayland sessions through "
                    + "PipeWire, not X11; run these tests under Xvfb or Xorg."
            );
        }

        IntPtr root = X11Native.XDefaultRootWindow(display);
        IntPtr window = XCreateSimpleWindow(
            display,
            root,
            -4000,
            -4000,
            (uint)width,
            (uint)height,
            0,
            0,
            0
        );
        var attributes = new XSetWindowAttributes { OverrideRedirect = 1 };
        _ = XChangeWindowAttributes(display, window, CwOverrideRedirect, ref attributes);
        _ = XMapWindow(display, window);
        _ = X11Native.XSync(display, 0);

        var testWindow = new X11TestWindow(display, window, width, height);
        testWindow.WaitForCompositingManager();
        testWindow.Paint();
        return testWindow;
    }

    private static bool IsRootlessXwaylandDesktop(IntPtr display)
    {
        bool isXwayland = XQueryExtension(display, "XWAYLAND", out _, out _, out _) != 0;
        IntPtr selection = X11Native.XInternAtom(display, "_NET_WM_CM_S0", 0);
        return isXwayland && X11Native.XGetSelectionOwner(display, selection) != IntPtr.Zero;
    }

    /// <summary>
    /// A desktop compositing manager redirects a newly mapped window asynchronously, which gives
    /// it a new pixmap. Waiting lets tests start from a settled window; Xvfb has no such manager.
    /// </summary>
    private void WaitForCompositingManager()
    {
        IntPtr selection = X11Native.XInternAtom(_display, "_NET_WM_CM_S0", 0);
        if (X11Native.XGetSelectionOwner(_display, selection) != IntPtr.Zero)
            Thread.Sleep(300);
    }

    /// <summary>
    /// Paints the left half red and the right half blue.
    /// </summary>
    public void Paint() => Paint(Red, Blue);

    public void Paint(uint left, uint right)
    {
        IntPtr gc = XCreateGC(_display, _window, 0, IntPtr.Zero);
        try
        {
            int half = Width / 2;
            _ = XSetForeground(_display, gc, left);
            _ = XFillRectangle(_display, _window, gc, 0, 0, (uint)half, (uint)Height);
            _ = XSetForeground(_display, gc, right);
            _ = XFillRectangle(_display, _window, gc, half, 0, (uint)(Width - half), (uint)Height);
        }
        finally
        {
            _ = XFreeGC(_display, gc);
            _ = X11Native.XSync(_display, 0);
        }
    }

    public void Resize(int width, int height)
    {
        _ = XResizeWindow(_display, _window, (uint)width, (uint)height);
        Width = width;
        Height = height;
        _ = X11Native.XSync(_display, 0);
        Paint();
    }

    /// <summary>
    /// Unmaps and maps the window again, as minimizing and restoring it does.
    /// </summary>
    public void Remap()
    {
        _ = XUnmapWindow(_display, _window);
        _ = X11Native.XSync(_display, 0);
        _ = XMapWindow(_display, _window);
        _ = X11Native.XSync(_display, 0);
    }

    public void Destroy()
    {
        if (_window == IntPtr.Zero)
            return;
        _ = XDestroyWindow(_display, _window);
        _ = X11Native.XSync(_display, 0);
        _window = IntPtr.Zero;
    }

    /// <summary>
    /// Reads a text property as bytes, or <see langword="null"/> when it is missing.
    /// </summary>
    public byte[]? ReadProperty(string propertyName, string typeName)
    {
        IntPtr property = X11Native.XInternAtom(_display, propertyName, 0);
        IntPtr type = X11Native.XInternAtom(_display, typeName, 0);
        if (
            X11Native.XGetWindowProperty(
                _display,
                _window,
                property,
                0,
                1024,
                0,
                type,
                out _,
                out int format,
                out nuint itemCount,
                out _,
                out IntPtr data
            ) != X11Native.Success
            || data == IntPtr.Zero
        )
            return null;

        try
        {
            if (format != 8)
                return null;
            byte[] bytes = new byte[(int)itemCount];
            Marshal.Copy(data, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            _ = X11Native.XFree(data);
        }
    }

    public string? ReadUtf8Property(string propertyName) =>
        ReadProperty(propertyName, "UTF8_STRING") is { } bytes
            ? Encoding.UTF8.GetString(bytes)
            : null;

    public void Dispose()
    {
        Destroy();
        _ = X11Native.XCloseDisplay(_display);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XSetWindowAttributes
    {
        public nuint BackgroundPixmap;
        public nuint BackgroundPixel;
        public nuint BorderPixmap;
        public nuint BorderPixel;
        public int BitGravity;
        public int WinGravity;
        public int BackingStore;
        public nuint BackingPlanes;
        public nuint BackingPixel;
        public int SaveUnder;
        public nint EventMask;
        public nint DoNotPropagateMask;
        public int OverrideRedirect;
        public nuint Colormap;
        public nuint Cursor;
    }

    [DllImport("libX11.so.6")]
    private static extern IntPtr XCreateSimpleWindow(
        IntPtr display,
        IntPtr parent,
        int x,
        int y,
        uint width,
        uint height,
        uint borderWidth,
        nuint border,
        nuint background
    );

    [DllImport("libX11.so.6")]
    private static extern int XChangeWindowAttributes(
        IntPtr display,
        IntPtr window,
        nint valueMask,
        ref XSetWindowAttributes attributes
    );

    [DllImport("libX11.so.6", CharSet = CharSet.Ansi)]
    private static extern int XQueryExtension(
        IntPtr display,
        string name,
        out int majorOpcode,
        out int firstEvent,
        out int firstError
    );

    [DllImport("libX11.so.6")]
    private static extern int XMapWindow(IntPtr display, IntPtr window);

    [DllImport("libX11.so.6")]
    private static extern int XUnmapWindow(IntPtr display, IntPtr window);

    [DllImport("libX11.so.6")]
    private static extern int XResizeWindow(IntPtr display, IntPtr window, uint width, uint height);

    [DllImport("libX11.so.6")]
    private static extern int XDestroyWindow(IntPtr display, IntPtr window);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XCreateGC(
        IntPtr display,
        IntPtr drawable,
        nuint valueMask,
        IntPtr values
    );

    [DllImport("libX11.so.6")]
    private static extern int XFreeGC(IntPtr display, IntPtr gc);

    [DllImport("libX11.so.6")]
    private static extern int XSetForeground(IntPtr display, IntPtr gc, nuint foreground);

    [DllImport("libX11.so.6")]
    private static extern int XFillRectangle(
        IntPtr display,
        IntPtr drawable,
        IntPtr gc,
        int x,
        int y,
        uint width,
        uint height
    );
}
