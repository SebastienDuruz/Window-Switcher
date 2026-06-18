using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using WindowSwitcher.Lib.Data;
using WindowSwitcher.Lib.Data.Platform.SystemInfo.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcher.Lib.Models;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace WindowSwitcher.Windows.Services;

internal sealed class FloatingWindowService
{
    private const double TitleReservedHeight = 12;
    private const double PreviewBorderThickness = 2;
    private const int DefaultPreviewRefreshIntervalMs = 100;
    private const int PreviewRequestTimeoutMs = 1_500;
    private const int StreamPreviewRequestTimeoutMs = 1_500;
    private const int ResizePreviewRefreshDebounceMs = 250;

    private readonly Window _ownerWindow;
    private readonly WindowConfig _windowConfig;
    private readonly IPreviewFrameProvider _previewFrameProvider;
    private readonly IStreamingPreviewFrameProvider? _streamingPreviewFrameProvider;
    private readonly IFloatingPreviewPolicy _floatingPreviewPolicy;
    private readonly INativeThumbnailRenderer _nativeThumbnailRenderer;
    private readonly Image _windowScreenshot;
    private readonly Border _previewBorder;
    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _previewOperationCancellationSync = new();
    private readonly SemaphoreSlim _previewUpdateSemaphore = new(1, 1);
    private CancellationTokenSource? _previewOperationCancellation = new();
    private Bitmap? _currentScreenshot;
    private Bitmap? _previousScreenshot;
    private nint _thumbnailHandle;
    private volatile bool _isClosing;
    private bool _previewCaptureSuspendedWhileDisabled;
    private bool _isPreviewSurfaceCleared = true;
    private int _targetScreenshotWidthPx;
    private int _targetScreenshotHeightPx;
    private readonly Lock _streamFrameSync = new();
    private readonly Lock _resizePreviewRefreshSync = new();
    private Bitmap? _pendingStreamFrame;
    private int _streamFrameDrainScheduled;
    private CancellationTokenSource? _resizePreviewRefreshCancellation;
    private bool _resizePreviewRefreshPending;
    private long _lastPreviewLoopFailureLogTicks;

    public FloatingWindowService(
        Window ownerWindow,
        WindowConfig windowConfig,
        IPreviewFrameProvider previewFrameProvider,
        IFloatingPreviewPolicy floatingPreviewPolicy,
        INativeThumbnailRenderer nativeThumbnailRenderer,
        Image windowScreenshot,
        Border previewBorder
    )
    {
        ArgumentNullException.ThrowIfNull(ownerWindow);
        ArgumentNullException.ThrowIfNull(windowConfig);
        ArgumentNullException.ThrowIfNull(previewFrameProvider);
        ArgumentNullException.ThrowIfNull(floatingPreviewPolicy);
        ArgumentNullException.ThrowIfNull(nativeThumbnailRenderer);
        ArgumentNullException.ThrowIfNull(windowScreenshot);
        ArgumentNullException.ThrowIfNull(previewBorder);

        _ownerWindow = ownerWindow;
        _windowConfig = windowConfig;
        _previewFrameProvider = previewFrameProvider;
        _streamingPreviewFrameProvider = previewFrameProvider as IStreamingPreviewFrameProvider;
        _floatingPreviewPolicy = floatingPreviewPolicy;
        _nativeThumbnailRenderer = nativeThumbnailRenderer;
        _windowScreenshot = windowScreenshot;
        _previewBorder = previewBorder;
        _previewCaptureSuspendedWhileDisabled = !IsPreviewCaptureEnabled();
    }

    public void Start()
    {
        _ = Task.Run(() => RunPreviewLoopAsync(_cts.Token));
    }

    public void OnWindowResized()
    {
        UpdateLayout();

        if (_streamingPreviewFrameProvider is not null)
            ScheduleResizePreviewRefresh();

        if (_floatingPreviewPolicy.UseNativeThumbnailPreview && IsPreviewCaptureEnabled())
            RegisterWindowThumbnail();
    }

    public void OnPointerReleased()
    {
        FlushPendingResizePreviewRefresh();
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
        int widthPx = (int)Math.Round(previewWidth * scale);
        int heightPx = (int)Math.Round(previewHeight * scale);
        Volatile.Write(ref _targetScreenshotWidthPx, widthPx);
        Volatile.Write(ref _targetScreenshotHeightPx, heightPx);
    }

