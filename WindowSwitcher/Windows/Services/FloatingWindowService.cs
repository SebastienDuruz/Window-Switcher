using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using WindowSwitcherLib.Data.Common;
using WindowSwitcherLib.Data.Configuration;
using WindowSwitcherLib.Data.Logging;
using WindowSwitcherLib.Data.Platform.Interop;
using WindowSwitcherLib.Data.Platform.WindowAccess.PreviewFrames;
using WindowSwitcherLib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcherLib.Domain.Models;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace WindowSwitcher.Windows.Services;

internal sealed class FloatingWindowService
{
    private const double TitleReservedHeight = 12;
    private const double PreviewBorderThickness = 2;
    private const int PreviewRefreshIntervalMsPipeWire = 10;
    private const int PreviewRefreshIntervalMsScreenshot = 100;
    private const int PreviewRequestTimeoutMsScreenshot = 1_500;

    private readonly Window _ownerWindow;
    private readonly WindowConfig _windowConfig;
    private readonly IPreviewFrameProvider _previewFrameProvider;
    private readonly Image _windowScreenshot;
    private readonly Border _previewBorder;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _previewUpdateSemaphore = new(1, 1);
    private Bitmap? _currentScreenshot;
    private Bitmap? _previousScreenshot;
    private IntPtr _thumbnailHandle = IntPtr.Zero;
    private volatile bool _isClosing;
    private int _targetScreenshotWidthPx;
    private int _targetScreenshotHeightPx;

    public FloatingWindowService(
        Window ownerWindow,
        WindowConfig windowConfig,
        IPreviewFrameProvider previewFrameProvider,
        Image windowScreenshot,
        Border previewBorder)
    {
        ArgumentNullException.ThrowIfNull(ownerWindow);
        ArgumentNullException.ThrowIfNull(windowConfig);
        ArgumentNullException.ThrowIfNull(previewFrameProvider);
        ArgumentNullException.ThrowIfNull(windowScreenshot);
        ArgumentNullException.ThrowIfNull(previewBorder);

        _ownerWindow = ownerWindow;
        _windowConfig = windowConfig;
        _previewFrameProvider = previewFrameProvider;
        _windowScreenshot = windowScreenshot;
        _previewBorder = previewBorder;
    }

    public void Start()
    {
        _ = Task.Run(() => RunPeriodicTask(_cts.Token));
    }

