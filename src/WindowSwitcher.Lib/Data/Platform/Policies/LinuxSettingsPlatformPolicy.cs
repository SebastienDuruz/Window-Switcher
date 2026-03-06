using WindowSwitcher.Lib.Data.Platform.SystemInfo.Abstractions;

namespace WindowSwitcher.Lib.Data.Platform.Policies;

/// <summary>
/// Linux settings policy enabling window decoration toggle.
/// </summary>
public sealed class LinuxSettingsPlatformPolicy : ISettingsPlatformPolicy
{
    public bool ShowWindowDecorationSetting => true;
}
