using System.Text;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Linux;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Models;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux;

internal sealed class LinuxUinputKeyboardForwarder : ILinuxKeyboardForwarder
{
    private static readonly string[] UinputPaths = ["/dev/uinput", "/dev/input/uinput"];
    internal const string DeviceName = "Window Switcher Virtual Keyboard";
    internal const ushort VendorId = 0x1209;
    internal const ushort ProductId = 0x0001;
    private const string PermissionDiagnostic =
        "Writing /dev/uinput requires elevated access. Load the uinput module and grant the user write access to the device.";
    private readonly int _fileDescriptor;
    private bool _disposed;

    public LinuxUinputKeyboardForwarder(IEnumerable<InputDeviceInfo> keyboardDevices)
    {
        ArgumentNullException.ThrowIfNull(keyboardDevices);

        _fileDescriptor = OpenUinputDevice();
        try
        {
            ConfigureDevice(keyboardDevices);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Forward(NativeInputEvent nativeEvent)
    {
        ThrowIfDisposed();

        nint written = LinuxNative.Write(_fileDescriptor, in nativeEvent);
        if (written == NativeInputEvent.Size)
            return;

        int errno = LinuxNative.GetLastErrno();
        if (LinuxNative.IsPermissionError(errno))
        {
            throw new LinuxUinputAccessException(
                LinuxUinputFailureKind.PermissionDenied,
                $"Writing /dev/uinput was refused (errno={errno}). {PermissionDiagnostic}"
            );
        }
        throw new IOException($"write(/dev/uinput) failed (errno={errno}).");
    }

    public void Forward(IEnumerable<NativeInputEvent> nativeEvents)
    {
        ArgumentNullException.ThrowIfNull(nativeEvents);

        foreach (NativeInputEvent nativeEvent in nativeEvents)
            Forward(nativeEvent);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_fileDescriptor >= 0)
        {
            _ = LinuxNative.Ioctl(_fileDescriptor, LinuxIoctl.UiDevDestroy, 0);
            _ = LinuxNative.Close(_fileDescriptor);
        }
    }

    private static int OpenUinputDevice()
    {
        foreach (string path in UinputPaths)
        {
            int fd = LinuxNative.OpenWriteOnlyNonBlocking(path);
            if (fd >= 0)
                return fd;

            int errno = LinuxNative.GetLastErrno();
            if (LinuxNative.IsNotFoundError(errno))
                continue;

            if (LinuxNative.IsPermissionError(errno))
            {
                throw new LinuxUinputAccessException(
                    LinuxUinputFailureKind.PermissionDenied,
                    $"Global keyboard startup failed on Linux because {path} is not writable. {PermissionDiagnostic}"
                );
            }

            throw new IOException($"open({path}) failed (errno={errno}).");
        }

        throw new LinuxUinputAccessException(
            LinuxUinputFailureKind.Missing,
            "Global keyboard startup failed on Linux because no uinput device node was found."
        );
    }

    private void ConfigureDevice(IEnumerable<InputDeviceInfo> keyboardDevices)
    {
        if (
            LinuxNative.Ioctl(_fileDescriptor, LinuxIoctl.UiSetEvBit, LinuxInputConstants.EvKey) < 0
        )
            ThrowLastIoctlFailure("UI_SET_EVBIT(EV_KEY)");
        if (
            LinuxNative.Ioctl(_fileDescriptor, LinuxIoctl.UiSetEvBit, LinuxInputConstants.EvSyn) < 0
        )
            ThrowLastIoctlFailure("UI_SET_EVBIT(EV_SYN)");

        foreach (ushort keyCode in CollectKeyCodes(keyboardDevices))
        {
            if (LinuxNative.Ioctl(_fileDescriptor, LinuxIoctl.UiSetKeyBit, keyCode) >= 0)
                continue;

            _ = LinuxNative.GetLastErrno();
        }

        var setup = new NativeUinputSetup
        {
            Id = new NativeInputId
            {
                BusType = 0x03,
                Vendor = VendorId,
                Product = ProductId,
                Version = 1,
            },
            Name = CreateDeviceNameBytes(),
            ForceFeedbackEffectsMax = 0,
        };

        if (LinuxNative.Ioctl(_fileDescriptor, LinuxIoctl.UiDevSetup, ref setup) < 0)
            ThrowLastIoctlFailure("UI_DEV_SETUP");
        if (LinuxNative.Ioctl(_fileDescriptor, LinuxIoctl.UiDevCreate, 0) < 0)
            ThrowLastIoctlFailure("UI_DEV_CREATE");

    }

    internal static IReadOnlyCollection<ushort> CollectKeyCodes(
        IEnumerable<InputDeviceInfo> keyboardDevices
    )
    {
        ArgumentNullException.ThrowIfNull(keyboardDevices);
        return keyboardDevices
            .SelectMany(device => device.Caps.KeyCodes)
            .Where(code => code is > 0 and <= LinuxInputConstants.KeyMax)
            .Distinct()
            .Order()
            .ToArray();
    }

    private static byte[] CreateDeviceNameBytes()
    {
        byte[] name = new byte[80];
        byte[] encoded = Encoding.UTF8.GetBytes(DeviceName);
        int length = Math.Min(encoded.Length, name.Length - 1);
        Array.Copy(encoded, name, length);
        return name;
    }

    private static void ThrowLastIoctlFailure(string operation)
    {
        int errno = LinuxNative.GetLastErrno();
        if (LinuxNative.IsPermissionError(errno))
        {
            throw new LinuxUinputAccessException(
                LinuxUinputFailureKind.PermissionDenied,
                $"{operation} was refused (errno={errno}). {PermissionDiagnostic}"
            );
        }
        throw new IOException($"{operation} failed (errno={errno}).");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(LinuxUinputKeyboardForwarder));
    }
}
