using System.Runtime.InteropServices;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Linux;
using Xunit;

namespace WindowSwitcher.Tests.Platform.Input;

/// <summary>
/// Request codes and structure sizes are compared with the values the Linux kernel headers
/// produce on x86_64; a wrong code makes every evdev or uinput call fail with EINVAL or ENOTTY.
/// </summary>
public sealed class LinuxIoctlTests
{
    [Fact]
    public void EvdevRequestCodes_MatchKernelHeaders()
    {
        Assert.Equal(0x80084502UL, LinuxIoctl.EviocgId);
        Assert.Equal(0x40044590UL, LinuxIoctl.EviocGrab);
        Assert.Equal(0x81004506UL, LinuxIoctl.EviocgName(256));
        Assert.Equal(0x81004507UL, LinuxIoctl.EviocgPhys(256));
        Assert.Equal(0x81004508UL, LinuxIoctl.EviocgUniq(256));
        // EVIOCGBIT(EV_KEY, KEY_MAX / 8 + 1)
        Assert.Equal(0x80604521UL, LinuxIoctl.EviocgBit(1, 0x60));
    }

    [Fact]
    public void UinputRequestCodes_MatchKernelHeaders()
    {
        Assert.Equal(0x5501UL, LinuxIoctl.UiDevCreate);
        Assert.Equal(0x5502UL, LinuxIoctl.UiDevDestroy);
        Assert.Equal(0x405C5503UL, LinuxIoctl.UiDevSetup);
        Assert.Equal(0x40045564UL, LinuxIoctl.UiSetEvBit);
        Assert.Equal(0x40045565UL, LinuxIoctl.UiSetKeyBit);
    }

    [Fact]
    public void NativeStructures_MatchKernelLayoutOn64BitProcesses()
    {
        Assert.SkipUnless(Environment.Is64BitProcess, "struct input_event is 16 bytes on 32-bit.");

        Assert.Equal(24, NativeInputEvent.Size);
        Assert.Equal(8, Marshal.SizeOf<NativeInputId>());
        Assert.Equal(92, Marshal.SizeOf<NativeUinputSetup>());
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(8, 1)]
    [InlineData(9, 2)]
    [InlineData(0x2FF + 1, 96)]
    public void BitsToBytes_RoundsUpToWholeBytes(int bits, int expectedBytes)
    {
        Assert.Equal(expectedBytes, LinuxIoctl.BitsToBytes(bits));
    }

    [Fact]
    public void DecodeBitset_ReturnsEnabledBitIndices()
    {
        byte[] bitset = [0b0000_0101, 0b1000_0000];

        Assert.Equal<ushort>([0, 2, 15], LinuxIoctl.DecodeBitset(bitset, maxBit: 15).Order());
    }

    [Fact]
    public void DecodeBitset_IgnoresBitsAboveMaximumAndBeyondBuffer()
    {
        byte[] bitset = [0xFF];

        Assert.Equal<ushort>([0, 1, 2], LinuxIoctl.DecodeBitset(bitset, maxBit: 2).Order());
        Assert.Equal(8, LinuxIoctl.DecodeBitset(bitset, maxBit: 63).Count);
    }
}
