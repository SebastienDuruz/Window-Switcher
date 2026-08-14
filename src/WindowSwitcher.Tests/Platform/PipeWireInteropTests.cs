using System.Runtime.InteropServices;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Pipewire;
using Xunit;

namespace WindowSwitcher.Tests.Platform;

public sealed class PipeWireInteropTests
{
    [Fact]
    public void BgraFrameCopier_HandlesStrideAndBilinearScaling()
    {
        byte[] source =
        [
            0,
            0,
            0,
            255,
            255,
            0,
            0,
            255,
            0,
            0,
            0,
            0,
            0,
            255,
            0,
            255,
            0,
            0,
            255,
            255,
            0,
            0,
            0,
            0,
        ];
        IntPtr sourcePointer = Marshal.AllocHGlobal(source.Length);
        IntPtr destinationPointer = Marshal.AllocHGlobal(3 * 3 * 4);
        try
        {
            Marshal.Copy(source, 0, sourcePointer, source.Length);

            bool copied = BgraFrameCopier.TryCopyOrScale(
                sourcePointer,
                source.Length,
                sourceStride: 12,
                sourceWidth: 2,
                sourceHeight: 2,
                destinationPointer,
                destinationWidth: 3,
                destinationHeight: 3
            );
            byte[] destination = new byte[3 * 3 * 4];
            Marshal.Copy(destinationPointer, destination, 0, destination.Length);

            Assert.True(copied);
            Assert.Equal(255, destination[3]);
            Assert.InRange(destination[(1 * 3 + 1) * 4], 63, 65);
            Assert.InRange(destination[(1 * 3 + 1) * 4 + 1], 63, 65);
            Assert.InRange(destination[(1 * 3 + 1) * 4 + 2], 63, 65);
        }
        finally
        {
            Marshal.FreeHGlobal(destinationPointer);
            Marshal.FreeHGlobal(sourcePointer);
        }
    }

    [Fact]
    public void BgraFrameCopier_HandlesNegativeStride()
    {
        byte[] bottomUp = [255, 0, 0, 255, 0, 0, 255, 255];
        IntPtr source = Marshal.AllocHGlobal(bottomUp.Length);
        IntPtr destination = Marshal.AllocHGlobal(bottomUp.Length);
        try
        {
            Marshal.Copy(bottomUp, 0, source, bottomUp.Length);

            bool copied = BgraFrameCopier.TryCopyOrScale(
                source,
                bottomUp.Length,
                sourceStride: -4,
                sourceWidth: 1,
                sourceHeight: 2,
                destination,
                destinationWidth: 1,
                destinationHeight: 2
            );
            byte[] result = new byte[bottomUp.Length];
            Marshal.Copy(destination, result, 0, result.Length);

            Assert.True(copied);
            Assert.Equal(new byte[] { 0, 0, 255, 255, 255, 0, 0, 255 }, result);
        }
        finally
        {
            Marshal.FreeHGlobal(destination);
            Marshal.FreeHGlobal(source);
        }
    }

    [Fact]
    public void BgraFrameCopier_ConvertsRgbaToBgra()
    {
        byte[] rgba = [10, 20, 30, 40];
        IntPtr source = Marshal.AllocHGlobal(rgba.Length);
        IntPtr destination = Marshal.AllocHGlobal(rgba.Length);
        try
        {
            Marshal.Copy(rgba, 0, source, rgba.Length);
            Assert.True(
                BgraFrameCopier.TryCopyOrScale(
                    source,
                    rgba.Length,
                    4,
                    1,
                    1,
                    ObsPipeWireNative.PixelFormat.Rgba,
                    destination,
                    1,
                    1
                )
            );
            byte[] bgra = new byte[4];
            Marshal.Copy(destination, bgra, 0, bgra.Length);
            Assert.Equal(new byte[] { 30, 20, 10, 40 }, bgra);
        }
        finally
        {
            Marshal.FreeHGlobal(destination);
            Marshal.FreeHGlobal(source);
        }
    }

    [Fact]
    public void BgraFrameCopier_RejectsOutputBeyondMemoryLimit()
    {
        bool copied = BgraFrameCopier.TryCopyOrScale(
            new IntPtr(1),
            sourceLength: 4,
            sourceStride: 4,
            sourceWidth: 1,
            sourceHeight: 1,
            new IntPtr(2),
            destinationWidth: 4096,
            destinationHeight: 4096
        );

        Assert.False(copied);
    }

    [Fact]
    public void LatestFrameChannel_ReleasesReplacedFrame()
    {
        var channel = new LatestFrameChannel();
        bool firstReleased = false;
        bool secondReleased = false;
        var first = new NativeBgraPreviewFrame(IntPtr.Zero, 4, 1, 1, () => firstReleased = true);
        var second = new NativeBgraPreviewFrame(IntPtr.Zero, 4, 1, 1, () => secondReleased = true);

        channel.Publish(first);
        channel.Publish(second);

        Assert.True(firstReleased);
        Assert.True(channel.Reader.TryRead(out NativeBgraPreviewFrame? actual));
        NativeBgraPreviewFrame actualFrame = Assert.IsType<NativeBgraPreviewFrame>(actual);
        Assert.Same(second, actualFrame);
        actualFrame.Dispose();
        Assert.True(secondReleased);
    }

    [Fact]
    public void NativeFrameBufferPool_ReturnsLeaseForReuse()
    {
        using var pool = new NativeFrameBufferPool(maximumBuffers: 1);
        NativeFrameBufferPool.Lease first = Assert.IsType<NativeFrameBufferPool.Lease>(
            pool.TryRent(16)
        );
        IntPtr pointer = first.Pointer;

        Assert.Null(pool.TryRent(16));
        first.Dispose();
        using NativeFrameBufferPool.Lease second = Assert.IsType<NativeFrameBufferPool.Lease>(
            pool.TryRent(16)
        );

        Assert.Equal(pointer, second.Pointer);
    }

    [Fact]
    public void RestoreTokenCache_ConsumesAllAliasesOnce()
    {
        using var cache = new WaylandScreenCastMemoryCache();
        string[] aliases = ["process_title:editor|document", "window:42"];

        Assert.True(cache.SetRestoreToken(aliases, " token-2 "));

        Assert.Equal("token-2", cache.TakeRestoreToken([aliases[1]]));
        Assert.Null(cache.TakeRestoreToken(aliases));
    }
}
