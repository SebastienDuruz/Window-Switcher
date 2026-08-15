using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Tmds.DBus;
using WindowSwitcher.Lib.Data.Platform.Diagnostics;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Pipewire;

internal interface IPipeWireNativeStream : IDisposable
{
    void UpdateTargetDimensions(int width, int height);
    void SetActive(bool active);
    void DisableDmaBuf();
    IAsyncEnumerable<PreviewFrame> ReadFramesAsync(CancellationToken cancellationToken);
}

internal interface IPipeWireNativeStreamFactory
{
    Task<IPipeWireNativeStream?> CreateAsync(
        CloseSafeHandle remoteHandle,
        uint pipeWireNodeId,
        int width,
        int height,
        CancellationToken cancellationToken
    );
}

internal sealed class PipeWireNativeStreamFactory(IPlatformDiagnostics? diagnostics = null)
    : IPipeWireNativeStreamFactory
{
    private readonly IPlatformDiagnostics _diagnostics =
        diagnostics ?? TracePlatformDiagnostics.Instance;

    public Task<IPipeWireNativeStream?> CreateAsync(
        CloseSafeHandle remoteHandle,
        uint pipeWireNodeId,
        int width,
        int height,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(remoteHandle);
        return PipeWireNativeStream.CreateAsync(
            remoteHandle,
            pipeWireNodeId,
            width,
            height,
            _diagnostics,
            cancellationToken
        );
    }
}

internal sealed class PipeWireNativeStream : IPipeWireNativeStream
{
    private const int BufferPoolSize = 3;
    private const int MaximumOutputFrameBytes = 16 * 1024 * 1024;
    private const int MaximumInputFrameBytes = 128 * 1024 * 1024;

    private readonly object _syncRoot = new();
    private readonly LatestFrameChannel _frames = new();
    private readonly NativeFrameBufferPool _bufferPool = new(BufferPoolSize);
    private readonly IPlatformDiagnostics _diagnostics;
    private readonly WindowSwitcherPipeWireNative.FrameCallback _frameCallback;
    private readonly WindowSwitcherPipeWireNative.StateCallback _stateCallback;
    private GCHandle _selfHandle;
    private IntPtr _nativeStream;
    private int _targetWidth;
    private int _targetHeight;
    private bool _faulted;
    private bool _disposed;
    private bool _framePublishedReported;
    private BgraScalePlan? _scalePlan;

    private PipeWireNativeStream(int width, int height, IPlatformDiagnostics diagnostics)
    {
        _targetWidth = width;
        _targetHeight = height;
        _diagnostics = diagnostics;
        _frameCallback = OnFrame;
        _stateCallback = OnStateChanged;
    }

    internal static Task<IPipeWireNativeStream?> CreateAsync(
        CloseSafeHandle remoteHandle,
        uint pipeWireNodeId,
        int width,
        int height,
        IPlatformDiagnostics diagnostics,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(remoteHandle);
        ArgumentNullException.ThrowIfNull(diagnostics);
        return Task.Run<IPipeWireNativeStream?>(
            () =>
                Create(remoteHandle, pipeWireNodeId, width, height, diagnostics, cancellationToken),
            cancellationToken
        );
    }

    private static PipeWireNativeStream? Create(
        CloseSafeHandle remoteHandle,
        uint pipeWireNodeId,
        int width,
        int height,
        IPlatformDiagnostics diagnostics,
        CancellationToken cancellationToken
    )
    {
        var created = new PipeWireNativeStream(width, height, diagnostics);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryComputeFrameLayout(width, height, MaximumOutputFrameBytes, out _))
                return null;

            int fileDescriptor = GetFileDescriptor(remoteHandle);
            if (fileDescriptor < 0)
                return null;

