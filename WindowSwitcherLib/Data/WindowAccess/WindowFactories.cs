using System.Runtime.InteropServices;
using WindowSwitcherLib.Models;

namespace WindowSwitcherLib.WindowAccess;

public class WindowFactories
{
    public static IWindowAccessor GetAccessor()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new WindowsWindowAccessor();
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return new LinuxX11WindowAccessor();
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            throw new PlatformNotSupportedException("Mac access is not supported on this platform");
        else
            throw new PlatformNotSupportedException("Unknown platform");
    }
}