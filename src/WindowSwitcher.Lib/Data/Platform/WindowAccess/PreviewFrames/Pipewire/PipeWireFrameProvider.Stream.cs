using System.Buffers;
using System.Diagnostics;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Tmds.DBus;
using WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;
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

        public enum FrameFormat
        {
            Bgra32 = 0,
        }

        public readonly record struct FrameSnapshot(
            long Sequence,
            byte[] Bytes,
            FrameFormat Format,
            int WidthPx,
            int HeightPx,
            FrameBufferLease? Lease
        );

        public sealed class FrameBufferLease(ArrayPool<byte> pool, byte[] buffer)
        {
            private byte[]? _buffer = buffer;
            private int _refCount = 1;

            public byte[] Buffer => _buffer ?? Array.Empty<byte>();

            public void AddRef()
            {
                _ = Interlocked.Increment(ref _refCount);
            }

            public void Release()
            {
                if (Interlocked.Decrement(ref _refCount) != 0)
                    return;

                byte[]? released = Interlocked.Exchange(ref _buffer, null);
                if (released is null)
                    return;

                pool.Return(released);
            }
        }

        private readonly string _nodeId;
        private readonly IGstLaunchWrapper _gstLaunch;
        private readonly CloseSafeHandle? _pipeWireRemoteHandle;
        private readonly int _minFrameIntervalMs;
        private readonly object _syncRoot = new();
        private readonly SemaphoreSlim _frameReadySignal = new(initialCount: 0, maxCount: 1);
        private readonly ArrayPool<byte> _framePool = ArrayPool<byte>.Shared;

        private Process? _process;
        private CancellationTokenSource? _cts;
        private Task? _readerTask;
        private byte[]? _latestFrameBytes;
        private FrameBufferLease? _latestFrameLease;
        private FrameFormat _latestFrameFormat;
        private int _latestFrameWidthPx;
        private int _latestFrameHeightPx;
        private long _latestFrameSequence;
        private long _activeGeneration;
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
            int minFrameIntervalMs,
            int? maxWidthPx,
            int? maxHeightPx
        )
        {
            ArgumentNullException.ThrowIfNull(gstLaunch);
            _nodeId = nodeId;
            _gstLaunch = gstLaunch;
            _pipeWireRemoteHandle = pipeWireRemoteHandle;
            _minFrameIntervalMs = minFrameIntervalMs;
            int? normalizedMaxWidthPx = NormalizeTargetDimension(maxWidthPx);
            int? normalizedMaxHeightPx = NormalizeTargetDimension(maxHeightPx);
            _rawFrameWidthPx = normalizedMaxWidthPx ?? DefaultRawFrameWidthPx;
            _rawFrameHeightPx = normalizedMaxHeightPx ?? DefaultRawFrameHeightPx;
        }

        public void EnsureRunning()
        {
            lock (_syncRoot)
            {
                if (_disposed)
                    return;

                if (_process is not null && !_process.HasExited && !_faulted)
                    return;
            }

            Restart();
        }

        public bool NeedsRestart()
        {
            lock (_syncRoot)
            {
                if (_disposed)
                    return false;

                return _faulted || _process is null || _process.HasExited;
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
                    return;
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

            int? remoteFd = GetPipeWireRemoteFd();
            Process? process = null;
            if (
                rawFrameWidthPx > 0
                && rawFrameHeightPx > 0
                && TryComputeRawFrameByteCount(rawFrameWidthPx, rawFrameHeightPx, out int rawBytes)
                && rawBytes <= MaxFrameBytes
            )
            {
                process = _gstLaunch.StartPipeWireRawBgraStream(
                    _nodeId,
                    rawFrameWidthPx,
                    rawFrameHeightPx,
                    remoteFd
                );
            }

            var cts = new CancellationTokenSource();
            if (process is null)
            {
                lock (_syncRoot)
                {
                    _faulted = true;
                    _restartInProgress = false;
                }

                cts.Dispose();
                return;
            }

            long generation;
            bool restartRequested;
            lock (_syncRoot)
            {
                _activeGeneration++;
                generation = _activeGeneration;
                _faulted = false;
                _hasReceivedFrame = false;
                _latestFrameFormat = FrameFormat.Bgra32;
                _latestFrameWidthPx = rawFrameWidthPx;
                _latestFrameHeightPx = rawFrameHeightPx;
                _process = process;
                _cts = cts;
                _readerTask = Task.Run(
                    () =>
                        ReadRawLoop(
                            process,
                            cts.Token,
                            generation,
                            rawFrameWidthPx,
                            rawFrameHeightPx
                        )
                );
                _ = Task.Run(() => DrainErrors(process, cts.Token));
                restartRequested = _restartRequested;
                _restartRequested = false;
                _restartInProgress = false;
            }

            if (restartRequested)
                _ = Task.Run(Restart);

            DrainFrameSignal();
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

                shouldRestart = _process is not null || _readerTask is not null || _faulted;
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

        public async Task<Bitmap?> GetFrameAsync(int timeoutMs, CancellationToken cancellationToken)
        {
            bool hasFiniteTimeout = timeoutMs >= 0;
            long deadlineMs = hasFiniteTimeout ? Environment.TickCount64 + timeoutMs : 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                FrameSnapshot? snapshot = GetFrameSnapshotAfter(-1);
                if (snapshot is not null)
                {
                    try
                    {
                        return CreateBitmap(snapshot.Value);
                    }
                    finally
                    {
                        ReleaseSnapshot(snapshot.Value);
                    }
                }

                if (IsFaulted())
                    return null;

                try
                {
                    if (hasFiniteTimeout)
                    {
                        long remainingMs = deadlineMs - Environment.TickCount64;
                        if (remainingMs <= 0)
                            return null;

                        bool signaled = await _frameReadySignal
                            .WaitAsync(
                                millisecondsTimeout: remainingMs > int.MaxValue
                                    ? int.MaxValue
                                    : (int)remainingMs,
                                cancellationToken
                            )
                            .ConfigureAwait(false);
                        if (!signaled)
                            return null;
                    }
                    else
                    {
                        await _frameReadySignal.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
                catch (ObjectDisposedException)
                {
                    return null;
                }
            }

            return null;
        }

        public void ReleaseSnapshot(FrameSnapshot snapshot)
        {
            snapshot.Lease?.Release();
        }

        public async Task<FrameSnapshot?> WaitForNextFrameAsync(
            long afterSequence,
            int timeoutMs,
            CancellationToken cancellationToken
        )
        {
            bool hasFiniteTimeout = timeoutMs >= 0;
            long deadlineMs = hasFiniteTimeout ? Environment.TickCount64 + timeoutMs : 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                FrameSnapshot? snapshot = GetFrameSnapshotAfter(afterSequence);
                if (snapshot is not null)
                    return snapshot;

                if (IsFaulted())
                    return null;

                try
                {
                    if (hasFiniteTimeout)
                    {
                        long remainingMs = deadlineMs - Environment.TickCount64;
                        if (remainingMs <= 0)
                            return null;

                        bool signaled = await _frameReadySignal
                            .WaitAsync(
                                millisecondsTimeout: remainingMs > int.MaxValue
                                    ? int.MaxValue
                                    : (int)remainingMs,
                                cancellationToken
                            )
                            .ConfigureAwait(false);
                        if (!signaled)
                            return null;
                    }
                    else
                    {
                        await _frameReadySignal.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
                catch (ObjectDisposedException)
                {
                    return null;
                }
            }

            return null;
        }

        private FrameSnapshot? GetFrameSnapshotAfter(long afterSequence)
        {
            lock (_syncRoot)
            {
                if (_latestFrameBytes is null || _latestFrameSequence <= afterSequence)
                    return null;

                FrameBufferLease? lease = _latestFrameLease;
                lease?.AddRef();

                return new FrameSnapshot(
                    _latestFrameSequence,
                    _latestFrameBytes,
                    _latestFrameFormat,
                    _latestFrameWidthPx,
                    _latestFrameHeightPx,
                    lease
                );
            }
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

        private static bool TryReadExact(Stream stream, byte[] buffer, int length)
        {
            int offset = 0;
            while (offset < length)
            {
                int read = stream.Read(buffer, offset, length - offset);
                if (read <= 0)
                    return false;

                offset += read;
            }

            return true;
        }

        private bool IsFaulted()
        {
            lock (_syncRoot)
            {
                return _faulted;
            }
        }

        private async Task DrainErrors(Process process, CancellationToken cancellationToken)
        {
            try
            {
                string stderr = await process
                    .StandardError.ReadToEndAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    string reduced = stderr.Length > 4_000 ? stderr[..4_000] : stderr;
                }
            }
            catch { }
        }

        private void ReadRawLoop(
            Process process,
            CancellationToken cancellationToken,
            long generation,
            int frameWidthPx,
            int frameHeightPx
        )
        {
            try
            {
                if (
                    !TryComputeRawFrameByteCount(
                        frameWidthPx,
                        frameHeightPx,
                        out int frameByteCount
                    )
                    || frameByteCount > MaxFrameBytes
                )
                {
                    return;
                }

                byte[] readBuffer = new byte[frameByteCount];
                Stream output = process.StandardOutput.BaseStream;
                long nextAcceptedFrameAtMs = 0;

                while (!cancellationToken.IsCancellationRequested)
                {
                    if (!TryReadExact(output, readBuffer, frameByteCount))
                        break;

                    long now = Environment.TickCount64;
                    if (_minFrameIntervalMs > 0 && now < nextAcceptedFrameAtMs)
                        continue;

                    byte[] frame = _framePool.Rent(frameByteCount);
                    try
                    {
                        Buffer.BlockCopy(readBuffer, 0, frame, 0, frameByteCount);
                        var lease = new FrameBufferLease(_framePool, frame);
                        FrameBufferLease? previousLease;

                        lock (_syncRoot)
                        {
                            previousLease = _latestFrameLease;
                            _latestFrameLease = lease;
                            _latestFrameBytes = frame;
                            _latestFrameFormat = FrameFormat.Bgra32;
                            _latestFrameWidthPx = frameWidthPx;
                            _latestFrameHeightPx = frameHeightPx;
                            _latestFrameSequence++;
                            _hasReceivedFrame = true;
                        }

                        previousLease?.Release();
                    }
                    catch
                    {
                        _framePool.Return(frame);
                        throw;
                    }

                    SignalFrameReady();

                    if (_minFrameIntervalMs > 0)
                        nextAcceptedFrameAtMs = now + _minFrameIntervalMs;
                }
            }
            catch (Exception) { }
            finally
            {
                lock (_syncRoot)
                {
                    if (_activeGeneration == generation)
                        _faulted = true;
                }
                SignalFrameReady();
            }
        }

        private void Stop()
        {
            Process? process;
            CancellationTokenSource? cts;
            FrameBufferLease? latestLease;

            lock (_syncRoot)
            {
                _activeGeneration++;
                process = _process;
                cts = _cts;
                latestLease = _latestFrameLease;
                _process = null;
                _cts = null;
                _readerTask = null;
                _latestFrameBytes = null;
                _latestFrameLease = null;
                _latestFrameFormat = FrameFormat.Bgra32;
                _latestFrameWidthPx = 0;
                _latestFrameHeightPx = 0;
                _hasReceivedFrame = false;
                _faulted = true;
            }

            latestLease?.Release();

            SignalFrameReady();
            DrainFrameSignal();

            if (cts is not null)
            {
                try
                {
                    cts.Cancel();
                }
                catch { }
                cts.Dispose();
            }

            if (process is not null)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch { }
                process.Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Stop();
            _pipeWireRemoteHandle?.Dispose();
            _frameReadySignal.Dispose();
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

        private void SignalFrameReady()
        {
            try
            {
                if (_frameReadySignal.CurrentCount == 0)
                    _frameReadySignal.Release();
            }
            catch
            {
                // Dispose/shutdown path.
            }
        }

        private void DrainFrameSignal()
        {
            try
            {
                while (_frameReadySignal.Wait(0)) { }
            }
            catch
            {
                // Dispose/shutdown path.
            }
        }
    }
}
