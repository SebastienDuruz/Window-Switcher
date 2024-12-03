using System.Runtime.InteropServices;
using WindowSwitcherLib.Models;

namespace WindowSwitcherLib.WindowAccess;

public class WindowFactories
{
    public static WindowAccessor GetAccessor()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new WindowsWindowAccessor();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return new LinuxX11WindowAccessor();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            throw new PlatformNotSupportedException("Mac access is not supported on this platform");
        
        throw new PlatformNotSupportedException("Unknown platform");
    }
}