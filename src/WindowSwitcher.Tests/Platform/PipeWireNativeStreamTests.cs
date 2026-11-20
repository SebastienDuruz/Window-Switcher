using System.Runtime.InteropServices;
using Serilog.Events;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Pipewire;
using WindowSwitcher.Tests.TestLogging;
using Xunit;
using PixelFormat = WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Pipewire.WindowSwitcherPipeWireNative.PixelFormat;

namespace WindowSwitcher.Tests.Platform;

/// <summary>
/// Drives the callbacks the native PipeWire helper invokes, with managed buffers and a fake
/// native layer. Every DMA-BUF lease handed to <c>OnFrame</c> must be released exactly once,
/// otherwise the compositor runs out of buffers and the preview freezes.
/// </summary>
public sealed class PipeWireNativeStreamTests
{
    private static readonly IntPtr StreamHandle = new(0x1000);
    private static readonly IntPtr Lease = new(0x2000);
    private const uint DrmFormatXrgb8888 = 0x34325258;

    [Fact]
    public async Task CpuFrame_IsCopiedIntoAPooledBgraFrame()
    {
        using PipeWireNativeStream sut = CreateSut(out FakeNative native, width: 2, height: 1);
        byte[] rgba = [1, 2, 3, 4, 5, 6, 7, 8];

        WithPinned(rgba, source => SendCpuFrame(sut, source, rgba.Length, 2, 1, PixelFormat.Rgba));
        using PreviewFrame frame = await ReadOneAsync(sut);

        var bgra = Assert.IsType<NativeBgraPreviewFrame>(frame);
        Assert.Equal((2, 1, 8), (bgra.WidthPx, bgra.HeightPx, bgra.Stride));
        byte[] copied = new byte[bgra.Length];
        Marshal.Copy(bgra.Data, copied, 0, copied.Length);
        Assert.Equal([3, 2, 1, 4, 7, 6, 5, 8], copied);
        Assert.Equal([true], native.ActiveChanges);
    }

    [Fact]
    public async Task CpuFrame_IsScaledToTheTargetDimensions()
    {
        using PipeWireNativeStream sut = CreateSut(out _, width: 2, height: 2);
        byte[] bgra = new byte[4 * 4 * 4];

        WithPinned(bgra, source => SendCpuFrame(sut, source, bgra.Length, 4, 4, PixelFormat.Bgra));
        using PreviewFrame frame = await ReadOneAsync(sut);

        Assert.Equal((2, 2), (frame.WidthPx, frame.HeightPx));
    }

    [Theory]
    [InlineData(0, 4, 4)] // no accessible bytes
    [InlineData(64, 0, 4)] // zero width
    [InlineData(64, 4, 0)] // zero height
    [InlineData(8, 4, 4)] // buffer shorter than the declared frame
    public async Task InvalidCpuFrame_IsDropped(int accessibleSize, int width, int height)
    {
        using PipeWireNativeStream sut = CreateSut(out _, width: 4, height: 4);
        byte[] buffer = new byte[64];

        WithPinned(
            buffer,
            source =>
                SendCpuFrame(
                    sut,
                    source,
                    accessibleSize,
                    (uint)width,
                    (uint)height,
                    PixelFormat.Bgra
                )
        );

        Assert.Null(await TryReadOneAsync(sut));
    }

    [Fact]
    public async Task DmaBufFrame_KeepsLeaseUntilTheFrameIsDisposed()
    {
        using PipeWireNativeStream sut = CreateSut(out FakeNative native);

        SendDmaBufFrame(sut, fileDescriptor: 42, stride: 7680, width: 1920, height: 1080);
        PreviewFrame frame = await ReadOneAsync(sut);

        var dmaBuf = Assert.IsType<LinuxDmaBufPreviewFrame>(frame);
        Assert.Equal(
            (42, 7680, 1920, 1080),
            (dmaBuf.FileDescriptor, dmaBuf.Stride, dmaBuf.WidthPx, dmaBuf.HeightPx)
        );
        Assert.Empty(native.ReleasedLeases);

        frame.Dispose();
        frame.Dispose();
        Assert.Equal([Lease], native.ReleasedLeases);
    }

    [Theory]
    [InlineData(-1, 7680, 1920, 1080, DrmFormatXrgb8888)] // no file descriptor
    [InlineData(42, 0, 1920, 1080, DrmFormatXrgb8888)] // invalid stride
    [InlineData(42, 7680, 0, 1080, DrmFormatXrgb8888)] // zero width
    [InlineData(42, 7680, 1920, 0, DrmFormatXrgb8888)] // zero height
    [InlineData(42, 7680, 1920, 1080, 0u)] // unknown DRM format
    public async Task InvalidDmaBufFrame_ReleasesItsLeaseImmediately(
        int fileDescriptor,
        int stride,
        int width,
        int height,
        uint drmFormat
    )
    {
        using PipeWireNativeStream sut = CreateSut(out FakeNative native);

        SendDmaBufFrame(sut, fileDescriptor, stride, (uint)width, (uint)height, drmFormat);

        Assert.Equal([Lease], native.ReleasedLeases);
        Assert.Null(await TryReadOneAsync(sut));
    }

