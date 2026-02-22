using WindowSwitcherLib.Data.Platform.Interop;
using WindowSwitcherLib.Data.Platform.SystemInfo.Abstractions;

namespace WindowSwitcherLib.Data.Platform.Policies;

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
