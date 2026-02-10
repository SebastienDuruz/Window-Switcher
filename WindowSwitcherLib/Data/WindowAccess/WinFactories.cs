using System.Runtime.InteropServices;
using WindowSwitcherLib.Data.WindowAccess.Accessors;

namespace WindowSwitcherLib.Data.WindowAccess;

public abstract class WinFactories
{
    public static WinAccessorBase GetAccessor()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new WindowsWinAccessorBase();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return new LinuxWinAccessorBase();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            throw new PlatformNotSupportedException("Mac access is not supported on this software");
        
        throw new PlatformNotSupportedException("Unknown platform");
    }
}