    [Fact]
    public void DmaBufFrame_AfterDispose_ReleasesItsLeaseImmediately()
    {
        PipeWireNativeStream sut = CreateSut(out FakeNative native);
        sut.Dispose();

        SendDmaBufFrame(sut, fileDescriptor: 42, stride: 7680, width: 1920, height: 1080);

        Assert.Equal([Lease], native.ReleasedLeases);
    }

    [Fact]
    public void UnreadFrame_IsReleased_WhenANewerFrameReplacesIt()
    {
        using PipeWireNativeStream sut = CreateSut(out FakeNative native);
        IntPtr secondLease = new(0x3000);

        SendDmaBufFrame(sut, fileDescriptor: 42, stride: 7680, width: 1920, height: 1080);
        SendDmaBufFrame(sut, 43, 7680, 1920, 1080, DrmFormatXrgb8888, secondLease);

        Assert.Equal([Lease], native.ReleasedLeases);
    }

    [Fact]
    public void Deactivation_ReleasesTheUnreadFrame()
    {
        using PipeWireNativeStream sut = CreateSut(out FakeNative native);
        SendDmaBufFrame(sut, fileDescriptor: 42, stride: 7680, width: 1920, height: 1080);

        sut.SetActive(false);

        Assert.Equal([false], native.ActiveChanges);
        Assert.Equal([Lease], native.ReleasedLeases);
    }

    [Fact]
    public void Dispose_DestroysNativeStreamOnceAndReleasesUnreadFrame()
    {
        PipeWireNativeStream sut = CreateSut(out FakeNative native);
        SendDmaBufFrame(sut, fileDescriptor: 42, stride: 7680, width: 1920, height: 1080);

        sut.Dispose();
        sut.Dispose();

        Assert.Equal([StreamHandle], native.DestroyedStreams);
        Assert.Equal([Lease], native.ReleasedLeases);
    }

    [Fact]
    public void Dispose_LogsNativeCleanupFailure()
    {
        PipeWireNativeStream sut = CreateSut(
            out FakeNative native,
            out CollectingSink logs,
            width: 4,
            height: 4
        );
        native.DestroyFailure = new InvalidOperationException("boom");

        sut.Dispose();

        Assert.True(logs.Contains(LogEventLevel.Error, "Native PipeWire stream cleanup failed"));
    }

    [Fact]
    public async Task ErrorState_CompletesTheFrameStreamAndIsReportedOnce()
    {
        using PipeWireNativeStream sut = CreateSut(
            out _,
            out CollectingSink logs,
            width: 4,
            height: 4
        );

        sut.OnStateChanged(IntPtr.Zero, -1, "connection lost");
        sut.OnStateChanged(IntPtr.Zero, -1, "connection lost");

        Assert.False(await ReadsAnyFrameAsync(sut));
        Assert.Single(logs.AtLevel(LogEventLevel.Error));
        Assert.True(logs.Contains(LogEventLevel.Error, "connection lost"));
    }

    [Fact]
    public async Task NonErrorState_KeepsTheStreamRunning()
    {
        using PipeWireNativeStream sut = CreateSut(out _, out CollectingSink logs, 4, 4);

        sut.OnStateChanged(IntPtr.Zero, 3, "streaming");
        SendDmaBufFrame(sut, fileDescriptor: 42, stride: 7680, width: 1920, height: 1080);

        using PreviewFrame frame = await ReadOneAsync(sut);
        Assert.Empty(logs.AtLevel(LogEventLevel.Error));
    }

    [Fact]
    public void UpdateTargetDimensions_RenegotiatesOnlyForValidChanges()
    {
        using PipeWireNativeStream sut = CreateSut(out FakeNative native, width: 320, height: 200);

        sut.UpdateTargetDimensions(320, 200);
        sut.UpdateTargetDimensions(0, 200);
        sut.UpdateTargetDimensions(100_000, 100_000);
        sut.UpdateTargetDimensions(640, 400);

        Assert.Equal([(640u, 400u)], native.TargetUpdates);
    }

    [Fact]
    public async Task UpdateTargetDimensions_FaultsTheStream_WhenRenegotiationFails()
    {
        using PipeWireNativeStream sut = CreateSut(
            out FakeNative native,
            out CollectingSink logs,
            320,
            200
        );
        native.UpdateTargetResult = -22;

        sut.UpdateTargetDimensions(640, 400);

        Assert.False(await ReadsAnyFrameAsync(sut));
        Assert.True(logs.Contains(LogEventLevel.Error, "renegotiation failed"));
    }

