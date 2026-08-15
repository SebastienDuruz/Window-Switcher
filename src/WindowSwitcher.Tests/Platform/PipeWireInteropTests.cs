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

    [Theory]
    [InlineData(0u, 1, 2, 3, 4, 1, 2, 3, 4)]
    [InlineData(1u, 1, 2, 3, 4, 1, 2, 3, 255)]
    [InlineData(2u, 3, 2, 1, 4, 1, 2, 3, 4)]
    [InlineData(3u, 3, 2, 1, 4, 1, 2, 3, 255)]
    public void BgraFrameCopier_UsesDirectPathForEveryAcceptedFormat(
        uint formatValue,
        byte input0,
        byte input1,
        byte input2,
        byte input3,
        byte expected0,
        byte expected1,
        byte expected2,
        byte expected3
    )
    {
        var format = (WindowSwitcherPipeWireNative.PixelFormat)formatValue;
        byte[] sourceBytes = [input0, input1, input2, input3];
        IntPtr source = Marshal.AllocHGlobal(sourceBytes.Length);
        IntPtr destination = Marshal.AllocHGlobal(sourceBytes.Length);
        try
        {
            Marshal.Copy(sourceBytes, 0, source, sourceBytes.Length);

            Assert.True(
                BgraFrameCopier.TryCopyOrScale(
                    source,
                    sourceBytes.Length,
                    sourceStride: 4,
                    sourceWidth: 1,
                    sourceHeight: 1,
                    format,
                    destination,
                    destinationWidth: 1,
                    destinationHeight: 1
                )
            );
            var actual = new byte[4];
            Marshal.Copy(destination, actual, 0, actual.Length);
            Assert.Equal([expected0, expected1, expected2, expected3], actual);
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
    public void BgraFrameCopier_ReusesPrecomputedBilinearCoordinates()
    {
        byte[] sourceBytes = [1, 2, 3, 255, 4, 5, 6, 255, 7, 8, 9, 255, 10, 11, 12, 255];
        IntPtr source = Marshal.AllocHGlobal(sourceBytes.Length);
        IntPtr destination = Marshal.AllocHGlobal(3 * 3 * 4);
        BgraScalePlan? plan = null;
        try
        {
            Marshal.Copy(sourceBytes, 0, source, sourceBytes.Length);
            Assert.True(
                BgraFrameCopier.TryCopyOrScale(
                    source,
                    sourceBytes.Length,
                    sourceStride: 8,
                    sourceWidth: 2,
                    sourceHeight: 2,
                    WindowSwitcherPipeWireNative.PixelFormat.Bgra,
                    destination,
                    destinationWidth: 3,
                    destinationHeight: 3,
                    ref plan
                )
            );
            BgraScalePlan first = Assert.IsType<BgraScalePlan>(plan);

            Assert.True(
                BgraFrameCopier.TryCopyOrScale(
                    source,
                    sourceBytes.Length,
                    sourceStride: 8,
                    sourceWidth: 2,
                    sourceHeight: 2,
                    WindowSwitcherPipeWireNative.PixelFormat.Bgra,
                    destination,
                    destinationWidth: 3,
                    destinationHeight: 3,
                    ref plan
                )
            );
            Assert.Same(first, plan);
        }
        finally
        {
            Marshal.FreeHGlobal(destination);
            Marshal.FreeHGlobal(source);
        }
    }

    [Fact]
    public void LatestFrameChannel_ReleasesReplacedFrame()
    {
        var channel = new LatestFrameChannel();
        bool firstReleased = false;
        bool secondReleased = false;
        var first = new NativeBgraPreviewFrame(IntPtr.Zero, 4, 1, 1, 4, () => firstReleased = true);
        var second = new NativeBgraPreviewFrame(
            IntPtr.Zero,
            4,
            1,
            1,
            4,
            () => secondReleased = true
        );

        channel.Publish(first);
        channel.Publish(second);

        Assert.True(firstReleased);
        Assert.True(channel.Reader.TryRead(out PreviewFrame? actual));
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

    [Fact]
    public void RestoreTokenCache_ClearRemovesEveryTokenAndAlias()
    {
        using var cache = new WaylandScreenCastMemoryCache();
        cache.SetRestoreToken(["title:first", "window:1"], "token-1");
        cache.SetRestoreToken(["title:second", "window:2"], "token-2");

        cache.Clear();

        Assert.Null(cache.TakeRestoreToken(["title:first", "window:1"]));
        Assert.Null(cache.TakeRestoreToken(["title:second", "window:2"]));
    }
}
