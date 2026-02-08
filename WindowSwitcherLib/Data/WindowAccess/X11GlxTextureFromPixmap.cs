using System;
using System.Runtime.InteropServices;

namespace WindowSwitcherLib.Data.WindowAccess;

public sealed class X11GlxTextureFromPixmap : IDisposable
{
    private readonly string _windowId;
    private IntPtr _display;
    private IntPtr _pixmap;
    private IntPtr _glxPixmap;
    private IntPtr _fbConfigArray;
    private bool _initialized;
    private bool _bound;
    private glXBindTexImageEXTDelegate? _bindTexImage;
    private glXReleaseTexImageEXTDelegate? _releaseTexImage;

    public X11GlxTextureFromPixmap(string windowId)
    {
        ArgumentNullException.ThrowIfNull(windowId);
        _windowId = windowId;
    }

    public bool TryInitializeForCurrentContext(out string? error)
    {
        error = null;
        if (_initialized)
            return true;

        IntPtr currentDisplay = glXGetCurrentDisplay();
        if (currentDisplay == IntPtr.Zero)
        {
            error = "glXGetCurrentDisplay returned null (not running on X11/GLX).";
            return false;
        }

        IntPtr bindPtr = glXGetProcAddress("glXBindTexImageEXT");
        IntPtr releasePtr = glXGetProcAddress("glXReleaseTexImageEXT");
        if (bindPtr == IntPtr.Zero || releasePtr == IntPtr.Zero)
        {
            error = "Missing GLX_EXT_texture_from_pixmap entry points.";
            return false;
        }

        _bindTexImage = Marshal.GetDelegateForFunctionPointer<glXBindTexImageEXTDelegate>(bindPtr);
        _releaseTexImage = Marshal.GetDelegateForFunctionPointer<glXReleaseTexImageEXTDelegate>(releasePtr);

        _display = currentDisplay;
        int screen = XDefaultScreen(_display);
        if (screen < 0)
        {
            error = "XDefaultScreen failed.";
            return false;
        }

        if (!TryParseX11WindowId(_windowId, out IntPtr window))
        {
            error = "Invalid X11 window id.";
            return false;
        }

        // Ensure the window is redirected so we can name its pixmap.
        try
        {
            XCompositeRedirectWindow(_display, window, CompositeRedirectAutomatic);
        }
        catch
        {
            // Best effort: some compositors may already redirect.
        }

        _pixmap = XCompositeNameWindowPixmap(_display, window);
        if (_pixmap == IntPtr.Zero)
        {
            error = "XCompositeNameWindowPixmap failed.";
            return false;
        }

        if (!TryCreateGlxPixmap(screen, bindRgba: true, out error) &&
            !TryCreateGlxPixmap(screen, bindRgba: false, out error))
            return false;

        _initialized = true;
        return true;
    }

    private bool TryCreateGlxPixmap(int screen, bool bindRgba, out string? error)
    {
        error = null;

        IntPtr fbConfigArray = IntPtr.Zero;
        IntPtr glxPixmap = IntPtr.Zero;

        int[] fbAttribs =
        [
            GLX_DRAWABLE_TYPE, GLX_PIXMAP_BIT,
            GLX_RENDER_TYPE, GLX_RGBA_BIT,
            GLX_X_RENDERABLE, 1,
            bindRgba ? GLX_BIND_TO_TEXTURE_RGBA_EXT : GLX_BIND_TO_TEXTURE_RGB_EXT, 1,
            GLX_BIND_TO_TEXTURE_TARGETS_EXT, GLX_TEXTURE_2D_BIT_EXT,
            0
        ];

        fbConfigArray = glXChooseFBConfig(_display, screen, fbAttribs, out int fbCount);
        if (fbConfigArray == IntPtr.Zero || fbCount <= 0)
        {
            error = "glXChooseFBConfig failed.";
            return false;
        }

        IntPtr fbConfig = Marshal.ReadIntPtr(fbConfigArray);
        int textureFormat = bindRgba ? GLX_TEXTURE_FORMAT_RGBA_EXT : GLX_TEXTURE_FORMAT_RGB_EXT;
        int[] pixmapAttribs =
        [
            GLX_TEXTURE_TARGET_EXT, GLX_TEXTURE_2D_EXT,
            GLX_TEXTURE_FORMAT_EXT, textureFormat,
            0
        ];

        glxPixmap = glXCreatePixmap(_display, fbConfig, _pixmap, pixmapAttribs);
        if (glxPixmap == IntPtr.Zero)
        {
            error = "glXCreatePixmap failed.";
            try
            {
                _ = XFree(fbConfigArray);
            }
            catch
            {
                // Ignore failures on teardown.
            }
            return false;
        }

        _fbConfigArray = fbConfigArray;
        _glxPixmap = glxPixmap;
        return true;
    }