    [Fact]
    public async Task DisableDmaBuf_FaultsTheStream_WhenRenegotiationFails()
    {
        using PipeWireNativeStream sut = CreateSut(
            out FakeNative native,
            out CollectingSink logs,
            4,
            4
        );
        native.SetDmaBufEnabledResult = -1;

        sut.DisableDmaBuf();

        Assert.Equal([false], native.DmaBufChanges);
        Assert.False(await ReadsAnyFrameAsync(sut));
        Assert.True(logs.Contains(LogEventLevel.Error, "CPU preview buffers"));
    }

    private static PipeWireNativeStream CreateSut(
        out FakeNative native,
        int width = 1920,
        int height = 1080
    ) => CreateSut(out native, out _, width, height);

    private static PipeWireNativeStream CreateSut(
        out FakeNative native,
        out CollectingSink logs,
        int width,
        int height
    )
    {
        native = new FakeNative();
        return new PipeWireNativeStream(
            width,
            height,
            TestLogger.Create(out logs),
            native,
            StreamHandle
        );
    }

    private static void SendCpuFrame(
        PipeWireNativeStream sut,
        IntPtr source,
        int accessibleSize,
        uint width,
        uint height,
        PixelFormat pixelFormat
    )
    {
        sut.OnFrame(
            IntPtr.Zero,
            source,
            (uint)accessibleSize,
            dmaBufFileDescriptor: -1,
            offset: 0,
            sourceStride: checked((int)width * 4),
            width,
            height,
            pixelFormat,
            drmFormat: 0,
            modifier: 0,
            frameLease: IntPtr.Zero
        );
    }

    private static void SendDmaBufFrame(
        PipeWireNativeStream sut,
        int fileDescriptor,
        int stride,
        uint width,
        uint height,
        uint drmFormat = DrmFormatXrgb8888,
        IntPtr? lease = null
    )
    {
        sut.OnFrame(
            IntPtr.Zero,
            IntPtr.Zero,
            accessibleSize: 0,
            fileDescriptor,
            offset: 0,
            stride,
            width,
            height,
            PixelFormat.Bgrx,
            drmFormat,
            modifier: LinuxDmaBufPreviewFrame.InvalidModifier,
            frameLease: lease ?? Lease
        );
    }

    private static void WithPinned(byte[] buffer, Action<IntPtr> action)
    {
        GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            action(handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
        }
    }

    private static async Task<PreviewFrame> ReadOneAsync(PipeWireNativeStream sut)
    {
        PreviewFrame? frame = await TryReadOneAsync(sut, TimeSpan.FromSeconds(5));
        Assert.NotNull(frame);
        return frame;
    }

    private static async Task<PreviewFrame?> TryReadOneAsync(
        PipeWireNativeStream sut,
        TimeSpan? timeout = null
    )
    {
        using var cancellation = new CancellationTokenSource(
            timeout ?? TimeSpan.FromMilliseconds(100)
        );
        try
        {
            await foreach (PreviewFrame frame in sut.ReadFramesAsync(cancellation.Token))
                return frame;
        }
        catch (OperationCanceledException)
        {
            // No frame arrived before the timeout, which is what the dropping tests expect.
        }
        return null;
    }

    /// <summary>
    /// Returns false when the frame sequence ends, which only happens after a fault or dispose.
    /// </summary>
    private static async Task<bool> ReadsAnyFrameAsync(PipeWireNativeStream sut)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (PreviewFrame frame in sut.ReadFramesAsync(cancellation.Token))
        {
            frame.Dispose();
            return true;
        }
        return false;
    }

    private sealed class FakeNative : IPipeWireStreamNative
    {
        public int UpdateTargetResult { get; set; }
        public int SetDmaBufEnabledResult { get; set; }
        public Exception? DestroyFailure { get; set; }
        public List<bool> ActiveChanges { get; } = [];
        public List<bool> DmaBufChanges { get; } = [];
        public List<(uint Width, uint Height)> TargetUpdates { get; } = [];
        public List<IntPtr> DestroyedStreams { get; } = [];
        public List<IntPtr> ReleasedLeases { get; } = [];

        public int SetActive(IntPtr stream, bool active)
        {
            ActiveChanges.Add(active);
            return 0;
        }

        public int SetDmaBufEnabled(IntPtr stream, bool enabled)
        {
            DmaBufChanges.Add(enabled);
            return SetDmaBufEnabledResult;
        }

        public int UpdateTarget(IntPtr stream, uint width, uint height)
        {
            TargetUpdates.Add((width, height));
            return UpdateTargetResult;
        }

        public void DestroyStream(IntPtr stream)
        {
            DestroyedStreams.Add(stream);
            if (DestroyFailure is not null)
                throw DestroyFailure;
        }

        public void ReleaseFrame(IntPtr frameLease) => ReleasedLeases.Add(frameLease);
    }
}