    public void Stop(string windowId)
    {
        if (_isClosing)
            return;

        _isClosing = true;
        CancelPendingResizePreviewRefresh(executeRefresh: false);
        _previewFrameProvider.ForgetWindow(windowId);
        _cts.Cancel();
        CancelPreviewOperations(recreateTokenSource: false);
        DisposePendingStreamFrame();

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

        CancelPendingResizePreviewRefresh(executeRefresh: false);
        UpdateLayout();
        CancelPreviewOperations(recreateTokenSource: true);
        if (!IsPreviewCaptureEnabled())
        {
            if (!_previewCaptureSuspendedWhileDisabled)
            {
                _previewFrameProvider.SuspendWindow(_windowConfig.WindowId);
                _previewCaptureSuspendedWhileDisabled = true;
            }

            if (_floatingPreviewPolicy.UseNativeThumbnailPreview)
                UnregisterWindowThumbnail();
            _ = ClearPreviewSurfaceAsync(_cts.Token);
            return;
        }

        _previewCaptureSuspendedWhileDisabled = false;
        if (_floatingPreviewPolicy.UseNativeThumbnailPreview)
            RegisterWindowThumbnail();
    }

    private void ScheduleResizePreviewRefresh()
    {
        CancellationTokenSource refreshCancellation = new();
        CancellationTokenSource? previousCancellation;
        lock (_resizePreviewRefreshSync)
        {
            previousCancellation = _resizePreviewRefreshCancellation;
            _resizePreviewRefreshCancellation = refreshCancellation;
            _resizePreviewRefreshPending = true;
        }

        previousCancellation?.Cancel();

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(
                    ResizePreviewRefreshDebounceMs,
                    refreshCancellation.Token
                ).ConfigureAwait(false);

                if (TryConsumePendingResizePreviewRefresh(refreshCancellation))
                    CancelPreviewOperations(recreateTokenSource: true);
            }
            catch (OperationCanceledException)
            {
                // Resize is still in progress or the window is shutting down.
            }
            finally
            {
                refreshCancellation.Dispose();
            }
        });
    }

    private void FlushPendingResizePreviewRefresh()
    {
        CancellationTokenSource? pendingCancellation;
        bool shouldRefresh = false;
        lock (_resizePreviewRefreshSync)
        {
            pendingCancellation = _resizePreviewRefreshCancellation;
            _resizePreviewRefreshCancellation = null;
            if (_resizePreviewRefreshPending)
            {
                _resizePreviewRefreshPending = false;
                shouldRefresh = true;
            }
        }

        pendingCancellation?.Cancel();

        if (shouldRefresh && !_isClosing)
            CancelPreviewOperations(recreateTokenSource: true);
    }

    private void CancelPendingResizePreviewRefresh(bool executeRefresh)
    {
        CancellationTokenSource? pendingCancellation;
        bool shouldRefresh = false;
        lock (_resizePreviewRefreshSync)
        {
            pendingCancellation = _resizePreviewRefreshCancellation;
            _resizePreviewRefreshCancellation = null;
            if (_resizePreviewRefreshPending)
            {
                _resizePreviewRefreshPending = false;
                shouldRefresh = true;
            }
        }

        pendingCancellation?.Cancel();

        if (executeRefresh && shouldRefresh && !_isClosing)
            CancelPreviewOperations(recreateTokenSource: true);
    }

    private bool TryConsumePendingResizePreviewRefresh(CancellationTokenSource refreshCancellation)
    {
        lock (_resizePreviewRefreshSync)
        {
            if (!ReferenceEquals(_resizePreviewRefreshCancellation, refreshCancellation))
                return false;

            _resizePreviewRefreshCancellation = null;
            if (!_resizePreviewRefreshPending || _isClosing)
                return false;

            _resizePreviewRefreshPending = false;
            return true;
        }
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
                await Task.Delay(DefaultPreviewRefreshIntervalMs, operationToken)
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
            catch (Exception ex)
            {
                LogPreviewLoopFailure("screenshot_polling", ex);
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
                
                int widthPx = Volatile.Read(ref _targetScreenshotWidthPx);
                int heightPx = Volatile.Read(ref _targetScreenshotHeightPx);
                var request = new ScreenshotRequest(
                    MaxWidthPx: widthPx > 0 ? widthPx : null,
                    MaxHeightPx: heightPx > 0 ? heightPx : null,
                    TimeoutMs: StreamPreviewRequestTimeoutMs
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

                    QueueLatestStreamFrame(frame, operationToken);
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
            catch (Exception ex)
            {
                LogPreviewLoopFailure("continuous_stream", ex);
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void LogPreviewLoopFailure(string mode, Exception exception)
    {
        long nowTicks = DateTimeOffset.UtcNow.Ticks;
        long previousTicks = Interlocked.Read(ref _lastPreviewLoopFailureLogTicks);
        if (
            previousTicks != 0
            && nowTicks - previousTicks < TimeSpan.FromSeconds(30).Ticks
        )
            return;

        if (
            Interlocked.CompareExchange(
                ref _lastPreviewLoopFailureLogTicks,
                nowTicks,
                previousTicks
            ) != previousTicks
        )
            return;

        Trace.TraceWarning(
            $"[Preview] Preview loop failed; retrying. Mode={mode}; ExceptionType={exception.GetType().FullName}"
        );
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
                TimeoutMs: requestTimeoutMs
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
            .ReadConfig(config => config.EnablePreviews);
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
            _previewCaptureSuspendedWhileDisabled = false;
            return true;
        }

        DisposePendingStreamFrame();
        if (!_previewCaptureSuspendedWhileDisabled)
        {
            _previewFrameProvider.SuspendWindow(_windowConfig.WindowId);
            _previewCaptureSuspendedWhileDisabled = true;
        }

        await ClearPreviewSurfaceAsync(cancellationToken).ConfigureAwait(false);
        await Task.Delay(DefaultPreviewRefreshIntervalMs, cancellationToken).ConfigureAwait(false);
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

    private void QueueLatestStreamFrame(Bitmap frame, CancellationToken cancellationToken)
    {
        Bitmap? disposeNow = null;
        bool scheduleDrain = false;

        lock (_streamFrameSync)
        {
            if (_isClosing || cancellationToken.IsCancellationRequested || !IsPreviewCaptureEnabled())
            {
                disposeNow = frame;
            }
            else
            {
                // Last-frame-wins: replace any older frame still waiting for UI.
                disposeNow = _pendingStreamFrame;
                _pendingStreamFrame = frame;
                scheduleDrain =
                    Interlocked.CompareExchange(ref _streamFrameDrainScheduled, 1, 0) == 0;
            }
        }

        disposeNow?.Dispose();

        if (scheduleDrain)
            _ = ProcessQueuedStreamFramesAsync(cancellationToken);
    }

    private async Task ProcessQueuedStreamFramesAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Bitmap? nextFrame;
                lock (_streamFrameSync)
                {
                    nextFrame = _pendingStreamFrame;
                    _pendingStreamFrame = null;
                }

                if (nextFrame is null)
                    break;

                await ApplyStreamFrameAsync(nextFrame, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown or preview settings changed while processing queued stream frames.
        }
        finally
        {
            Interlocked.Exchange(ref _streamFrameDrainScheduled, 0);

            bool shouldScheduleDrain = false;
            if (_isClosing || cancellationToken.IsCancellationRequested)
            {
                DisposePendingStreamFrame();
            }
            else
            {
                bool hasPendingFrame;
                lock (_streamFrameSync)
                    hasPendingFrame = _pendingStreamFrame is not null;

                shouldScheduleDrain =
                    hasPendingFrame
                    && Interlocked.CompareExchange(ref _streamFrameDrainScheduled, 1, 0) == 0;
            }

            if (shouldScheduleDrain)
            {
                _ = ProcessQueuedStreamFramesAsync(cancellationToken);
            }
        }
    }

    private async Task ApplyStreamFrameAsync(Bitmap frame, CancellationToken cancellationToken)
    {
        bool lockTaken = false;
        bool frameTransferred = false;
        try
        {
            await _previewUpdateSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            lockTaken = true;

            if (_isClosing || cancellationToken.IsCancellationRequested || !IsPreviewCaptureEnabled())
            {
                frame.Dispose();
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_isClosing || cancellationToken.IsCancellationRequested || !IsPreviewCaptureEnabled())
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
    }

    private void DisposePendingStreamFrame()
    {
        Bitmap? pendingFrame;
        lock (_streamFrameSync)
        {
            pendingFrame = _pendingStreamFrame;
            _pendingStreamFrame = null;
        }

        pendingFrame?.Dispose();
    }

    private void RegisterWindowThumbnail()
    {
        if (_thumbnailHandle != 0)
        {
            _nativeThumbnailRenderer.Unregister(_thumbnailHandle);
            _thumbnailHandle = 0;
        }

        IPlatformHandle? platformHandle = _ownerWindow.TryGetPlatformHandle();
        if (platformHandle is null)
            return;
        if (!long.TryParse(_windowConfig.WindowId, out long srcHandleLong))
            return;

        double scale = _ownerWindow.Screens.Primary?.Scaling ?? 1;
        int inset = (int)Math.Round(PreviewBorderThickness * scale);
        var destinationBounds = new NativeThumbnailBounds(
            Left: inset,
            Top: (int)(TitleReservedHeight * scale) + inset,
            Right: (int)(_windowConfig.WindowWidth * scale) - inset,
            Bottom: (int)(_windowConfig.WindowHeight * scale) - inset
        );

        if (
            _nativeThumbnailRenderer.TryRegister(
                platformHandle.Handle,
                new IntPtr(srcHandleLong),
                destinationBounds,
                out nint thumbnailHandle
            )
        )
        {
            _thumbnailHandle = thumbnailHandle;
        }
    }

    private void UnregisterWindowThumbnail()
    {
        if (_thumbnailHandle == 0)
            return;

        _nativeThumbnailRenderer.Unregister(_thumbnailHandle);
        _thumbnailHandle = 0;
    }

    private double RoundToPixel(double value)
    {
        double scale = _ownerWindow.RenderScaling;
        if (scale <= 0)
            scale = 1;
        return Math.Round(value * scale) / scale;
    }
}