    public bool TryBindToCurrentTexture(out string? error)
    {
        error = null;
        if (!_initialized)
        {
            error = "Not initialized.";
            return false;
        }

        if (_bound)
            return true;

        if (_bindTexImage is null)
        {
            error = "Bind function is null.";
            return false;
        }

        try
        {
            _bindTexImage(_display, _glxPixmap, GLX_FRONT_LEFT_EXT, IntPtr.Zero);
            _bound = true;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public void Release()
    {
        if (!_bound)
            return;

        try
        {
            _releaseTexImage?.Invoke(_display, _glxPixmap, GLX_FRONT_LEFT_EXT);
        }
        catch
        {
            // Ignore failures on teardown.
        }
        finally
        {
            _bound = false;
        }
    }

    public void Dispose()
    {
        Release();

        if (_glxPixmap != IntPtr.Zero)
        {
            try
            {
                glXDestroyPixmap(_display, _glxPixmap);
            }
            catch
            {
                // Ignore failures on teardown.
            }
            _glxPixmap = IntPtr.Zero;
        }

        if (_pixmap != IntPtr.Zero)
        {
            try
            {
                _ = XFreePixmap(_display, _pixmap);
            }
            catch
            {
                // Ignore failures on teardown.
            }
            _pixmap = IntPtr.Zero;
        }

        if (_fbConfigArray != IntPtr.Zero)
        {
            try
            {
                _ = XFree(_fbConfigArray);
            }
            catch
            {
                // Ignore failures on teardown.
            }
            _fbConfigArray = IntPtr.Zero;
        }

        _initialized = false;
    }

    private static bool TryParseX11WindowId(string value, out IntPtr window)
    {
        window = IntPtr.Zero;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string trimmed = value.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[2..];

        if (!ulong.TryParse(trimmed, System.Globalization.NumberStyles.HexNumber, provider: null, out ulong parsed))
            return false;

        window = (IntPtr)unchecked((nint)parsed);
        return true;
    }

    private const int CompositeRedirectAutomatic = 1;

    private const int GLX_RGBA_BIT = 0x00000001;
    private const int GLX_PIXMAP_BIT = 0x00000002;
    private const int GLX_DRAWABLE_TYPE = 0x8010;
    private const int GLX_RENDER_TYPE = 0x8011;
    private const int GLX_X_RENDERABLE = 0x8012;

    // GLX_EXT_texture_from_pixmap
    private const int GLX_BIND_TO_TEXTURE_RGB_EXT = 0x20D0;
    private const int GLX_BIND_TO_TEXTURE_RGBA_EXT = 0x20D1;
    private const int GLX_BIND_TO_TEXTURE_TARGETS_EXT = 0x20D3;
    private const int GLX_TEXTURE_2D_BIT_EXT = 0x00000002;

    private const int GLX_TEXTURE_FORMAT_EXT = 0x20D5;
    private const int GLX_TEXTURE_FORMAT_RGB_EXT = 0x20D7;
    private const int GLX_TEXTURE_FORMAT_RGBA_EXT = 0x20D8;
    private const int GLX_TEXTURE_TARGET_EXT = 0x20D6;
    private const int GLX_TEXTURE_2D_EXT = 0x20DC;
    private const int GLX_FRONT_LEFT_EXT = 0x20DE;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void glXBindTexImageEXTDelegate(IntPtr display, IntPtr drawable, int buffer, IntPtr attribList);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void glXReleaseTexImageEXTDelegate(IntPtr display, IntPtr drawable, int buffer);

    [DllImport("libGL.so.1")]
    private static extern IntPtr glXGetCurrentDisplay();

    [DllImport("libGL.so.1", EntryPoint = "glXChooseFBConfig")]
    private static extern IntPtr glXChooseFBConfig(IntPtr display, int screen, int[] attribList, out int count);

    [DllImport("libGL.so.1", EntryPoint = "glXCreatePixmap")]
    private static extern IntPtr glXCreatePixmap(IntPtr display, IntPtr fbConfig, IntPtr pixmap, int[] attribList);

    [DllImport("libGL.so.1", EntryPoint = "glXDestroyPixmap")]
    private static extern void glXDestroyPixmap(IntPtr display, IntPtr glxPixmap);

    [DllImport("libGL.so.1", EntryPoint = "glXGetProcAddressARB")]
    private static extern IntPtr glXGetProcAddress([MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport("libX11.so.6")]
    private static extern int XDefaultScreen(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XFreePixmap(IntPtr display, IntPtr pixmap);

    [DllImport("libX11.so.6")]
    private static extern int XFree(IntPtr data);

    [DllImport("libXcomposite.so.1")]
    private static extern void XCompositeRedirectWindow(IntPtr display, IntPtr window, int update);

    [DllImport("libXcomposite.so.1")]
    private static extern IntPtr XCompositeNameWindowPixmap(IntPtr display, IntPtr window);
}
