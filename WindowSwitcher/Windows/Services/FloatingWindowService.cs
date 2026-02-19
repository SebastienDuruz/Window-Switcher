using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using WindowSwitcherLib.Data;
using WindowSwitcherLib.Data.Platform.Interop;
using WindowSwitcherLib.Data.Platform.SystemInfo.Abstractions;
using WindowSwitcherLib.Data.Platform.WindowAccess.PreviewFrames;
using WindowSwitcherLib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcherLib.Models;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace WindowSwitcher.Windows.Services;

internal sealed class FloatingWindowService
{
    private const double TitleReservedHeight = 12;
    private const double PreviewBorderThickness = 2;
    private const int DefaultPreviewRefreshIntervalMs = 100;
    private const int PreviewRequestTimeoutMs = 1_500;

    private readonly Window _ownerWindow;
    private readonly WindowConfig _windowConfig;
    private readonly IPreviewFrameProvider _previewFrameProvider;
    private readonly IStreamingPreviewFrameProvider? _streamingPreviewFrameProvider;
    private readonly IFloatingPreviewPolicy _floatingPreviewPolicy;
    private readonly Image _windowScreenshot;
    private readonly Border _previewBorder;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _previewOperationCancellationSync = new();
    private readonly SemaphoreSlim _previewUpdateSemaphore = new(1, 1);
    private CancellationTokenSource? _previewOperationCancellation = new();
    private Bitmap? _currentScreenshot;
    private Bitmap? _previousScreenshot;
    private IntPtr _thumbnailHandle = IntPtr.Zero;
    private volatile bool _isClosing;
    private bool _previewCaptureForgottenWhileDisabled;
    private bool _isPreviewSurfaceCleared = true;
    private int _targetScreenshotWidthPx;
    private int _targetScreenshotHeightPx;

    public FloatingWindowService(
        Window ownerWindow,
        WindowConfig windowConfig,
        IPreviewFrameProvider previewFrameProvider,
        IFloatingPreviewPolicy floatingPreviewPolicy,
        Image windowScreenshot,
        Border previewBorder
    )
    {
        ArgumentNullException.ThrowIfNull(ownerWindow);
        ArgumentNullException.ThrowIfNull(windowConfig);
        ArgumentNullException.ThrowIfNull(previewFrameProvider);
        ArgumentNullException.ThrowIfNull(floatingPreviewPolicy);
        ArgumentNullException.ThrowIfNull(windowScreenshot);
        ArgumentNullException.ThrowIfNull(previewBorder);

        _ownerWindow = ownerWindow;
        _windowConfig = windowConfig;
        _previewFrameProvider = previewFrameProvider;
        _streamingPreviewFrameProvider = previewFrameProvider as IStreamingPreviewFrameProvider;
        _floatingPreviewPolicy = floatingPreviewPolicy;
        _windowScreenshot = windowScreenshot;
        _previewBorder = previewBorder;
        _previewCaptureForgottenWhileDisabled = !IsPreviewCaptureEnabled();
    }

    public void Start()
    {
        _ = Task.Run(() => RunPreviewLoopAsync(_cts.Token));
    }

    public void OnWindowResized()
    {
        UpdateLayout();

        if (_floatingPreviewPolicy.UseNativeThumbnailPreview && IsPreviewCaptureEnabled())
            RegisterWindowThumbnail();
    }

    public void SetPreviewHighlight(bool isSelected)
    {
        _previewBorder.IsVisible = isSelected;
        if (
            !isSelected
            && IsPreviewCaptureEnabled()
            && _streamingPreviewFrameProvider is null
            && _floatingPreviewPolicy.RefreshScreenshotWhenDeselected
        )
        {
            _ = UpdateScreenshotForCurrentOperationAsync(PreviewRequestTimeoutMs, _cts.Token);
        }
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

        if (!_floatingPreviewPolicy.ShowScreenshotControl)
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
        CancelPreviewOperations(recreateTokenSource: false);

        if (_floatingPreviewPolicy.UseNativeThumbnailPreview)
            UnregisterWindowThumbnail();

        _windowScreenshot.Source = null;
        _currentScreenshot?.Dispose();
        _previousScreenshot?.Dispose();
        _currentScreenshot = null;
        _previousScreenshot = null;
        _isPreviewSurfaceCleared = true;
    }