    public void OnWindowResized(bool activateWindowsPreview)
    {
        UpdateLayout();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && activateWindowsPreview)
            RegisterWindowThumbnail();
    }

    public void SetPreviewHighlight(bool isSelected)
    {
        _previewBorder.IsVisible = isSelected;
        if (!isSelected && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            _ = UpdateScreenshot(PreviewRequestTimeoutMsScreenshot, _cts.Token);
    }

    public void UpdateLayout()
    {
        double topOffset = TitleReservedHeight;
        double previewWidth = Math.Max(0, _ownerWindow.Width);
        double previewHeight = Math.Max(0, _ownerWindow.Height - topOffset);

        double left = RoundToPixel(0);
        double top = RoundToPixel(topOffset);
        previewWidth = RoundToPixel(previewWidth);
        previewHeight = RoundToPixel(previewHeight);

        Canvas.SetLeft(_previewBorder, left);
        Canvas.SetTop(_previewBorder, top);
        _previewBorder.Width = previewWidth;
        _previewBorder.Height = previewHeight;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        Canvas.SetLeft(_windowScreenshot, left);
        Canvas.SetTop(_windowScreenshot, top);
        _windowScreenshot.Width = previewWidth;
        _windowScreenshot.Height = previewHeight;

        double scale = _ownerWindow.RenderScaling;
        if (scale <= 0)
            scale = 1;
        int widthPx = (int)Math.Clamp(Math.Round(previewWidth * scale), 1, 8192);
        int heightPx = (int)Math.Clamp(Math.Round(previewHeight * scale), 1, 8192);
        Volatile.Write(ref _targetScreenshotWidthPx, widthPx);
        Volatile.Write(ref _targetScreenshotHeightPx, heightPx);
    }

    public void Stop(string windowId)
    {
        if (_isClosing)
            return;

        _isClosing = true;
        _previewFrameProvider.ForgetWindow(windowId);
        _cts.Cancel();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && _thumbnailHandle != IntPtr.Zero)
        {
            DwmFunctions.DwmUnregisterThumbnail(_thumbnailHandle);
            _thumbnailHandle = IntPtr.Zero;
        }

        _windowScreenshot.Source = null;
        _currentScreenshot?.Dispose();
        _previousScreenshot?.Dispose();
        _currentScreenshot = null;
        _previousScreenshot = null;
    }

    private async Task RunPeriodicTask(CancellationToken cancellationToken)
    {
        var configAccessor = ConfigFileAccessor.GetInstance();
        if (!configAccessor.ReadConfig(config => config.ActivateWindowsPreview))
            return;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            RegisterWindowThumbnail();
            return;
        }

        await Task.Delay(Random.Shared.Next(0, 400), cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                bool isPipeWireProvider = _previewFrameProvider is PipeWireFrameProvider;
                int refreshIntervalMs = isPipeWireProvider
                    ? PreviewRefreshIntervalMsPipeWire
                    : PreviewRefreshIntervalMsScreenshot;
                int requestTimeoutMs = isPipeWireProvider
                    ? 500
                    : PreviewRequestTimeoutMsScreenshot;

                await UpdateScreenshot(requestTimeoutMs, cancellationToken);
                await Task.Delay(refreshIntervalMs, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Shutdown path.
            }
            catch (Exception ex)
            {
                if (ConfigFileAccessor.GetInstance().ReadConfig(config => config.ActivateLogs))
                    AppLogger.Log($"Preview screenshot loop failed: {ex.Message}", StaticData.LogSeverity.WARN);

                await Task.Delay(500, cancellationToken);
            }
        }
    }

    private async Task UpdateScreenshot(int requestTimeoutMs, CancellationToken cancellationToken)
    {
        if (_isClosing || cancellationToken.IsCancellationRequested)
            return;

        bool lockTaken = false;
        Bitmap? appScreenshot = null;
        try
        {
            await _previewUpdateSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            lockTaken = true;

            if (_isClosing || cancellationToken.IsCancellationRequested)
                return;

            int widthPx = Volatile.Read(ref _targetScreenshotWidthPx);
            int heightPx = Volatile.Read(ref _targetScreenshotHeightPx);
            var request = new ScreenshotRequest(
                MaxWidthPx: widthPx > 0 ? widthPx : null,
                MaxHeightPx: heightPx > 0 ? heightPx : null,
                TimeoutMs: Math.Clamp(requestTimeoutMs, 100, 10_000));

            appScreenshot = await _previewFrameProvider
                .RequestAsync(_windowConfig.WindowId, request, cancellationToken)
                .ConfigureAwait(false);

            if (appScreenshot is null || _isClosing || cancellationToken.IsCancellationRequested)
            {
                appScreenshot?.Dispose();
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_isClosing || cancellationToken.IsCancellationRequested)
                {
                    appScreenshot.Dispose();
                    return;
                }

                Bitmap? disposeNow = _previousScreenshot;
                _previousScreenshot = _currentScreenshot;
                _currentScreenshot = appScreenshot;
                _windowScreenshot.Source = appScreenshot;
                disposeNow?.Dispose();
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            appScreenshot?.Dispose();
        }
        finally
        {
            if (lockTaken)
                _previewUpdateSemaphore.Release();
        }
    }

    private void RegisterWindowThumbnail()
    {
        if (_thumbnailHandle != IntPtr.Zero)
        {
            DwmFunctions.DwmUnregisterThumbnail(_thumbnailHandle);
            _thumbnailHandle = IntPtr.Zero;
        }

        IPlatformHandle? platformHandle = _ownerWindow.TryGetPlatformHandle();
        if (platformHandle is null)
            return;
        if (!long.TryParse(_windowConfig.WindowId, out long srcHandleLong))
            return;

        IntPtr windowHandle = platformHandle.Handle;
        IntPtr srcHandle = new(srcHandleLong);
        int res = DwmFunctions.DwmRegisterThumbnail(windowHandle, srcHandle, out IntPtr thumbnail);
        if (res != 0)
            return;

        _thumbnailHandle = thumbnail;

        DwmFunctions.DwmQueryThumbnailSourceSize(thumbnail, out DwmFunctions.PSIZE _);
        double scale = _ownerWindow.Screens.Primary?.Scaling ?? 1;
        int inset = (int)Math.Round(PreviewBorderThickness * scale);
        DwmFunctions.Rect dest = new()
        {
            Left = inset,
            Top = (int)(TitleReservedHeight * scale) + inset,
            Right = (int)(_windowConfig.WindowWidth * scale) - inset,
            Bottom = (int)(_windowConfig.WindowHeight * scale) - inset,
        };

        DwmFunctions.DWM_THUMBNAIL_PROPERTIES props = new();
        props.dwFlags =
            DwmFunctions.DWM_TNP_SOURCECLIENTAREAONLY |
            DwmFunctions.DWM_TNP_VISIBLE |
            DwmFunctions.DWM_TNP_OPACITY |
            DwmFunctions.DWM_TNP_RECTDESTINATION;
        props.fSourceClientAreaOnly = false;
        props.fVisible = true;
        props.opacity = 255;
        props.rcDestination = dest;

        DwmFunctions.DwmUpdateThumbnailProperties(thumbnail, ref props);
    }

    private double RoundToPixel(double value)
    {
        double scale = _ownerWindow.RenderScaling;
        if (scale <= 0)
            scale = 1;
        return Math.Round(value * scale) / scale;
    }
}
