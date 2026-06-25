using Tmds.DBus;
using WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Pipewire;

public sealed partial class PipeWireFrameProvider
{
    private sealed class WindowCaptureContext(
        string windowId,
        string nodeId,
        string? portalSessionPath,
        string? portalSessionDestination,
        PipeWireWindowStream stream,
        int? targetWidthPx,
        int? targetHeightPx
    )
    {
        public string WindowId { get; } = windowId;
        public string NodeId { get; } = nodeId;
        public string? PortalSessionPath { get; } = portalSessionPath;
        public string? PortalSessionDestination { get; } = portalSessionDestination;
        public PipeWireWindowStream Stream { get; } = stream;
        private readonly object _syncRoot = new();
        public int? TargetWidthPx { get; private set; } = targetWidthPx;
        public int? TargetHeightPx { get; private set; } = targetHeightPx;
        public int ConsecutiveFailures { get; set; }
        public int ConsecutiveNoFrameTimeouts { get; set; }
        public bool IsDisposed { get; private set; }

        public void ApplyRequest(ScreenshotRequest request)
        {
            int? requestWidth = NormalizeTargetDimension(request.MaxWidthPx);
            int? requestHeight = NormalizeTargetDimension(request.MaxHeightPx);
            bool updated = false;
            lock (_syncRoot)
            {
                if (TargetWidthPx == requestWidth && TargetHeightPx == requestHeight)
                    return;

                TargetWidthPx = requestWidth;
                TargetHeightPx = requestHeight;
                updated = true;
            }

            if (updated)
                Stream.UpdateTargetDimensions(requestWidth, requestHeight);
        }

        public void Dispose(Action<string, string?> closePortalSession)
        {
            if (IsDisposed)
                return;

            IsDisposed = true;
            Stream.Dispose();
            if (!string.IsNullOrWhiteSpace(PortalSessionPath))
                closePortalSession(PortalSessionPath, PortalSessionDestination);
        }

        public void Suspend()
        {
            if (IsDisposed)
                return;

            Stream.Suspend();
        }
    }

    private sealed class PipeWireWindowStream : IDisposable
    {
        private const int MaxFrameBytes = 16 * 1024 * 1024;
        private const int DefaultRawFrameWidthPx = 640;
        private const int DefaultRawFrameHeightPx = 360;

        private readonly string _nodeId;
        private readonly IGstLaunchWrapper _gstLaunch;
        private readonly CloseSafeHandle? _pipeWireRemoteHandle;
        private readonly object _syncRoot = new();

        private IPipeWireRawBgraStream? _stream;
        private bool _hasReceivedFrame;
        private bool _disposed;
        private bool _faulted;
        private bool _restartInProgress;
        private bool _restartRequested;
        private int _rawFrameWidthPx;
        private int _rawFrameHeightPx;

        public PipeWireWindowStream(
            string nodeId,
            IGstLaunchWrapper gstLaunch,
            CloseSafeHandle? pipeWireRemoteHandle,
            int? maxWidthPx,
            int? maxHeightPx
        )
        {
            ArgumentNullException.ThrowIfNull(gstLaunch);
            _nodeId = nodeId;
            _gstLaunch = gstLaunch;
            _pipeWireRemoteHandle = pipeWireRemoteHandle;
            int? normalizedMaxWidthPx = NormalizeTargetDimension(maxWidthPx);
            int? normalizedMaxHeightPx = NormalizeTargetDimension(maxHeightPx);
            _rawFrameWidthPx = normalizedMaxWidthPx ?? DefaultRawFrameWidthPx;
            _rawFrameHeightPx = normalizedMaxHeightPx ?? DefaultRawFrameHeightPx;
        }

        public bool NeedsRestart()
        {
            lock (_syncRoot)
            {
                if (_disposed)
                    return false;

                return _faulted || _stream is null || _stream.IsFaulted;
            }
        }

        public bool HasReceivedFrame()
        {
            lock (_syncRoot)
            {
                return _hasReceivedFrame;
            }
        }

