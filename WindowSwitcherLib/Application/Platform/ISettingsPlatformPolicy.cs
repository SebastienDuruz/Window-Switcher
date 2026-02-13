namespace WindowSwitcherLib.Application.Platform;

/// <summary>
/// Exposes platform settings capabilities used by settings UI.
/// </summary>
public interface ISettingsPlatformPolicy
{
    /// <summary>
    /// Indicates whether window decoration setting should be visible.
    /// </summary>
    bool ShowWindowDecorationSetting { get; }
}
