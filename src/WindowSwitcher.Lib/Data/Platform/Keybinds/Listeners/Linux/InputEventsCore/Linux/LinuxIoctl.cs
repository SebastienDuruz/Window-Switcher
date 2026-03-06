namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Linux;

/// <summary>
/// Helpers to build Linux ioctl request codes used by evdev.
/// </summary>
/// <remarks>
/// Constants follow the Linux <c>_IOC</c> encoding layout.
/// </remarks>
internal static class LinuxIoctl
{
    private const int IocNrbits = 8;
    private const int IocTypebits = 8;
    private const int IocSizebits = 14;

    private const int IocNrshift = 0;
    private const int IocTypeshift = IocNrshift + IocNrbits;
    private const int IocSizeshift = IocTypeshift + IocTypebits;
    private const int IocDirshift = IocSizeshift + IocSizebits;

    private const int IocRead = 2;

    private const int EvdevType = 'E';

    /// <summary>
    /// <c>EVIOCGID</c> request code.
    /// </summary>
    public static ulong EviocgId { get; } = Ior(EvdevType, 0x02, NativeInputEventSize.InputId);

    /// <summary>
    /// Builds <c>EVIOCGNAME(len)</c>.
    /// </summary>
    public static ulong EviocgName(int length) => Ior(EvdevType, 0x06, length);

    /// <summary>
    /// Builds <c>EVIOCGPHYS(len)</c>.
    /// </summary>
    public static ulong EviocgPhys(int length) => Ior(EvdevType, 0x07, length);

    /// <summary>
    /// Builds <c>EVIOCGUNIQ(len)</c>.
    /// </summary>
    public static ulong EviocgUniq(int length) => Ior(EvdevType, 0x08, length);

    /// <summary>
    /// Builds <c>EVIOCGBIT(eventType, len)</c>.
    /// </summary>
    public static ulong EviocgBit(int eventType, int length) => Ior(EvdevType, 0x20 + eventType, length);

    /// <summary>
    /// Converts a bit count to the minimum whole-byte buffer size required by ioctl bitsets.
    /// </summary>
    public static int BitsToBytes(int bits)
    {
        return Math.Max(1, (bits + 7) / 8);
    }

    /// <summary>
    /// Decodes a Linux capability bitset into a managed set of enabled bit indices.
    /// </summary>
    /// <param name="bitset">Raw bitset bytes returned by ioctl.</param>
    /// <param name="maxBit">Highest bit index to inspect (inclusive).</param>
    /// <returns>Set of enabled codes.</returns>
    public static HashSet<ushort> DecodeBitset(byte[] bitset, int maxBit)
    {
        var values = new HashSet<ushort>();

        for (var bit = 0; bit <= maxBit; bit++)
        {
            var byteIndex = bit / 8;
            if (byteIndex >= bitset.Length)
            {
                break;
            }

            var bitMask = 1 << (bit % 8);
            if ((bitset[byteIndex] & bitMask) != 0)
            {
                values.Add((ushort)bit);
            }
        }

        return values;
    }

    private static ulong Ior(int type, int number, int size)
    {
        return Ioc(IocRead, type, number, size);
    }

    private static ulong Ioc(int direction, int type, int number, int size)
    {
        return ((ulong)direction << IocDirshift)
            | ((ulong)type << IocTypeshift)
            | ((ulong)number << IocNrshift)
            | ((ulong)size << IocSizeshift);
    }

    private static class NativeInputEventSize
    {
        public static readonly int InputId = System.Runtime.InteropServices.Marshal.SizeOf<NativeInputId>();
    }
}
