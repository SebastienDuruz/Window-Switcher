using System.Runtime.InteropServices;
using System.Text;
using WindowSwitcher.Lib.Data.Platform.Diagnostics;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.X11;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors;

internal sealed class X11EwmhClient : IX11EwmhClient
{
    private const int MaximumWindowCount = 65_536;
    private const int MaximumTitleBytes = 1_048_576;
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, IntPtr> _atoms = new(StringComparer.Ordinal);
    private IntPtr _display;
    private IntPtr _rootWindow;
    private bool _connectionUnavailableReported;
    private bool _ewmhUnavailableReported;
    private bool _disposed;

    public IReadOnlyList<X11EwmhWindow> GetWindows()
    {
        lock (_syncRoot)
        {
            if (!EnsureConnected())
                return [];

            try
            {
                IntPtr windowType = GetAtom("WINDOW");
                if (!TryReadProperty(
                        _rootWindow,
                        GetAtom("_NET_CLIENT_LIST"),
                        windowType,
                        MaximumWindowCount,
                        out int format,
                        out nuint itemCount,
                        out IntPtr data
                    ))
                {
                    if (!_ewmhUnavailableReported)
                    {
                        TracePlatformDiagnostics.Instance.Warning(
                            "X11 EWMH unavailable: _NET_CLIENT_LIST is missing."
                        );
                        _ewmhUnavailableReported = true;
                    }
                    return [];
                }

                _ewmhUnavailableReported = false;

                try
                {
                    if (format != 32 || itemCount > MaximumWindowCount)
                        return [];

                    var windows = new List<X11EwmhWindow>(checked((int)itemCount));
                    for (nuint index = 0; index < itemCount; index++)
                    {
                        uint windowId = ReadProtocolCardinal(data, index);
                        if (windowId == 0)
                            continue;

                        string title = ReadWindowTitle(windowId);
                        if (string.IsNullOrWhiteSpace(title))
                            continue;

                        uint processId = ReadWindowProcessId(windowId);
                        windows.Add(new X11EwmhWindow(windowId, processId, title));
                    }

                    return windows;
                }
                finally
                {
                    FreePropertyData(data);
                }
            }
            catch (Exception exception) when (
                exception is DllNotFoundException
                    or EntryPointNotFoundException
                    or BadImageFormatException
                    or OverflowException
            )
            {
                TracePlatformDiagnostics.Instance.Error(
                    "X11 EWMH window discovery failed",
                    exception
                );
                Disconnect();
                return [];
            }
        }
    }

    public bool TryActivateWindow(uint windowId)
    {
        lock (_syncRoot)
        {
            if (!EnsureConnected() || !WindowExists(windowId))
                return false;

            var message = CreateActiveWindowMessage(
                _display,
                windowId,
                GetAtom("_NET_ACTIVE_WINDOW"),
                ReadActiveWindowId()
            );
            int sent = X11Native.XSendEvent(
                _display,
                _rootWindow,
                0,
                X11Native.SubstructureNotifyMask | X11Native.SubstructureRedirectMask,
                ref message
            );
            _ = X11Native.XFlush(_display);
            return sent != 0;
        }
    }

    public bool TryRenameWindow(uint windowId, string title)
    {
        ArgumentNullException.ThrowIfNull(title);

        lock (_syncRoot)
        {
            if (!EnsureConnected() || !WindowExists(windowId))
                return false;

            byte[] utf8Title = Encoding.UTF8.GetBytes(title);
            byte[] legacyTitle = Encoding.Latin1.GetBytes(title);
            X11Native.XChangeProperty(
                _display,
                (IntPtr)windowId,
                GetAtom("_NET_WM_NAME"),
                GetAtom("UTF8_STRING"),
                8,
                X11Native.PropModeReplace,
                utf8Title,
                utf8Title.Length
            );
            X11Native.XChangeProperty(
                _display,
                (IntPtr)windowId,
                GetAtom("WM_NAME"),
                GetAtom("STRING"),
                8,
                X11Native.PropModeReplace,
                legacyTitle,
                legacyTitle.Length
            );
            _ = X11Native.XSync(_display, 0);
            return WindowExists(windowId);
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
                return;

            _disposed = true;
            Disconnect();
        }
    }

    internal static XClientMessageEvent CreateActiveWindowMessage(
        IntPtr display,
        uint windowId,
        IntPtr activeWindowAtom,
        uint currentlyActiveWindowId
    )
    {
        return new XClientMessageEvent
        {
            Type = X11Native.ClientMessage,
            Display = display,
            Window = (IntPtr)windowId,
            MessageType = activeWindowAtom,
            Format = 32,
            Data = new XClientMessageData
            {
                Data0 = 2,
                Data1 = 0,
                Data2 = (nint)currentlyActiveWindowId,
            },
        };
    }

