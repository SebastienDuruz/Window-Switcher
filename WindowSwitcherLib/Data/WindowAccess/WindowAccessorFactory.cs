using System.Runtime.InteropServices;
using WindowSwitcherLib.Models;

namespace WindowSwitcherLib.WindowAccess;

public class WindowAccessorFactory
{
    public static IWindowAccessor GetAccessor()
    {
        if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new PlatformNotSupportedException("Window access is not supported on this platform");
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return new LinuxX11WindowAccessor();
        else if(RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            throw new PlatformNotSupportedException("Mac access is not supported on this platform");
        else
            throw new PlatformNotSupportedException("Unknown platform");
    }
}