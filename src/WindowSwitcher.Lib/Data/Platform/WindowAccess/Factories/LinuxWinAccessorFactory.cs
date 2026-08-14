using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Factories.Abstractions;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.Factories;

/// <summary>
/// Creates the Linux X11/EWMH accessor used for X11 and XWayland windows.
/// </summary>
public sealed class LinuxWinAccessorFactory : IWinAccessorFactory
{
    /// <summary>
    /// Creates a Linux accessor factory.
    /// </summary>
    public LinuxWinAccessorFactory() { }

    /// <inheritdoc />
    public WinAccessorBase Create()
    {
        return new X11EwmhWindowAccessor();
    }
}
