using WindowSwitcherLib.Application.Platform;
using WindowSwitcherLib.Data.Platform.Interop;

namespace WindowSwitcherLib.Infrastructure.Platform.Policies;

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
