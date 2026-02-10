using System.Runtime.InteropServices;

namespace WindowSwitcherLib.Data.WindowAccess;

public abstract class WinFactories
{
    public static WinAccessor GetAccessor()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new WinsWinAccessor();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return new LinuxWinAccessor();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            throw new PlatformNotSupportedException("Mac access is not supported on this software");
        
        throw new PlatformNotSupportedException("Unknown platform");
    }
}