    public void ApplySettings()
    {
        if (_isClosing)
            return;

        UpdateLayout();
        CancelPreviewOperations(recreateTokenSource: true);
        if (!IsPreviewCaptureEnabled())
        {
            if (!_previewCaptureForgottenWhileDisabled)
            {
                _previewFrameProvider.ForgetWindow(_windowConfig.WindowId);
                _previewCaptureForgottenWhileDisabled = true;
            }

            if (_floatingPreviewPolicy.UseNativeThumbnailPreview)
                UnregisterWindowThumbnail();
            _ = ClearPreviewSurfaceAsync(_cts.Token);
            return;
        }

        _previewCaptureForgottenWhileDisabled = false;
        _previewFrameProvider.ForgetWindow(_windowConfig.WindowId);
        if (_floatingPreviewPolicy.UseNativeThumbnailPreview)
            RegisterWindowThumbnail();
    }

    private async Task RunPreviewLoopAsync(CancellationToken cancellationToken)
    {
        if (_floatingPreviewPolicy.UseNativeThumbnailPreview)
        {
            if (IsPreviewCaptureEnabled())
                RegisterWindowThumbnail();
            return;
        }

        await Task.Delay(Random.Shared.Next(0, 400), cancellationToken);

        if (_streamingPreviewFrameProvider is not null)
        {
            await RunContinuousStreamLoopAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await RunScreenshotPollingLoopAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RunScreenshotPollingLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using CancellationTokenSource operationCts = CreatePreviewOperationTokenSource(
                cancellationToken
            );
            CancellationToken operationToken = operationCts.Token;
            try
            {
                if (!await EnsurePreviewEnabledAsync(operationToken).ConfigureAwait(false))
                    continue;

                await UpdateScreenshot(PreviewRequestTimeoutMs, operationToken)
                    .ConfigureAwait(false);
                await Task.Delay(GetPreviewRefreshIntervalMs(), operationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Shutdown path.
            }
            catch (OperationCanceledException)
            {
                // Preview settings changed while an operation was running.
            }
            catch (Exception)
            {
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task RunContinuousStreamLoopAsync(CancellationToken cancellationToken)
    {
        if (_streamingPreviewFrameProvider is null)
            return;

        while (!cancellationToken.IsCancellationRequested)
        {
            using CancellationTokenSource operationCts = CreatePreviewOperationTokenSource(
                cancellationToken
            );
            CancellationToken operationToken = operationCts.Token;

            try
            {
                if (!await EnsurePreviewEnabledAsync(operationToken).ConfigureAwait(false))
                    continue;

                int timeoutMs = Math.Clamp(PreviewRequestTimeoutMs, 100, 10_000);
                var request = new ScreenshotRequest(
                    MaxWidthPx: null,
                    MaxHeightPx: null,
                    TimeoutMs: GetStreamRequestTimeoutMs(timeoutMs)
                );

                await foreach (
                    Bitmap frame in _streamingPreviewFrameProvider.StreamAsync(
                        _windowConfig.WindowId,
                        request,
                        operationToken
                    )
                )
                {
                    if (!IsPreviewCaptureEnabled())
                    {
                        frame.Dispose();
                        break;
                    }

                    bool lockTaken = false;
                    bool frameTransferred = false;
                    try
                    {
                        await _previewUpdateSemaphore
                            .WaitAsync(cancellationToken)
                            .ConfigureAwait(false);
                        lockTaken = true;

                        if (_isClosing || cancellationToken.IsCancellationRequested)
                        {
                            frame.Dispose();
                            return;
                        }

                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            if (_isClosing || cancellationToken.IsCancellationRequested)
                            {
                                frame.Dispose();
                                return;
                            }

                            Bitmap? disposeNow = _previousScreenshot;
                            _previousScreenshot = _currentScreenshot;
                            _currentScreenshot = frame;
                            _windowScreenshot.Source = frame;
                            _isPreviewSurfaceCleared = false;
                            frameTransferred = true;
                            disposeNow?.Dispose();
                        });
                    }
                    catch
                    {
                        if (!frameTransferred)
                            frame.Dispose();
                        throw;
                    }
                    finally
                    {
                        if (lockTaken)
                            _previewUpdateSemaphore.Release();
                    }

                    if (frameTransferred)
                        await Task.Delay(GetPreviewRefreshIntervalMs(), operationToken)
                            .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Shutdown path.
            }
            catch (OperationCanceledException)
            {
                // Preview settings changed while an operation was running.
            }
            catch (Exception)
            {
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static int GetPreviewRefreshIntervalMs()
    {
        return DefaultPreviewRefreshIntervalMs;
    }

    private static int GetStreamRequestTimeoutMs(int defaultTimeoutMs)
    {
        int basedOnPreviewLoop = checked(GetPreviewRefreshIntervalMs() * 3);
        return Math.Clamp(Math.Max(defaultTimeoutMs, basedOnPreviewLoop), 100, 30_000);
    }

    private async Task UpdateScreenshot(int requestTimeoutMs, CancellationToken cancellationToken)
    {
        if (_isClosing || cancellationToken.IsCancellationRequested || !IsPreviewCaptureEnabled())
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
                TimeoutMs: Math.Clamp(requestTimeoutMs, 100, 10_000)
            );

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
                _isPreviewSurfaceCleared = false;
                disposeNow?.Dispose();
            });
        }
        catch (OperationCanceledException)
        {
            appScreenshot?.Dispose();
        }
        finally
        {
            if (lockTaken)
                _previewUpdateSemaphore.Release();
        }
    }

    private static bool IsPreviewCaptureEnabled()
    {
        return ConfigFileAccessor
            .GetInstance()
            .ReadConfig(config => !config.DisablePreviews && config.ActivateWindowsPreview);
    }

    private async Task UpdateScreenshotForCurrentOperationAsync(
        int requestTimeoutMs,
        CancellationToken cancellationToken
    )
    {
        using CancellationTokenSource operationCts = CreatePreviewOperationTokenSource(
            cancellationToken
        );
        await UpdateScreenshot(requestTimeoutMs, operationCts.Token).ConfigureAwait(false);
    }

    private CancellationTokenSource CreatePreviewOperationTokenSource(
        CancellationToken cancellationToken
    )
    {
        CancellationToken previewOperationToken;
        lock (_previewOperationCancellationSync)
            previewOperationToken = _previewOperationCancellation?.Token ?? CancellationToken.None;

        return CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            previewOperationToken
        );
    }

    private void CancelPreviewOperations(bool recreateTokenSource)
    {
        CancellationTokenSource? toCancel;
        lock (_previewOperationCancellationSync)
        {
            toCancel = _previewOperationCancellation;
            _previewOperationCancellation = recreateTokenSource
                ? new CancellationTokenSource()
                : null;
        }

        if (toCancel is null)
            return;

        try
        {
            toCancel.Cancel();
        }
        catch
        {
            // Best effort cancellation.
        }
        finally
        {
            toCancel.Dispose();
        }
    }

    private async Task<bool> EnsurePreviewEnabledAsync(CancellationToken cancellationToken)
    {
        if (IsPreviewCaptureEnabled())
        {
            _previewCaptureForgottenWhileDisabled = false;
            return true;
        }

        if (!_previewCaptureForgottenWhileDisabled)
        {
            _previewFrameProvider.ForgetWindow(_windowConfig.WindowId);
            _previewCaptureForgottenWhileDisabled = true;
        }

        await ClearPreviewSurfaceAsync(cancellationToken).ConfigureAwait(false);
        await Task.Delay(GetPreviewRefreshIntervalMs(), cancellationToken).ConfigureAwait(false);
        return false;
    }

    private async Task ClearPreviewSurfaceAsync(CancellationToken cancellationToken)
    {
        if (_isPreviewSurfaceCleared)
            return;

        bool lockTaken = false;
        try
        {
            await _previewUpdateSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            lockTaken = true;

            if (_isPreviewSurfaceCleared)
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Bitmap? current = _currentScreenshot;
                Bitmap? previous = _previousScreenshot;
                _currentScreenshot = null;
                _previousScreenshot = null;
                _windowScreenshot.Source = null;
                _isPreviewSurfaceCleared = true;
                current?.Dispose();
                previous?.Dispose();
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown path.
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
            DwmFunctions.DWM_TNP_SOURCECLIENTAREAONLY
            | DwmFunctions.DWM_TNP_VISIBLE
            | DwmFunctions.DWM_TNP_OPACITY
            | DwmFunctions.DWM_TNP_RECTDESTINATION;
        props.fSourceClientAreaOnly = false;
        props.fVisible = true;
        props.opacity = 255;
        props.rcDestination = dest;

        DwmFunctions.DwmUpdateThumbnailProperties(thumbnail, ref props);
    }

    private void UnregisterWindowThumbnail()
    {
        if (_thumbnailHandle == IntPtr.Zero)
            return;

        DwmFunctions.DwmUnregisterThumbnail(_thumbnailHandle);
        _thumbnailHandle = IntPtr.Zero;
    }

    private double RoundToPixel(double value)
    {
        double scale = _ownerWindow.RenderScaling;
        if (scale <= 0)
            scale = 1;
        return Math.Round(value * scale) / scale;
    }
}
