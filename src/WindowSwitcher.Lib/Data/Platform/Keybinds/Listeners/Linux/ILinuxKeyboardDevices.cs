using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Linux;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Models;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux;

internal interface ILinuxInputDeviceDiscovery
{
    Task<IReadOnlyList<InputDeviceInfo>> DiscoverAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Raw evdev access used by the keyboard reader loop. Each failing call reports its errno.
/// </summary>
internal interface ILinuxEvdevIo
{
    int Open(string devicePath, out int errno);
    int SetGrab(int fileDescriptor, bool grabbed, out int errno);
    nint Read(int fileDescriptor, byte[] buffer, out int errno);
    void Close(int fileDescriptor);
}

internal sealed class LinuxEvdevIo : ILinuxEvdevIo
{
    public static LinuxEvdevIo Instance { get; } = new();

    public int Open(string devicePath, out int errno)
    {
        int fileDescriptor = LinuxNative.OpenReadOnlyNonBlocking(devicePath);
        errno = fileDescriptor < 0 ? LinuxNative.GetLastErrno() : 0;
        return fileDescriptor;
    }

    public int SetGrab(int fileDescriptor, bool grabbed, out int errno)
    {
        int result = LinuxNative.Ioctl(fileDescriptor, LinuxIoctl.EviocGrab, grabbed ? 1 : 0);
        errno = result < 0 ? LinuxNative.GetLastErrno() : 0;
        return result;
    }

    public nint Read(int fileDescriptor, byte[] buffer, out int errno)
    {
        nint bytesRead = LinuxNative.Read(fileDescriptor, buffer, buffer.Length);
        errno = bytesRead < 0 ? LinuxNative.GetLastErrno() : 0;
        return bytesRead;
    }

    public void Close(int fileDescriptor) => _ = LinuxNative.Close(fileDescriptor);
}

internal interface ILinuxKeyboardForwarder : IDisposable
{
    void Forward(NativeInputEvent nativeEvent);
}

internal interface ILinuxKeyboardForwarderFactory
{
    Task<ILinuxKeyboardForwarder> CreateAsync(
        IReadOnlyCollection<InputDeviceInfo> keyboardDevices,
        CancellationToken cancellationToken
    );
}

internal sealed class LinuxKeyboardForwarderFactory : ILinuxKeyboardForwarderFactory
{
    public async Task<ILinuxKeyboardForwarder> CreateAsync(
        IReadOnlyCollection<InputDeviceInfo> keyboardDevices,
        CancellationToken cancellationToken
    )
    {
        LinuxUinputKeyboardForwarder forwarder = await Task.Run(
                () => new LinuxUinputKeyboardForwarder(keyboardDevices),
                cancellationToken
            )
            .ConfigureAwait(false);
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken)
                .ConfigureAwait(false);
            return forwarder;
        }
        catch
        {
            forwarder.Dispose();
            throw;
        }
    }
}
