using System.Runtime.InteropServices;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.X11;
using WindowSwitcher.Lib.Models;
using Xunit;

namespace WindowSwitcher.Tests.Platform;

public sealed class X11FrameConverterTests
{
    [Fact]
    public void CreateFrame_ConvertsRgb565()
    {
        using var image = new XImageFixture(
            [0x00, 0xf8],
            width: 1,
            height: 1,
            stride: 2,
            bitsPerPixel: 16,
            byteOrder: X11Native.LsbFirst,
            redMask: 0xf800,
            greenMask: 0x07e0,
            blueMask: 0x001f
        );

        Assert.Equal([0, 0, 255, 255], Convert(image));
    }

    [Fact]
    public void CreateFrame_ConvertsTwentyFourBitBigEndian()
    {
        using var image = new XImageFixture(
            [30, 20, 10],
            width: 1,
            height: 1,
            stride: 3,
            bitsPerPixel: 24,
            byteOrder: X11Native.MsbFirst,
            redMask: 0x00ff0000,
            greenMask: 0x0000ff00,
            blueMask: 0x000000ff
        );

        Assert.Equal([10, 20, 30, 255], Convert(image));
    }

    [Fact]
    public void CreateFrame_CopiesCompatibleThirtyTwoBitRowsWithoutPadding()
    {
        using var image = new XImageFixture(
            [1, 2, 3, 4, 99, 98, 97, 96, 5, 6, 7, 8, 95, 94, 93, 92],
            width: 1,
            height: 2,
            stride: 8,
            bitsPerPixel: 32,
            byteOrder: X11Native.LsbFirst,
            redMask: 0x00ff0000,
            greenMask: 0x0000ff00,
            blueMask: 0x000000ff
        );

        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], Convert(image));
    }

    [Fact]
    public void CreateFrame_UsesNearestNeighborWhenResizing()
    {
        using var image = new XImageFixture(
            [
                1, 2, 3, 255,
                4, 5, 6, 255,
                7, 8, 9, 255,
                10, 11, 12, 255,
            ],
            width: 2,
            height: 2,
            stride: 8,
            bitsPerPixel: 32,
            byteOrder: X11Native.LsbFirst,
            redMask: 0x00ff0000,
            greenMask: 0x0000ff00,
            blueMask: 0x000000ff
        );

        Assert.Equal(
            [1, 2, 3, 255],
            Convert(image, new ScreenshotRequest(MaxWidthPx: 1, MaxHeightPx: 1))
        );
    }

    [Fact]
    public void CreateFrame_RejectsOutputBeyondSixteenMibibytes()
    {
        var image = new XImage
        {
            Data = new IntPtr(1),
            Width = 4096,
            Height = 4096,
            BytesPerLine = 4096 * 4,
            BitsPerPixel = 32,
            ByteOrder = X11Native.LsbFirst,
            RedMask = 0x00ff0000,
            GreenMask = 0x0000ff00,
            BlueMask = 0x000000ff,
        };
        IntPtr imagePointer = Marshal.AllocHGlobal(Marshal.SizeOf<XImage>());
        using var pool = new NativeFrameBufferPool(1);
        try
        {
            Marshal.StructureToPtr(image, imagePointer, fDeleteOld: false);
            Assert.Null(
                X11FrameConverter.CreateFrame(imagePointer, new ScreenshotRequest(), pool)
            );
        }
        finally
        {
            Marshal.FreeHGlobal(imagePointer);
        }
    }

    [Fact]
    public void CaptureFallback_DisablesSharedMemoryAndRetriesSameFrame()
    {
        var sequence = new List<string>();
        using NativeBgraPreviewFrame expected = EmptyFrame();

        NativeBgraPreviewFrame? actual = X11CaptureFallback.Capture(
            hasSharedMemoryImage: true,
            captureSharedMemory: () =>
            {
                sequence.Add("shm");
                return new X11CaptureAttempt(false, null);
            },
            disableSharedMemory: () => sequence.Add("disable"),
            captureXImage: () =>
            {
                sequence.Add("ximage");
                return expected;
            }
        );

        Assert.Same(expected, actual);
        Assert.Equal(["shm", "disable", "ximage"], sequence);
    }

    [Fact]
    public void CaptureFallback_DoesNotRetryWhenConversionRejectsCapturedImage()
    {
        bool fallbackCalled = false;

        NativeBgraPreviewFrame? actual = X11CaptureFallback.Capture(
            hasSharedMemoryImage: true,
            captureSharedMemory: () => new X11CaptureAttempt(true, null),
            disableSharedMemory: () => throw new InvalidOperationException(),
            captureXImage: () =>
            {
                fallbackCalled = true;
                return EmptyFrame();
            }
        );

        Assert.Null(actual);
        Assert.False(fallbackCalled);
    }

    private static byte[] Convert(
        XImageFixture fixture,
        ScreenshotRequest? request = null
    )
    {
        using var pool = new NativeFrameBufferPool(1);
        using NativeBgraPreviewFrame frame = Assert.IsType<NativeBgraPreviewFrame>(
            X11FrameConverter.CreateFrame(
                fixture.ImagePointer,
                request ?? new ScreenshotRequest(),
                pool
            )
        );
        var bytes = new byte[frame.Length];
        Marshal.Copy(frame.Data, bytes, 0, bytes.Length);
        return bytes;
    }

    private static NativeBgraPreviewFrame EmptyFrame() =>
        new(IntPtr.Zero, 0, 0, 0, 0, () => { });

    private sealed class XImageFixture : IDisposable
    {
        private readonly IntPtr _dataPointer;

        internal XImageFixture(
            byte[] bytes,
            int width,
            int height,
            int stride,
            int bitsPerPixel,
            int byteOrder,
            ulong redMask,
            ulong greenMask,
            ulong blueMask
        )
        {
            _dataPointer = Marshal.AllocHGlobal(bytes.Length);
            ImagePointer = Marshal.AllocHGlobal(Marshal.SizeOf<XImage>());
            Marshal.Copy(bytes, 0, _dataPointer, bytes.Length);
            Marshal.StructureToPtr(
                new XImage
                {
                    Data = _dataPointer,
                    Width = width,
                    Height = height,
                    BytesPerLine = stride,
                    BitsPerPixel = bitsPerPixel,
                    ByteOrder = byteOrder,
                    RedMask = redMask,
                    GreenMask = greenMask,
                    BlueMask = blueMask,
                },
                ImagePointer,
                fDeleteOld: false
            );
        }

        internal IntPtr ImagePointer { get; }

        public void Dispose()
        {
            Marshal.FreeHGlobal(ImagePointer);
            Marshal.FreeHGlobal(_dataPointer);
        }
    }
}