    private bool EnsureConnected()
    {
        if (_disposed)
            return false;
        if (_display != IntPtr.Zero)
            return true;

        try
        {
            X11Native.EnsureInitialized();
            _display = X11Native.XOpenDisplay(IntPtr.Zero);
            if (_display == IntPtr.Zero)
            {
                if (!_connectionUnavailableReported)
                {
                    TracePlatformDiagnostics.Instance.Warning(
                        "X11 EWMH unavailable: the X display cannot be opened."
                    );
                    _connectionUnavailableReported = true;
                }
                return false;
            }

            _rootWindow = X11Native.XDefaultRootWindow(_display);
            if (_rootWindow != IntPtr.Zero)
            {
                _connectionUnavailableReported = false;
                return true;
            }
        }
        catch (Exception exception) when (
            exception is DllNotFoundException
                or EntryPointNotFoundException
                or BadImageFormatException
        )
        {
            TracePlatformDiagnostics.Instance.Error("X11 EWMH connection failed", exception);
        }

        Disconnect();
        return false;
    }

    private void Disconnect()
    {
        _atoms.Clear();
        _rootWindow = IntPtr.Zero;
        if (_display == IntPtr.Zero)
            return;

        _ = X11Native.XCloseDisplay(_display);
        _display = IntPtr.Zero;
    }

    private IntPtr GetAtom(string name)
    {
        if (_atoms.TryGetValue(name, out IntPtr atom))
            return atom;

        atom = X11Native.XInternAtom(_display, name, 0);
        _atoms[name] = atom;
        return atom;
    }

    private string ReadWindowTitle(uint windowId)
    {
        string value = ReadTextProperty(windowId, "_NET_WM_VISIBLE_NAME", "UTF8_STRING", Encoding.UTF8);
        if (!string.IsNullOrWhiteSpace(value))
            return value;

        value = ReadTextProperty(windowId, "_NET_WM_NAME", "UTF8_STRING", Encoding.UTF8);
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : ReadTextProperty(windowId, "WM_NAME", requestedTypeName: null, Encoding.Latin1);
    }

    private string ReadTextProperty(
        uint windowId,
        string propertyName,
        string? requestedTypeName,
        Encoding encoding
    )
    {
        IntPtr requestedType = requestedTypeName is null ? IntPtr.Zero : GetAtom(requestedTypeName);
        if (!TryReadProperty(
                (IntPtr)windowId,
                GetAtom(propertyName),
                requestedType,
                MaximumTitleBytes / 4,
                out int format,
                out nuint itemCount,
                out IntPtr data
            ))
            return string.Empty;

        try
        {
            if (format != 8 || itemCount == 0 || itemCount > MaximumTitleBytes)
                return string.Empty;

            int length = checked((int)itemCount);
            byte[] bytes = new byte[length];
            Marshal.Copy(data, bytes, 0, length);
            return encoding.GetString(bytes).TrimEnd('\0').Trim();
        }
        finally
        {
            FreePropertyData(data);
        }
    }

    private uint ReadWindowProcessId(uint windowId)
    {
        if (!TryReadProperty(
                (IntPtr)windowId,
                GetAtom("_NET_WM_PID"),
                GetAtom("CARDINAL"),
                1,
                out int format,
                out nuint itemCount,
                out IntPtr data
            ))
            return 0;

        try
        {
            return format == 32 && itemCount == 1 ? ReadProtocolCardinal(data, 0) : 0;
        }
        finally
        {
            FreePropertyData(data);
        }
    }

    private uint ReadActiveWindowId()
    {
        if (!TryReadProperty(
                _rootWindow,
                GetAtom("_NET_ACTIVE_WINDOW"),
                GetAtom("WINDOW"),
                1,
                out int format,
                out nuint itemCount,
                out IntPtr data
            ))
            return 0;

        try
        {
            return format == 32 && itemCount == 1 ? ReadProtocolCardinal(data, 0) : 0;
        }
        finally
        {
            FreePropertyData(data);
        }
    }

    private bool TryReadProperty(
        IntPtr window,
        IntPtr property,
        IntPtr requestedType,
        nint maximumItems,
        out int format,
        out nuint itemCount,
        out IntPtr data
    )
    {
        int status = X11Native.XGetWindowProperty(
            _display,
            window,
            property,
            0,
            maximumItems,
            0,
            requestedType,
            out IntPtr actualType,
            out format,
            out itemCount,
            out nuint bytesAfter,
            out data
        );

        if (
            status == X11Native.Success
            && actualType != IntPtr.Zero
            && bytesAfter == 0
            && (itemCount == 0 || data != IntPtr.Zero)
        )
            return true;

        if (data != IntPtr.Zero)
        {
            _ = X11Native.XFree(data);
            data = IntPtr.Zero;
        }

        return false;
    }

    private static void FreePropertyData(IntPtr data)
    {
        if (data != IntPtr.Zero)
            _ = X11Native.XFree(data);
    }

    private bool WindowExists(uint windowId)
    {
        return windowId != 0
            && X11Native.XGetWindowAttributes(_display, (IntPtr)windowId, out _) != 0;
    }

    internal static uint ReadProtocolCardinal(IntPtr data, nuint index)
    {
        nint value = Marshal.ReadIntPtr(data, checked((int)(index * (nuint)IntPtr.Size)));
        return unchecked((uint)value);
    }
}
