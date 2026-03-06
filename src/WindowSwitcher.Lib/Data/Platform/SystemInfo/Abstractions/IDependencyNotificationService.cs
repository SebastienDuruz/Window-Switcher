namespace WindowSwitcher.Lib.Data.Platform.SystemInfo.Abstractions;

/// <summary>
/// Exposes platform dependency notifications to the UI.
/// </summary>
public interface IDependencyNotificationService
{
    /// <summary>
    /// Raised when a required platform dependency is missing.
    /// </summary>
    event Action<string>? DependencyMissing;

    /// <summary>
    /// Returns dependencies already reported as missing.
    /// </summary>
    IReadOnlyCollection<string> GetReportedMissing();
}
