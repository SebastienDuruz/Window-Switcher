namespace WindowSwitcherLib.Application.Platform.Diagnostics;

/// <summary>
/// Provides platform diagnostics for application info UI.
/// </summary>
public interface IPlatformAppInfoProvider
{
    /// <summary>
    /// Builds a snapshot of runtime and platform information.
    /// </summary>
    PlatformAppInfoSnapshot GetSnapshot();
}
