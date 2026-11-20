using System.Globalization;
using System.Runtime.InteropServices;
using Serilog;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.X11;

internal sealed class X11WindowCaptureSession : IX11WindowCaptureSession
{
    private const int BufferPoolSize = 3;
    private const int DamageEventOffset = 0;
    private const int ShmPermissions = 0x180;
    private const string DisableShmEnvironmentVariable = "WINDOW_SWITCHER_X11_DISABLE_SHM";
    private const string DisableRenderEnvironmentVariable = "WINDOW_SWITCHER_X11_DISABLE_RENDER";

    // Picture transforms and filters were added in RENDER 0.6.
    private const int MinimumRenderMinorVersion = 6;
    private static readonly ILogger Logger = Log.ForContext<X11WindowCaptureSession>();
    private static readonly nint XImageDataOffset = Marshal.OffsetOf<XImage>(nameof(XImage.Data));

    private readonly object _sync = new();
    private readonly NativeFrameBufferPool _bufferPool = new(BufferPoolSize);
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
    private int _depth;
    private X11RenderScaler? _scaler;
    private int _readbackWidth;
    private int _readbackHeight;
    private bool _isRedirected;
    private bool _pixmapStale;
    private bool _sharedMemoryDisabled;
    private bool? _renderSupported;
    private bool _renderDisabled;
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

