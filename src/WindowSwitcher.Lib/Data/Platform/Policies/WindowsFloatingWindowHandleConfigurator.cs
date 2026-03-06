using WindowSwitcher.Lib.Data.Platform.Interop;
using WindowSwitcher.Lib.Data.Platform.SystemInfo.Abstractions;

namespace WindowSwitcher.Lib.Data.Platform.Policies;

/// <summary>
/// Configures Windows floating windows to stay hidden from Alt+Tab.
/// </summary>
public sealed class WindowsFloatingWindowHandleConfigurator : IFloatingWindowHandleConfigurator
{
    public void Configure(nint windowHandle)
    {
        if (windowHandle == 0)
            return;

        User32Functions.HideFromAltTab(new IntPtr(windowHandle));
    }
}
