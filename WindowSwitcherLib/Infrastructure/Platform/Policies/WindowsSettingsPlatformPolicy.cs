using WindowSwitcherLib.Application.Platform;

namespace WindowSwitcherLib.Infrastructure.Platform.Policies;

/// <summary>
/// Windows settings policy hiding Linux-specific decoration toggle.
/// </summary>
public sealed class WindowsSettingsPlatformPolicy : ISettingsPlatformPolicy
{
    public bool ShowWindowDecorationSetting => false;
}