        public void Restart()
        {
            lock (_syncRoot)
            {
                if (_disposed || _restartInProgress)
                {
                    if (_restartInProgress)
                        _restartRequested = true;
                    return;
                }

                _restartInProgress = true;
            }

            Stop();

            int rawFrameWidthPx;
            int rawFrameHeightPx;
            lock (_syncRoot)
            {
                if (_disposed)
                {
                    _restartInProgress = false;
                    return;
                }

                rawFrameWidthPx = _rawFrameWidthPx;
                rawFrameHeightPx = _rawFrameHeightPx;
            }

            IPipeWireRawBgraStream? stream = null;
            if (
                rawFrameWidthPx > 0
                && rawFrameHeightPx > 0
                && TryComputeRawFrameByteCount(rawFrameWidthPx, rawFrameHeightPx, out int rawBytes)
                && rawBytes <= MaxFrameBytes
            )
            {
                stream = _gstLaunch.StartPipeWireRawBgraStream(
                    _nodeId,
                    rawFrameWidthPx,
                    rawFrameHeightPx,
                    GetPipeWireRemoteFd()
                );
            }

            if (stream is null)
            {
                lock (_syncRoot)
                {
                    _faulted = true;
                    _restartInProgress = false;
                }

                return;
            }

            bool restartRequested;
            lock (_syncRoot)
            {
                _faulted = false;
                _hasReceivedFrame = false;
                _stream = stream;
                restartRequested = _restartRequested;
                _restartRequested = false;
                _restartInProgress = false;
            }

            if (restartRequested)
                _ = Task.Run(Restart);
        }

        public void UpdateTargetDimensions(int? maxWidthPx, int? maxHeightPx)
        {
            int normalizedWidthPx = NormalizeTargetDimension(maxWidthPx) ?? DefaultRawFrameWidthPx;
            int normalizedHeightPx =
                NormalizeTargetDimension(maxHeightPx) ?? DefaultRawFrameHeightPx;

            bool shouldRestart = false;
            lock (_syncRoot)
            {
                if (_disposed)
                    return;

                if (
                    _rawFrameWidthPx == normalizedWidthPx
                    && _rawFrameHeightPx == normalizedHeightPx
                )
                {
                    return;
                }

                _rawFrameWidthPx = normalizedWidthPx;
                _rawFrameHeightPx = normalizedHeightPx;

                if (_restartInProgress)
                {
                    _restartRequested = true;
                    return;
                }

                shouldRestart = _stream is not null || _faulted;
            }

            if (shouldRestart)
                Restart();
        }

        public void Suspend()
        {
            if (_disposed)
                return;

            Stop();
        }

        public NativeBgraPreviewFrame? PullNativeFrame(
            int timeoutMs,
            CancellationToken cancellationToken
        )
        {
            EnsureRunning();

            IPipeWireRawBgraStream? stream;
            lock (_syncRoot)
            {
                if (_disposed || _faulted)
                    return null;

                stream = _stream;
            }

            if (stream is null)
                return null;

            PipeWireRawBgraFrame? rawFrame = stream.PullFrame(timeoutMs, cancellationToken);
            if (rawFrame is null)
            {
                if (stream.IsFaulted)
                    MarkFaulted();
                return null;
            }

            if (
                rawFrame.Data == IntPtr.Zero
                || rawFrame.WidthPx <= 0
                || rawFrame.HeightPx <= 0
                || rawFrame.Length <= 0
                || rawFrame.Length > MaxFrameBytes
            )
            {
                rawFrame.Dispose();
                return null;
            }

            lock (_syncRoot)
                _hasReceivedFrame = true;

            return new NativeBgraPreviewFrame(
                rawFrame.Data,
                rawFrame.Length,
                rawFrame.WidthPx,
                rawFrame.HeightPx,
                rawFrame.Dispose
            );
        }

        private void EnsureRunning()
        {
            lock (_syncRoot)
            {
                if (_disposed)
                    return;

                if (_stream is not null && !_stream.IsFaulted && !_faulted)
                    return;
            }

            Restart();
        }

        private static bool TryComputeRawFrameByteCount(
            int frameWidthPx,
            int frameHeightPx,
            out int frameByteCount
        )
        {
            frameByteCount = 0;
            if (frameWidthPx <= 0 || frameHeightPx <= 0)
                return false;

            try
            {
                int stride = checked(frameWidthPx * 4);
                frameByteCount = checked(stride * frameHeightPx);
                return frameByteCount > 0;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private void MarkFaulted()
        {
            lock (_syncRoot)
                _faulted = true;
        }

        private void Stop()
        {
            IPipeWireRawBgraStream? stream;
            lock (_syncRoot)
            {
                stream = _stream;
                _stream = null;
                _hasReceivedFrame = false;
                _faulted = true;
            }

            stream?.Dispose();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Stop();
            _pipeWireRemoteHandle?.Dispose();
        }

        private int? GetPipeWireRemoteFd()
        {
            if (
                _pipeWireRemoteHandle is null
                || _pipeWireRemoteHandle.IsClosed
                || _pipeWireRemoteHandle.IsInvalid
            )
                return null;

            long value = _pipeWireRemoteHandle.DangerousGetHandle().ToInt64();
            return value is >= 0 and <= int.MaxValue ? (int)value : null;
        }
    }
}
