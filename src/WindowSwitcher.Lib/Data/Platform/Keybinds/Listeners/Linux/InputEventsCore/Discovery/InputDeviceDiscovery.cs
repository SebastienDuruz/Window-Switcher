using System.Text;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Linux;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Models;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Discovery;

/// <summary>
/// Discovers Linux evdev devices and probes metadata/capabilities via ioctl calls.
/// </summary>
internal sealed class InputDeviceDiscovery : ILinuxInputDeviceDiscovery
{
    private const string InputDirectory = "/dev/input";

    /// <summary>
    /// Creates a discovery component.
    /// </summary>
    public InputDeviceDiscovery() { }

    /// <summary>
    /// Scans <c>/dev/input/event*</c> and returns probe results for each visible node.
    /// </summary>
    /// <param name="ct">Cancellation token used while scanning and probing.</param>
    /// <returns>
    /// A list of discovered devices, including inaccessible entries when permission errors occur.
    /// </returns>
    public Task<IReadOnlyList<InputDeviceInfo>> DiscoverAsync(CancellationToken ct = default)
    {
        return Task.Run(() => Discover(ct), ct);
    }

    private static IReadOnlyList<InputDeviceInfo> Discover(CancellationToken ct)
    {
        var devices = new List<InputDeviceInfo>();

        if (!Directory.Exists(InputDirectory))
            return devices;

        // Numeric sort keeps event10 after event9 rather than lexicographic event1/event10/event2 ordering.
        foreach (
            var path in Directory
                .EnumerateFiles(InputDirectory, "event*")
                .OrderBy(PathSortKey, StringComparer.Ordinal)
        )
        {
            ct.ThrowIfCancellationRequested();

            var info = Probe(path);
            if (info is not null)
            {
                devices.Add(info);
            }
        }

        return devices;
    }

    /// <summary>
    /// Probes a specific device path.
    /// </summary>
    /// <param name="path">Absolute evdev node path (for example <c>/dev/input/event3</c>).</param>
    /// <param name="ct">Cancellation token used before probing starts.</param>
    /// <returns>
    /// Device info when probing succeeds, an inaccessible placeholder on permission errors,
    /// or <see langword="null"/> when the node no longer exists.
    /// </returns>
    public Task<InputDeviceInfo?> ProbeAsync(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.Run(() => Probe(path), ct);
    }

    private static InputDeviceInfo? Probe(string path)
    {
        var fd = LinuxNative.OpenReadOnlyNonBlocking(path);
        if (fd < 0)
        {
            var errno = LinuxNative.GetLastErrno();

            if (LinuxNative.IsNotFoundError(errno))
            {
                // Device disappeared between directory enumeration and open.
                return null;
            }

            if (LinuxNative.IsPermissionError(errno))
            {
                return new InputDeviceInfo
                {
                    Path = path,
                    IsAccessible = false,
                    AccessError = $"Permission denied (errno={errno})",
                };
            }

            return new InputDeviceInfo
            {
                Path = path,
                IsAccessible = false,
                AccessError = $"Open failed (errno={errno})",
            };
        }

        try
        {
            var name = ReadString(fd, LinuxIoctl.EviocgName);
            var phys = ReadString(fd, LinuxIoctl.EviocgPhys);
            var uniq = ReadString(fd, LinuxIoctl.EviocgUniq);

            // Kernel may not provide IDs for all devices; default struct values remain zero.
            _ = LinuxNative.IoctlGetId(fd, out var id);

            var caps = ReadCapabilities(fd);
            var kind = DeduceKind(caps);

            return new InputDeviceInfo
            {
                Path = path,
                Name = name,
                Phys = phys,
                Uniq = uniq,
                VendorId = id.Vendor,
                ProductId = id.Product,
                Version = id.Version,
                BusType = id.BusType,
                Kind = kind,
                Caps = caps,
                IsAccessible = true,
            };
        }
        finally
        {
            _ = LinuxNative.Close(fd);
        }
    }

    private static string PathSortKey(string path)
    {
        var fileName = Path.GetFileName(path);
        if (
            fileName.StartsWith("event", StringComparison.Ordinal)
            && int.TryParse(fileName[5..], out var eventNumber)
        )
        {
            return eventNumber.ToString("D6");
        }

        return fileName;
    }

    private static InputDeviceCapabilities ReadCapabilities(int fd)
    {
        var eventTypes = ReadBitset(fd, eventType: 0, LinuxInputConstants.EvMax);

        var keyCodes = eventTypes.Contains(LinuxInputConstants.EvKey)
            ? ReadBitset(fd, LinuxInputConstants.EvKey, LinuxInputConstants.KeyMax)
            : [];

        var relAxes = eventTypes.Contains(LinuxInputConstants.EvRel)
            ? ReadBitset(fd, LinuxInputConstants.EvRel, LinuxInputConstants.RelMax)
            : [];

        var absAxes = eventTypes.Contains(LinuxInputConstants.EvAbs)
            ? ReadBitset(fd, LinuxInputConstants.EvAbs, LinuxInputConstants.AbsMax)
            : [];

        return new InputDeviceCapabilities(
            eventTypes: eventTypes,
            keyCodes: keyCodes,
            relativeAxes: relAxes,
            absoluteAxes: absAxes
        );
    }

    private static HashSet<ushort> ReadBitset(int fd, int eventType, int maxCode)
    {
        var length = LinuxIoctl.BitsToBytes(maxCode + 1);
        var buffer = new byte[length];

        if (LinuxNative.Ioctl(fd, LinuxIoctl.EviocgBit(eventType, length), buffer) < 0)
        {
            return [];
        }

        return LinuxIoctl.DecodeBitset(buffer, maxCode);
    }

    private static string ReadString(int fd, Func<int, ulong> ioctlFactory, int maxLen = 256)
    {
        var buffer = new byte[maxLen];
        var rc = LinuxNative.Ioctl(fd, ioctlFactory(maxLen), buffer);

        if (rc < 0)
        {
            return string.Empty;
        }

        var terminator = Array.IndexOf(buffer, (byte)0);
        var length = terminator >= 0 ? terminator : Math.Min(rc, maxLen);

        if (length <= 0)
        {
            return string.Empty;
        }

        return Encoding.UTF8.GetString(buffer, 0, length).Trim();
    }

    private static DeviceKind DeduceKind(InputDeviceCapabilities caps)
    {
        // Keyboard heuristic: key capability plus alpha and enter keys.
        var isKeyboard =
            caps.EventTypes.Contains(LinuxInputConstants.EvKey)
            && caps.KeyCodes.Contains(LinuxInputConstants.KeyA)
            && caps.KeyCodes.Contains(LinuxInputConstants.KeyZ)
            && caps.KeyCodes.Contains(LinuxInputConstants.KeyEnter);

        if (isKeyboard)
        {
            return DeviceKind.Keyboard;
        }

        // Mouse heuristic: relative axes and at least one common mouse button.
        var isMouse =
            caps.EventTypes.Contains(LinuxInputConstants.EvRel)
            && (
                caps.KeyCodes.Contains(LinuxInputConstants.BtnLeft)
                || caps.KeyCodes.Contains(LinuxInputConstants.BtnRight)
            );

        if (isMouse)
        {
            return DeviceKind.Mouse;
        }

        return DeviceKind.Other;
    }
}
