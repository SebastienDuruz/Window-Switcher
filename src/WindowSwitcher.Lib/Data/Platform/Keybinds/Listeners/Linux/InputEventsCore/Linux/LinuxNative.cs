using System.Runtime.InteropServices;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Linux;

/// <summary>
/// Thin P/Invoke wrapper around Linux libc calls used by evdev readers.
/// </summary>
/// <remarks>
/// This code targets Linux only and relies on POSIX errno semantics.
/// </remarks>
internal static class LinuxNative
{
    private const int O_RDONLY = 0;
    private const int O_WRONLY = 1;
    private const int O_NONBLOCK = 0x800;

    // Common errno values used by retry, permission, and disconnect handling.
    public const int Eperm = 1;
    public const int Eintr = 4;
    public const int Eio = 5;
    public const int Eagain = 11;
    public const int Eacces = 13;
    public const int Enxio = 6;
    public const int Enodev = 19;
    public const int Enoent = 2;
    public const int Enotdir = 20;

    [DllImport("libc", SetLastError = true, EntryPoint = "open")]
    private static extern int OpenInternal(string pathname, int flags);

    [DllImport("libc", SetLastError = true, EntryPoint = "read")]
    private static extern nint ReadInternal(int fd, byte[] buffer, nuint count);

    [DllImport("libc", SetLastError = true, EntryPoint = "write")]
    private static extern nint WriteInternal(int fd, byte[] buffer, nuint count);

    [DllImport("libc", SetLastError = true, EntryPoint = "close")]
    private static extern int CloseInternal(int fd);

    [DllImport("libc", SetLastError = true, EntryPoint = "ioctl")]
    private static extern int IoctlBytesInternal(int fd, ulong request, byte[] data);

    [DllImport("libc", SetLastError = true, EntryPoint = "ioctl")]
    private static extern int IoctlIntInternal(int fd, ulong request, int data);

    [DllImport("libc", SetLastError = true, EntryPoint = "ioctl")]
    private static extern int IoctlInputIdInternal(int fd, ulong request, out NativeInputId data);

    [DllImport("libc", SetLastError = true, EntryPoint = "ioctl")]
    private static extern int IoctlUinputSetupInternal(
        int fd,
        ulong request,
        ref NativeUinputSetup data
    );

    /// <summary>
    /// Opens an evdev node as read-only and non-blocking.
    /// </summary>
    public static int OpenReadOnlyNonBlocking(string path)
    {
        return OpenInternal(path, O_RDONLY | O_NONBLOCK);
    }

    /// <summary>
    /// Opens a device node as write-only and non-blocking.
    /// </summary>
    public static int OpenWriteOnlyNonBlocking(string path)
    {
        return OpenInternal(path, O_WRONLY | O_NONBLOCK);
    }

    /// <summary>
    /// Reads raw bytes from the file descriptor.
    /// </summary>
    public static nint Read(int fd, byte[] buffer, int count)
    {
        return ReadInternal(fd, buffer, (nuint)count);
    }

    /// <summary>
    /// Writes raw bytes to the file descriptor.
    /// </summary>
    public static nint Write(int fd, byte[] buffer, int count)
    {
        return WriteInternal(fd, buffer, (nuint)count);
    }

    /// <summary>
    /// Closes the file descriptor.
    /// </summary>
    public static int Close(int fd)
    {
        return CloseInternal(fd);
    }

    /// <summary>
    /// Invokes ioctl with a byte buffer argument.
    /// </summary>
    public static int Ioctl(int fd, ulong request, byte[] data)
    {
        return IoctlBytesInternal(fd, request, data);
    }

    /// <summary>
    /// Invokes ioctl with an integer argument.
    /// </summary>
    public static int Ioctl(int fd, ulong request, int data)
    {
        return IoctlIntInternal(fd, request, data);
    }

    /// <summary>
    /// Reads <c>input_id</c> metadata via <c>EVIOCGID</c>.
    /// </summary>
    public static int IoctlGetId(int fd, out NativeInputId data)
    {
        return IoctlInputIdInternal(fd, request: LinuxIoctl.EviocgId, out data);
    }

    /// <summary>
    /// Invokes ioctl with a <c>uinput_setup</c> argument.
    /// </summary>
    public static int Ioctl(int fd, ulong request, ref NativeUinputSetup data)
    {
        return IoctlUinputSetupInternal(fd, request, ref data);
    }

    /// <summary>
    /// Returns the thread-local errno captured from the last native call.
    /// </summary>
    public static int GetLastErrno()
    {
        return Marshal.GetLastPInvokeError();
    }

    /// <summary>
    /// Indicates whether errno represents insufficient permissions.
    /// </summary>
    public static bool IsPermissionError(int errno)
    {
        return errno is Eacces or Eperm;
    }

    /// <summary>
    /// Indicates whether errno represents a missing path component.
    /// </summary>
    public static bool IsNotFoundError(int errno)
    {
        return errno is Enoent or Enotdir;
    }

    /// <summary>
    /// Indicates whether errno usually means a disconnected/unavailable input device.
    /// </summary>
    public static bool IsDisconnectError(int errno)
    {
        return errno is Eio or Enodev or Enxio;
    }

    /// <summary>
    /// Indicates whether errno means no data is currently available on non-blocking read.
    /// </summary>
    public static bool IsWouldBlockError(int errno)
    {
        return errno == Eagain;
    }
}
