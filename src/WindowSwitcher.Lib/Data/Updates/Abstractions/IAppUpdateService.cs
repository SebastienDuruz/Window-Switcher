using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Updates.Abstractions;

/// <summary>
/// Application service that checks and starts updates from configured release feeds.
/// </summary>
public interface IAppUpdateService
{
    /// <summary>
    /// Checks whether a newer release exists for the current application version.
    /// </summary>
    Task<UpdateCheckResult> CheckForUpdatesAsync(
        string currentVersion,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Starts the selected update target for a previously detected update.
    /// </summary>
    Task<UpdateLaunchResult> LaunchUpdateAsync(
        UpdateCheckResult updateCheckResult,
        CancellationToken cancellationToken = default
    );
}