            created._selfHandle = GCHandle.Alloc(created, GCHandleType.Normal);
            PipeWireDmaBufFormatModifier[] dmaBufCapabilities =
                PipeWireDmaBufCapabilities.Current.ToArray();
            uint[] dmaBufFormats = dmaBufCapabilities
                .Select(capability => capability.DrmFormat)
                .ToArray();
            ulong[] dmaBufModifiers = dmaBufCapabilities
                .Select(capability => capability.Modifier)
                .ToArray();
            diagnostics.Information("initialisation du backend PipeWire natif");
            created._nativeStream = WindowSwitcherPipeWireNative.CreateStream(
                fileDescriptor,
                pipeWireNodeId,
                checked((uint)width),
                checked((uint)height),
                PreviewFrameTiming.FramesPerSecond,
                dmaBufFormats,
                dmaBufModifiers,
                checked((uint)dmaBufCapabilities.Length),
                created._frameCallback,
                created._stateCallback,
                GCHandle.ToIntPtr(created._selfHandle)
            );
            remoteHandle.Dispose();
            if (created._nativeStream == IntPtr.Zero)
                return null;
            return created;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception exception)
        {
            diagnostics.Error("native PipeWire stream initialization failed", exception);
            return null;
        }
        finally
        {
            remoteHandle.Dispose();
            if (created._nativeStream == IntPtr.Zero)
                created.Dispose();
        }
    }

    public void UpdateTargetDimensions(int width, int height)
    {
        if (!TryComputeFrameLayout(width, height, MaximumOutputFrameBytes, out _))
            return;

        IntPtr stream;
        lock (_syncRoot)
        {
            if (_disposed || (_targetWidth == width && _targetHeight == height))
                return;
            _targetWidth = width;
            _targetHeight = height;
            stream = _nativeStream;
        }

        if (
            stream != IntPtr.Zero
            && WindowSwitcherPipeWireNative.UpdateTarget(
                stream,
                checked((uint)width),
                checked((uint)height)
            ) < 0
        )
            MarkFaulted("PipeWire format renegotiation failed");
    }

    public void SetActive(bool active)
    {
        IntPtr stream;
        lock (_syncRoot)
        {
            if (_disposed)
                return;
            stream = _nativeStream;
        }

        if (
            stream != IntPtr.Zero
            && WindowSwitcherPipeWireNative.SetActive(stream, active ? 1 : 0) < 0
        )
            MarkFaulted("PipeWire could not change stream activity");
        if (!active)
            _frames.Drain();
    }

    public void DisableDmaBuf()
    {
        IntPtr stream;
        lock (_syncRoot)
        {
            if (_disposed)
                return;
            stream = _nativeStream;
        }

        if (stream != IntPtr.Zero && WindowSwitcherPipeWireNative.SetDmaBufEnabled(stream, 0) < 0)
            MarkFaulted("PipeWire could not renegotiate CPU preview buffers");
    }

    public async IAsyncEnumerable<PreviewFrame> ReadFramesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        SetActive(true);
        while (await _frames.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (_frames.Reader.TryRead(out PreviewFrame? frame))
                yield return frame;
        }
    }

    private void OnFrame(
        IntPtr userData,
        IntPtr source,
        uint accessibleSize,
        int dmaBufFileDescriptor,
        uint offset,
        int sourceStride,
        uint sourceWidth,
        uint sourceHeight,
        WindowSwitcherPipeWireNative.PixelFormat pixelFormat,
        uint drmFormat,
        ulong modifier,
        IntPtr frameLease
    )
    {
        bool dmaBufPublished = false;
        try
        {
            if (frameLease != IntPtr.Zero)
            {
                if (
                    dmaBufFileDescriptor < 0
                    || offset > int.MaxValue
                    || sourceStride <= 0
                    || sourceWidth is 0 or > int.MaxValue
                    || sourceHeight is 0 or > int.MaxValue
                    || drmFormat == 0
                )
                    return;

                lock (_syncRoot)
                {
                    if (_disposed)
                        return;
                }

                _frames.Publish(
                    new LinuxDmaBufPreviewFrame(
                        dmaBufFileDescriptor,
                        checked((int)offset),
                        sourceStride,
                        checked((int)sourceWidth),
                        checked((int)sourceHeight),
                        drmFormat,
                        modifier,
                        () => WindowSwitcherPipeWireNative.ReleaseFrame(frameLease)
                    )
                );
                dmaBufPublished = true;
                ReportFirstPublishedFrame("première frame DMA-BUF publiée par le backend PipeWire");
                return;
            }

            if (
                source == IntPtr.Zero
                || accessibleSize == 0
                || accessibleSize > MaximumInputFrameBytes
                || sourceWidth is 0 or > int.MaxValue
                || sourceHeight is 0 or > int.MaxValue
            )
                return;

            int targetWidth;
            int targetHeight;
            BgraScalePlan? scalePlan;
            lock (_syncRoot)
            {
                if (_disposed)
                    return;
                targetWidth = _targetWidth;
                targetHeight = _targetHeight;
                scalePlan = _scalePlan;
            }

            if (
                !TryComputeFrameLayout(
                    targetWidth,
                    targetHeight,
                    MaximumOutputFrameBytes,
                    out int length
                )
            )
                return;
            NativeFrameBufferPool.Lease? lease = _bufferPool.TryRent(length);
            if (lease is null)
                return;

            bool copied = BgraFrameCopier.TryCopyOrScale(
                source,
                checked((int)accessibleSize),
                sourceStride,
                checked((int)sourceWidth),
                checked((int)sourceHeight),
                pixelFormat,
                lease.Pointer,
                targetWidth,
                targetHeight,
                ref scalePlan
            );
            if (!copied)
            {
                lease.Dispose();
                return;
            }

            lock (_syncRoot)
            {
                if (!_disposed)
                    _scalePlan = scalePlan;
            }

            _frames.Publish(
                new NativeBgraPreviewFrame(
                    lease.Pointer,
                    length,
                    targetWidth,
                    targetHeight,
                    checked(targetWidth * 4),
                    lease.Dispose
                )
            );
            ReportFirstPublishedFrame("première frame CPU publiée par le backend PipeWire");
        }
        catch (Exception exception)
        {
            try
            {
                _diagnostics.Error("native frame callback failed", exception);
            }
            catch { }
        }
        finally
        {
            if (frameLease != IntPtr.Zero && !dmaBufPublished)
                WindowSwitcherPipeWireNative.ReleaseFrame(frameLease);
        }
    }

    private void ReportFirstPublishedFrame(string message)
    {
        lock (_syncRoot)
        {
            if (_framePublishedReported)
                return;
            _framePublishedReported = true;
        }
        _diagnostics.Information(message);
    }

    private void OnStateChanged(IntPtr userData, int state, string? message)
    {
        try
        {
            if (state < 0)
            {
                MarkFaulted(
                    string.IsNullOrWhiteSpace(message)
                        ? "PipeWire stream entered the error state"
                        : $"PipeWire stream entered the error state: {message}"
                );
                return;
            }
            if (!string.IsNullOrWhiteSpace(message))
                _diagnostics.Information(message);
            else
                _diagnostics.Information($"PipeWire stream state changed to {state}");
        }
        catch
        {
            lock (_syncRoot)
                _faulted = true;
        }
    }

    private void MarkFaulted(string message)
    {
        lock (_syncRoot)
        {
            if (_faulted || _disposed)
                return;
            _faulted = true;
        }
        _diagnostics.Error(message);
        _frames.Complete();
    }

    private static int GetFileDescriptor(CloseSafeHandle remoteHandle)
    {
        if (remoteHandle.IsClosed || remoteHandle.IsInvalid)
            return -1;
        long value = remoteHandle.DangerousGetHandle().ToInt64();
        return value is >= 0 and <= int.MaxValue ? (int)value : -1;
    }

    private static bool TryComputeFrameLayout(
        int width,
        int height,
        int maximumLength,
        out int length
    )
    {
        length = 0;
        if (width <= 0 || height <= 0)
            return false;
        try
        {
            length = checked(checked(width * 4) * height);
            return length > 0 && length <= maximumLength;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        IntPtr stream;
        lock (_syncRoot)
        {
            if (_disposed)
                return;
            _disposed = true;
            stream = _nativeStream;
            _nativeStream = IntPtr.Zero;
        }

        _frames.Complete();
        _frames.Drain();
        if (stream != IntPtr.Zero)
        {
            try
            {
                WindowSwitcherPipeWireNative.DestroyStream(stream);
            }
            catch (Exception exception)
            {
                _diagnostics.Error("native PipeWire stream cleanup failed", exception);
            }
        }
        if (_selfHandle.IsAllocated)
            _selfHandle.Free();
        _bufferPool.Dispose();
    }
}

internal sealed class LatestFrameChannel
{
    private readonly Channel<PreviewFrame> _channel = Channel.CreateBounded<PreviewFrame>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
        }
    );

    internal ChannelReader<PreviewFrame> Reader => _channel.Reader;

    internal void Publish(PreviewFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        while (!_channel.Writer.TryWrite(frame))
        {
            if (_channel.Reader.TryRead(out PreviewFrame? replaced))
            {
                replaced.Dispose();
                continue;
            }
            frame.Dispose();
            return;
        }
    }

    internal void Drain()
    {
        while (_channel.Reader.TryRead(out PreviewFrame? frame))
            frame.Dispose();
    }

    internal void Complete()
    {
        _channel.Writer.TryComplete();
    }
}