    private string WindowIdText => FormattableString.Invariant($"0x{(long)_window:x}");

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
        catch (Exception exception)
        {
            Logger.Warning(exception, "X11 capture support probe failed");
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

            bool redirectedByThisClient = false;
            if (!HasCompositingManager(display))
            {
                X11Native.XCompositeRedirectWindow(
                    display,
                    window,
                    X11Native.CompositeRedirectAutomatic
                );
                _ = X11Native.XSync(display, discard: 0);
                redirectedByThisClient = true;
            }

            IntPtr damage = X11Native.XDamageCreate(
                display,
                window,
                X11Native.DamageReportNonEmpty
            );
            if (damage == IntPtr.Zero)
                return CloseAndReturnNull(display);

            // MapNotify tells the session that the composite pixmap was reallocated.
            _ = X11Native.XSelectInput(display, window, X11Native.StructureNotifyMask);
            _ = X11Native.XFlush(display);
            return new X11WindowCaptureSession(display, window, damageEventBase, damage)
            {
                _isRedirected = redirectedByThisClient,
            };
        }
        catch (Exception exception)
        {
            Logger.Error(
                exception,
                "X11 capture session creation failed for window {WindowId}",
                windowId
            );
            if (display != IntPtr.Zero)
                _ = X11Native.XCloseDisplay(display);
            return null;
        }
    }

    public bool TryCaptureFrame(ScreenshotRequest request, out NativeBgraPreviewFrame? frame)
    {
        frame = null;
        lock (_sync)
        {
            if (_disposed)
                return false;

            // The consumer still holds every buffer: skip this frame without reading the window back.
            if (!_bufferPool.HasAvailableBuffer)
                return true;

            if (!EnsureCaptureSurface(request))
                return false;

            frame = X11CaptureFallback.Capture(
                _shmImage != IntPtr.Zero,
                () => CaptureShmFrame(request),
                DisableSharedMemory,
                () => CaptureXImageFrame(request)
            );
            return frame is not null;
        }
    }

    public async Task<bool> WaitForDamageAsync(int timeoutMs, CancellationToken cancellationToken)
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
            catch (Exception exception)
            {
                Logger.Debug(
                    exception,
                    "XDamageDestroy failed for window {WindowId}",
                    WindowIdText
                );
            }

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
            catch (Exception exception)
            {
                Logger.Debug(
                    exception,
                    "XCompositeUnredirectWindow failed for window {WindowId}",
                    WindowIdText
                );
            }

            try
            {
                _ = X11Native.XCloseDisplay(_display);
            }
            catch (Exception exception)
            {
                Logger.Debug(exception, "XCloseDisplay failed for window {WindowId}", WindowIdText);
            }

            _bufferPool.Dispose();
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

    private static bool HasCompositingManager(IntPtr display)
    {
        try
        {
            int screen = X11Native.XDefaultScreen(display);
            IntPtr atom = X11Native.XInternAtom(
                display,
                FormattableString.Invariant($"_NET_WM_CM_S{screen}"),
                onlyIfExists: 1
            );
            if (atom == IntPtr.Zero)
                return false;

            return X11Native.XGetSelectionOwner(display, atom) != IntPtr.Zero;
        }
        catch (Exception exception)
        {
            Logger.Debug(exception, "X11 compositing manager detection failed");
            return false;
        }
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

    private bool EnsureCaptureSurface(ScreenshotRequest request)
    {
        if (
            X11Native.XGetWindowAttributes(_display, _window, out XWindowAttributes attributes) == 0
        )
            return false;
        if (attributes.Width <= 0 || attributes.Height <= 0)
            return false;

        if (
            _pixmap == IntPtr.Zero
            || _pixmapStale
            || _width != attributes.Width
            || _height != attributes.Height
        )
        {
            ReleaseCaptureSurface();
            _pixmapStale = false;

            _pixmap = X11Native.XCompositeNameWindowPixmap(_display, _window);
            if (_pixmap == IntPtr.Zero)
                return false;

            if (!TryReadPixmapGeometry(out int width, out int height, out int depth))
            {
                ReleaseCaptureSurface();
                return false;
            }

            _width = width;
            _height = height;
            _depth = depth;
        }

        EnsureReadbackSurface(attributes.Visual, request);
        return true;
    }

    /// <summary>
    /// Sizes the readback (XRender scaler and SHM image) for the requested thumbnail size.
    /// The SHM image matches the scaler output, or the full pixmap when the X server does not
    /// downscale. Both are rebuilt when the target size changes (preview window resized).
    /// </summary>
    private void EnsureReadbackSurface(IntPtr visual, ScreenshotRequest request)
    {
        (int targetWidth, int targetHeight) = X11FrameConverter.CalculateTargetSize(
            _width,
            _height,
            request
        );
        bool scaleOnServer = X11RenderScalePlan.ShouldScaleOnServer(
            _width,
            _height,
            targetWidth,
            targetHeight,
            IsRenderAvailable()
        );
        int readbackWidth = scaleOnServer ? targetWidth : _width;
        int readbackHeight = scaleOnServer ? targetHeight : _height;
        if (
            _readbackWidth == readbackWidth
            && _readbackHeight == readbackHeight
            && scaleOnServer == (_scaler is not null)
        )
            return;

        ReleaseReadbackSurface();

        if (scaleOnServer)
            _scaler = TryCreateScaler(visual, targetWidth, targetHeight);

        _readbackWidth = _scaler?.Width ?? _width;
        _readbackHeight = _scaler?.Height ?? _height;
        TryCreateShmImage(visual);
    }

    private X11RenderScaler? TryCreateScaler(IntPtr visual, int targetWidth, int targetHeight)
    {
        try
        {
            X11RenderScaler? scaler = X11RenderScaler.TryCreate(
                _display,
                _pixmap,
                visual,
                _depth,
                _width,
                _height,
                targetWidth,
                targetHeight
            );
            if (scaler is null)
            {
                if (TryDisableRender())
                {
                    Logger.Warning(
                        "X11 XRender has no format for the visual of window {WindowId}, using CPU scaling",
                        WindowIdText
                    );
                }

                return null;
            }

            _ = X11Native.XSync(_display, discard: 0);
            return scaler;
        }
        catch (Exception exception)
        {
            if (TryDisableRender())
            {
                Logger.Warning(
                    exception,
                    "X11 XRender scaler creation failed for window {WindowId}, using CPU scaling",
                    WindowIdText
                );
            }

            return null;
        }
    }

    private bool IsRenderAvailable()
    {
        if (_renderDisabled)
            return false;

        _renderSupported ??= QueryRender();
        return _renderSupported.Value;
    }

    private bool QueryRender()
    {
        if (IsEnvironmentFlagSet(DisableRenderEnvironmentVariable))
        {
            Logger.Warning(
                "X11 XRender scaling disabled by {EnvironmentVariable}, using CPU scaling",
                DisableRenderEnvironmentVariable
            );
            return false;
        }

        try
        {
            if (
                X11Native.XRenderQueryExtension(_display, out _, out _) != 0
                && X11Native.XRenderQueryVersion(_display, out int major, out int minor) != 0
                && (major > 0 || minor >= MinimumRenderMinorVersion)
            )
                return true;

            Logger.Warning("X11 RENDER extension unavailable, using CPU scaling");
            return false;
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "X11 XRender probe failed, using CPU scaling");
            return false;
        }
    }

    /// <summary>
    /// Switches this session to the CPU scaling path for good.
    /// </summary>
    /// <returns><see langword="true"/> the first time, so the caller logs the cause once.</returns>
    private bool TryDisableRender()
    {
        if (_renderDisabled)
            return false;

        _renderDisabled = true;
        return true;
    }

    private bool TryReadPixmapGeometry(out int width, out int height, out int depth)
    {
        width = 0;
        height = 0;
        depth = 0;

        if (
            X11Native.XGetGeometry(
                _display,
                _pixmap,
                out _,
                out _,
                out _,
                out uint pixmapWidth,
                out uint pixmapHeight,
                out _,
                out uint pixmapDepth
            ) == 0
        )
            return false;

        if (
            pixmapWidth == 0
            || pixmapHeight == 0
            || pixmapWidth > int.MaxValue
            || pixmapHeight > int.MaxValue
        )
            return false;
        if (pixmapDepth == 0 || pixmapDepth > int.MaxValue)
            return false;

        width = (int)pixmapWidth;
        height = (int)pixmapHeight;
        depth = (int)pixmapDepth;
        return true;
    }

    private void TryCreateShmImage(IntPtr visual)
    {
        if (_sharedMemoryDisabled || IsShmDisabled())
            return;

        try
        {
            if (X11Native.XShmQueryExtension(_display) == 0)
                return;

            _shmInfo = new XShmSegmentInfo { ShmId = -1 };
            _shmImage = X11Native.XShmCreateImage(
                _display,
                visual,
                (uint)_depth,
                X11Native.ZPixmap,
                IntPtr.Zero,
                ref _shmInfo,
                (uint)_readbackWidth,
                (uint)_readbackHeight
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
            if (imageBytes <= 0)
            {
                ReleaseShmImage();
                return;
            }

            _shmInfo.ShmId = X11Native.shmget(
                X11Native.IpcPrivate,
                (UIntPtr)imageBytes,
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
        catch (Exception exception)
        {
            Logger.Warning(
                exception,
                "X11 SHM unavailable for window {WindowId}, falling back to XGetImage",
                WindowIdText
            );
            ReleaseShmImage();
        }
    }

    private static bool IsShmDisabled() => IsEnvironmentFlagSet(DisableShmEnvironmentVariable);

    private static bool IsEnvironmentFlagSet(string variable)
    {
        string? value = Environment.GetEnvironmentVariable(variable);
        return string.Equals(value, "1", StringComparison.Ordinal)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the drawable to read back: the downscaled pixmap when XRender is used, the
    /// window pixmap otherwise. Returns <see cref="IntPtr.Zero"/> when the composite chain
    /// failed; XRender is then disabled and the next frame is resized for the CPU path.
    /// </summary>
    private IntPtr RenderReadbackDrawable()
    {
        if (_scaler is null)
            return _pixmap;

        try
        {
            return _scaler.Render();
        }
        catch (Exception exception)
        {
            if (TryDisableRender())
            {
                Logger.Warning(
                    exception,
                    "X11 XRender composite failed for window {WindowId}, using CPU scaling",
                    WindowIdText
                );
            }

            return IntPtr.Zero;
        }
    }

    private X11CaptureAttempt CaptureShmFrame(ScreenshotRequest request)
    {
        try
        {
            IntPtr drawable = RenderReadbackDrawable();
            // A composite failure is not a SHM failure: skip the frame but keep SHM enabled.
            if (drawable == IntPtr.Zero)
                return new X11CaptureAttempt(BackendSucceeded: true, Frame: null);

            if (
                X11Native.XShmGetImage(
                    _display,
                    drawable,
                    _shmImage,
                    x: 0,
                    y: 0,
                    X11Native.AllPlanes
                ) == 0
            )
                return new X11CaptureAttempt(BackendSucceeded: false, Frame: null);

            _ = X11Native.XSync(_display, discard: 0);
            return new X11CaptureAttempt(
                BackendSucceeded: true,
                X11FrameConverter.CreateFrame(_shmImage, request, _bufferPool)
            );
        }
        catch (Exception exception)
        {
            Logger.Warning(
                exception,
                "X11 SHM frame capture failed for window {WindowId}",
                WindowIdText
            );
            return new X11CaptureAttempt(BackendSucceeded: false, Frame: null);
        }
    }

    private void DisableSharedMemory()
    {
        _sharedMemoryDisabled = true;
        ReleaseShmImage();
    }

    private NativeBgraPreviewFrame? CaptureXImageFrame(ScreenshotRequest request)
    {
        IntPtr image = IntPtr.Zero;
        try
        {
            IntPtr drawable = RenderReadbackDrawable();
            if (drawable == IntPtr.Zero)
                return null;

            image = X11Native.XGetImage(
                _display,
                drawable,
                x: 0,
                y: 0,
                (uint)_readbackWidth,
                (uint)_readbackHeight,
                X11Native.AllPlanes,
                X11Native.ZPixmap
            );
            if (image == IntPtr.Zero)
                return null;

            return X11FrameConverter.CreateFrame(image, request, _bufferPool);
        }
        catch (Exception exception)
        {
            Logger.Warning(
                exception,
                "X11 XGetImage frame capture failed for window {WindowId}",
                WindowIdText
            );
            return null;
        }
        finally
        {
            DestroyImage(ref image);
        }
    }

    private static void DestroyImage(ref IntPtr image)
    {
        if (image == IntPtr.Zero)
            return;

        try
        {
            _ = X11Native.XDestroyImage(image);
        }
        catch (Exception exception)
        {
            Logger.Debug(exception, "XDestroyImage failed for captured frame image");
        }
        finally
        {
            image = IntPtr.Zero;
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
                    else if (xevent.Type == X11Native.MapNotify)
                    {
                        // Mapping reallocates the window pixmap: the named one stops updating
                        // (for example after a minimize and restore). Capture the new one.
                        _pixmapStale = true;
                        sawDamage = true;
                    }
                }

                if (sawDamage && _damage != IntPtr.Zero)
                {
                    X11Native.XDamageSubtract(_display, _damage, IntPtr.Zero, IntPtr.Zero);
                    _ = X11Native.XFlush(_display);
                }
            }
            catch (Exception exception)
            {
                Logger.Warning(
                    exception,
                    "X11 damage event processing failed for window {WindowId}",
                    WindowIdText
                );
                return false;
            }

            return sawDamage;
        }
    }

    private void ReleaseCaptureSurface()
    {
        // The scaler holds a picture on _pixmap: release it before freeing the pixmap.
        ReleaseReadbackSurface();

        try
        {
            if (_pixmap != IntPtr.Zero)
                _ = X11Native.XFreePixmap(_display, _pixmap);
        }
        catch (Exception exception)
        {
            Logger.Debug(exception, "XFreePixmap failed for window {WindowId}", WindowIdText);
        }

        _pixmap = IntPtr.Zero;
        _width = 0;
        _height = 0;
        _depth = 0;
    }

    private void ReleaseReadbackSurface()
    {
        try
        {
            _scaler?.Dispose();
        }
        catch (Exception exception)
        {
            Logger.Debug(
                exception,
                "XRender scaler release failed for window {WindowId}",
                WindowIdText
            );
        }

        _scaler = null;
        ReleaseShmImage();
        _readbackWidth = 0;
        _readbackHeight = 0;
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
            catch (Exception exception)
            {
                Logger.Debug(exception, "XShmDetach failed for window {WindowId}", WindowIdText);
            }

            try
            {
                _ = X11Native.XDestroyImage(_shmImage);
            }
            catch (Exception exception)
            {
                Logger.Debug(
                    exception,
                    "XDestroyImage failed for SHM image of window {WindowId}",
                    WindowIdText
                );
            }
        }

        if (_shmAddress != IntPtr.Zero && _shmAddress != new IntPtr(-1))
        {
            try
            {
                _ = X11Native.shmdt(_shmAddress);
            }
            catch (Exception exception)
            {
                Logger.Debug(exception, "shmdt failed for window {WindowId}", WindowIdText);
            }
        }

        if (_shmInfo.ShmId >= 0)
        {
            try
            {
                _ = X11Native.shmctl(_shmInfo.ShmId, X11Native.IpcRmId, IntPtr.Zero);
            }
            catch (Exception exception)
            {
                Logger.Debug(
                    exception,
                    "shmctl IPC_RMID failed for SHM segment {ShmId}",
                    _shmInfo.ShmId
                );
            }
        }

        _shmImage = IntPtr.Zero;
        _shmInfo = default;
        _shmInfo.ShmId = -1;
        _shmAddress = IntPtr.Zero;
    }
}
