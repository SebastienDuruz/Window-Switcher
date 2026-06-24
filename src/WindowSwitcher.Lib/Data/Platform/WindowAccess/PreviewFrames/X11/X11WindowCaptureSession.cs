using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.X11;

internal sealed class X11WindowCaptureSession : IDisposable
{
    private const int DamageEventOffset = 0;
    private const int ShmPermissions = 0x180;
    private static readonly nint XImageDataOffset =
        Marshal.OffsetOf<XImage>(nameof(XImage.Data));

    private readonly object _sync = new();
    private readonly IntPtr _display;
    private readonly IntPtr _window;
    private readonly int _damageEventType;
    private IntPtr _damage;
    private IntPtr _pixmap;
    private IntPtr _shmImage;
    private XShmSegmentInfo _shmInfo = new() { ShmId = -1 };
    private IntPtr _shmAddress;
    private int _width;
    private int _height;
    private bool _isRedirected;
    private bool _disposed;

    private X11WindowCaptureSession(
        IntPtr display,
        IntPtr window,
        int damageEventBase,
        IntPtr damage
    )
    {
        _display = display;
        _window = window;
        _damageEventType = damageEventBase + DamageEventOffset;
        _damage = damage;
    }

    public static bool IsSupported()
    {
        if (!OperatingSystem.IsLinux())
            return false;

        IntPtr display = IntPtr.Zero;
        try
        {
            X11Native.EnsureInitialized();
            display = X11Native.XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero)
                return false;

            return QueryComposite(display) && QueryDamage(display, out _);
        }
        catch
        {
            return false;
        }
        finally
        {
            if (display != IntPtr.Zero)
                _ = X11Native.XCloseDisplay(display);
        }
    }

    public static X11WindowCaptureSession? TryCreate(string windowId)
    {
        if (!OperatingSystem.IsLinux())
            return null;
        if (!TryParseWindowId(windowId, out IntPtr window))
            return null;

        IntPtr display = IntPtr.Zero;
        try
        {
            X11Native.EnsureInitialized();
            display = X11Native.XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero)
                return null;
            if (!QueryComposite(display) || !QueryDamage(display, out int damageEventBase))
                return CloseAndReturnNull(display);

            X11Native.XCompositeRedirectWindow(
                display,
                window,
                X11Native.CompositeRedirectAutomatic
            );
            _ = X11Native.XSync(display, discard: 0);

            IntPtr damage = X11Native.XDamageCreate(
                display,
                window,
                X11Native.DamageReportNonEmpty
            );
            if (damage == IntPtr.Zero)
                return CloseAndReturnNull(display);

            _ = X11Native.XFlush(display);
            return new X11WindowCaptureSession(
                display,
                window,
                damageEventBase,
                damage
            )
            {
                _isRedirected = true,
            };
        }
        catch
        {
            if (display != IntPtr.Zero)
                _ = X11Native.XCloseDisplay(display);
            return null;
        }
    }

    public Bitmap? CaptureFrame(ScreenshotRequest request)
    {
        lock (_sync)
        {
            if (_disposed)
                return null;

            if (!EnsureCaptureSurface())
                return null;

            if (_shmImage != IntPtr.Zero)
                return CaptureShmFrame(request);

            return CaptureXImageFrame(request);
        }
    }

    public async Task<bool> WaitForDamageAsync(
        int timeoutMs,
        CancellationToken cancellationToken
    )
    {
        int delayMs = Math.Min(33, Math.Max(1, timeoutMs));
        long deadlineTicks = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs).Ticks;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (TryConsumeDamageEvent())
                return true;

            if (DateTimeOffset.UtcNow.Ticks >= deadlineTicks)
                return false;

            await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            ReleaseCaptureSurface();

            try
            {
                if (_damage != IntPtr.Zero)
                    X11Native.XDamageDestroy(_display, _damage);
                _damage = IntPtr.Zero;
            }
            catch { }

            try
            {
                if (_isRedirected)
                {
                    X11Native.XCompositeUnredirectWindow(
                        _display,
                        _window,
                        X11Native.CompositeRedirectAutomatic
                    );
                }
            }
            catch { }

            try
            {
                _ = X11Native.XCloseDisplay(_display);
            }
            catch { }
        }
    }

    private static bool QueryComposite(IntPtr display)
    {
        if (X11Native.XCompositeQueryExtension(display, out _, out _) == 0)
            return false;

        int major = 0;
        int minor = 4;
        return X11Native.XCompositeQueryVersion(display, ref major, ref minor) != 0;
    }

    private static bool QueryDamage(IntPtr display, out int damageEventBase)
    {
        damageEventBase = 0;
        if (X11Native.XDamageQueryExtension(display, out damageEventBase, out _) == 0)
            return false;

        int major = 1;
        int minor = 1;
        return X11Native.XDamageQueryVersion(display, ref major, ref minor) != 0;
    }

    private static X11WindowCaptureSession? CloseAndReturnNull(IntPtr display)
    {
        _ = X11Native.XCloseDisplay(display);
        return null;
    }

    private static bool TryParseWindowId(string windowId, out IntPtr window)
    {
        window = IntPtr.Zero;
        if (string.IsNullOrWhiteSpace(windowId))
            return false;

        string value = windowId.Trim();
        NumberStyles styles = NumberStyles.Integer;
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            value = value[2..];
            styles = NumberStyles.HexNumber;
        }

        if (!ulong.TryParse(value, styles, CultureInfo.InvariantCulture, out ulong parsed))
            return false;
        if (parsed == 0 || parsed > long.MaxValue)
            return false;

        window = new IntPtr(unchecked((long)parsed));
        return true;
    }

    private bool EnsureCaptureSurface()
    {
        if (
            X11Native.XGetWindowAttributes(_display, _window, out XWindowAttributes attributes)
            == 0
        )
            return false;
        if (attributes.Width <= 0 || attributes.Height <= 0)
            return false;

        if (
            _pixmap != IntPtr.Zero
            && _width == attributes.Width
            && _height == attributes.Height
        )
            return true;

        ReleaseCaptureSurface();
        _width = attributes.Width;
        _height = attributes.Height;

        _pixmap = X11Native.XCompositeNameWindowPixmap(_display, _window);
        if (_pixmap == IntPtr.Zero)
            return false;

        TryCreateShmImage(attributes);
        return true;
    }

    private void TryCreateShmImage(XWindowAttributes attributes)
    {
        try
        {
            if (X11Native.XShmQueryExtension(_display) == 0)
                return;

            _shmInfo = new XShmSegmentInfo { ShmId = -1 };
            _shmImage = X11Native.XShmCreateImage(
                _display,
                attributes.Visual,
                (uint)Math.Max(0, attributes.Depth),
                X11Native.ZPixmap,
                IntPtr.Zero,
                ref _shmInfo,
                (uint)_width,
                (uint)_height
            );
            if (_shmImage == IntPtr.Zero)
                return;

            XImage image = Marshal.PtrToStructure<XImage>(_shmImage);
            if (image.BytesPerLine <= 0 || image.Height <= 0)
            {
                ReleaseShmImage();
                return;
            }

            int imageBytes = checked(image.BytesPerLine * image.Height);
            _shmInfo.ShmId = X11Native.shmget(
                X11Native.IpcPrivate,
                (UIntPtr)(uint)imageBytes,
                X11Native.IpcCreat | ShmPermissions
            );
            if (_shmInfo.ShmId < 0)
            {
                ReleaseShmImage();
                return;
            }

            _shmAddress = X11Native.shmat(_shmInfo.ShmId, IntPtr.Zero, 0);
            if (_shmAddress == new IntPtr(-1))
            {
                _ = X11Native.shmctl(_shmInfo.ShmId, X11Native.IpcRmId, IntPtr.Zero);
                _shmInfo.ShmId = -1;
                _shmAddress = IntPtr.Zero;
                ReleaseShmImage();
                return;
            }

            _shmInfo.ShmAddr = _shmAddress;
            _shmInfo.ReadOnly = 0;
            Marshal.WriteIntPtr(_shmImage, checked((int)XImageDataOffset), _shmAddress);
            if (X11Native.XShmAttach(_display, ref _shmInfo) == 0)
            {
                ReleaseShmImage();
                return;
            }

            _ = X11Native.XSync(_display, discard: 0);
            _ = X11Native.shmctl(_shmInfo.ShmId, X11Native.IpcRmId, IntPtr.Zero);
        }
        catch
        {
            ReleaseShmImage();
        }
    }

    private Bitmap? CaptureShmFrame(ScreenshotRequest request)
    {
        try
        {
            if (
                X11Native.XShmGetImage(
                    _display,
                    _pixmap,
                    _shmImage,
                    x: 0,
                    y: 0,
                    X11Native.AllPlanes
                )
                == 0
            )
                return null;

            _ = X11Native.XSync(_display, discard: 0);
            return X11FrameConverter.CreateBitmap(_shmImage, request);
        }
        catch
        {
            ReleaseShmImage();
            return null;
        }
    }

    private Bitmap? CaptureXImageFrame(ScreenshotRequest request)
    {
        IntPtr image = IntPtr.Zero;
        try
        {
            image = X11Native.XGetImage(
                _display,
                _pixmap,
                x: 0,
                y: 0,
                (uint)_width,
                (uint)_height,
                X11Native.AllPlanes,
                X11Native.ZPixmap
            );
            if (image == IntPtr.Zero)
                return null;

            return X11FrameConverter.CreateBitmap(image, request);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (image != IntPtr.Zero)
            {
                try
                {
                    _ = X11Native.XDestroyImage(image);
                }
                catch { }
            }
        }
    }

    private bool TryConsumeDamageEvent()
    {
        lock (_sync)
        {
            if (_disposed)
                return false;

            bool sawDamage = false;
            try
            {
                while (X11Native.XPending(_display) > 0)
                {
                    _ = X11Native.XNextEvent(_display, out XEvent xevent);
                    if (xevent.Type == _damageEventType)
                        sawDamage = true;
                }

                if (sawDamage && _damage != IntPtr.Zero)
                {
                    X11Native.XDamageSubtract(
                        _display,
                        _damage,
                        IntPtr.Zero,
                        IntPtr.Zero
                    );
                    _ = X11Native.XFlush(_display);
                }
            }
            catch
            {
                return false;
            }

            return sawDamage;
        }
    }

    private void ReleaseCaptureSurface()
    {
        ReleaseShmImage();

        try
        {
            if (_pixmap != IntPtr.Zero)
                _ = X11Native.XFreePixmap(_display, _pixmap);
        }
        catch { }

        _pixmap = IntPtr.Zero;
        _width = 0;
        _height = 0;
    }

    private void ReleaseShmImage()
    {
        if (_shmImage != IntPtr.Zero)
        {
            try
            {
                if (_shmInfo.ShmAddr != IntPtr.Zero)
                    _ = X11Native.XShmDetach(_display, ref _shmInfo);
            }
            catch { }

            try
            {
                _ = X11Native.XDestroyImage(_shmImage);
            }
            catch { }
        }

        if (_shmAddress != IntPtr.Zero && _shmAddress != new IntPtr(-1))
        {
            try
            {
                _ = X11Native.shmdt(_shmAddress);
            }
            catch { }
        }

        if (_shmInfo.ShmId >= 0)
        {
            try
            {
                _ = X11Native.shmctl(_shmInfo.ShmId, X11Native.IpcRmId, IntPtr.Zero);
            }
            catch { }
        }

        _shmImage = IntPtr.Zero;
        _shmInfo = default;
        _shmInfo.ShmId = -1;
        _shmAddress = IntPtr.Zero;
    }
}
