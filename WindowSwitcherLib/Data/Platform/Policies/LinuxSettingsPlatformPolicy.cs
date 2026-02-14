using WindowSwitcherLib.Data.Platform.SystemInfo.Abstractions;

namespace WindowSwitcherLib.Data.Platform.Policies;

/// <summary>
/// Linux settings policy enabling window decoration toggle.
/// </summary>
public sealed class LinuxSettingsPlatformPolicy : ISettingsPlatformPolicy
{
    public bool ShowWindowDecorationSetting => true;

    public bool ShowLinuxPreviewRefreshRateSetting => true;
}
