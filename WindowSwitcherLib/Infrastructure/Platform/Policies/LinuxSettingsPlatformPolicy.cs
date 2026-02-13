using WindowSwitcherLib.Application.Platform;

namespace WindowSwitcherLib.Infrastructure.Platform.Policies;

/// <summary>
/// Linux settings policy enabling window decoration toggle.
/// </summary>
public sealed class LinuxSettingsPlatformPolicy : ISettingsPlatformPolicy
{
    public bool ShowWindowDecorationSetting => true;
}
