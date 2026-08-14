using System.Runtime.InteropServices;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;

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
            return new X11EwmhWindowAccessor();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            throw new PlatformNotSupportedException("Mac access is not supported on this software");

        throw new PlatformNotSupportedException("Unknown platform");
    }
}
