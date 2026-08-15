using System.Runtime.InteropServices;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using Xunit;

namespace WindowSwitcher.Tests.Platform;

public sealed class NativePreviewFrameTests
{
    [Fact]
    public void TryCopyTo_CopiesRowsUsingSourceAndDestinationStrides()
    {
        IntPtr source = Marshal.AllocHGlobal(16);
        IntPtr destination = Marshal.AllocHGlobal(20);
        try
        {
            byte[] sourceBytes = [1, 2, 3, 4, 5, 6, 7, 8, 90, 91, 92, 93, 94, 95, 96, 97];
            Marshal.Copy(sourceBytes, 0, source, sourceBytes.Length);
            using var frame = new NativeBgraPreviewFrame(
                source,
                16,
                widthPx: 1,
                heightPx: 2,
                stride: 8,
                release: () => { }
            );

            bool copied = frame.TryCopyTo(destination, destinationStride: 10);
            var actual = new byte[14];
            Marshal.Copy(destination, actual, 0, actual.Length);

            Assert.True(copied);
            Assert.Equal([1, 2, 3, 4], actual[..4]);
            Assert.Equal([90, 91, 92, 93], actual[10..14]);
        }
        finally
        {
            Marshal.FreeHGlobal(source);
            Marshal.FreeHGlobal(destination);
        }
    }

    [Fact]
    public void Dispose_ReleasesLeaseOnlyOnce()
    {
        int releases = 0;
        var frame = new NativeBgraPreviewFrame(IntPtr.Zero, 0, 0, 0, 0, () => releases++);

        frame.Dispose();
        frame.Dispose();

        Assert.Equal(1, releases);
    }

    [Fact]
    public void DmaBufDispose_ReleasesPipeWireLeaseOnlyOnce()
    {
        int releases = 0;
        var frame = new LinuxDmaBufPreviewFrame(
            fileDescriptor: 12,
            offset: 64,
            stride: 4096,
            widthPx: 640,
            heightPx: 360,
            drmFormat: 0x34325241,
            modifier: LinuxDmaBufPreviewFrame.InvalidModifier,
            release: () => releases++
        );

        frame.Dispose();
        frame.Dispose();

        Assert.Equal(1, releases);
        Assert.Equal(12, frame.FileDescriptor);
        Assert.Equal(64, frame.Offset);
        Assert.Equal(4096, frame.Stride);
        Assert.Equal(640, frame.WidthPx);
        Assert.Equal(360, frame.HeightPx);
    }
}
