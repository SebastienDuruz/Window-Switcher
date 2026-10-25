using System.Runtime.InteropServices;
using WindowSwitcher.Lib.Data.Platform.Commands.Dependencies;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.KWin;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.Factories;

/// <summary>
/// Default factory that selects the accessor implementation from the current OS.
/// </summary>
public sealed class RuntimeWinAccessorFactory
{
    /// <inheritdoc />
    public WinAccessorBase Create()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new WindowsWinAccessor();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return CreateLinux(
                LinuxSessionDetector.GetSessionKind(),
                LinuxSessionDetector.GetDesktopKind()
            );
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            throw new PlatformNotSupportedException("Mac access is not supported on this software");

        throw new PlatformNotSupportedException("Unknown platform");
    }

    internal static WinAccessorBase CreateLinux(LinuxSessionKind session, LinuxDesktopKind desktop)
    {
        // Native Wayland windows are invisible to EWMH; only KWin lets other clients list them.
        if (session == LinuxSessionKind.Wayland && desktop == LinuxDesktopKind.Kde)
            return new CompositeWinAccessor(
                new X11EwmhWindowAccessor(),
                new KWinWaylandWindowAccessor()
            );

        return new X11EwmhWindowAccessor();
    }
}
