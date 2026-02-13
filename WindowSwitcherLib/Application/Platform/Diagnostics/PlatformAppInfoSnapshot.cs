namespace WindowSwitcherLib.Application.Platform.Diagnostics;

/// <summary>
/// Snapshot of platform and runtime diagnostics shown in the About window.
/// </summary>
public sealed record PlatformAppInfoSnapshot(
    string OsDescription,
    string FrameworkDescription,
    string ProcessArchitecture,
    string UiBackend,
    string ConfigPath,
    string PreviewMode,
    string DependencyStatus);
