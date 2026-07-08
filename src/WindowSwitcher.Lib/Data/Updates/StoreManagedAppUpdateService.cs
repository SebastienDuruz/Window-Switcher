using WindowSwitcher.Lib.Data.Updates.Abstractions;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Updates;

/// <summary>
/// Update service used when application updates are managed by the Microsoft Store.
/// </summary>
public sealed class StoreManagedAppUpdateService : IAppUpdateService
{
    /// <inheritdoc />
    public Task<UpdateCheckResult> CheckForUpdatesAsync(
        string currentVersion,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            new UpdateCheckResult
            {
                CurrentVersion = currentVersion,
                Message = "Updates are managed by Microsoft Store.",
            }
        );
    }

    /// <inheritdoc />
    public Task<UpdateLaunchResult> LaunchUpdateAsync(
        UpdateCheckResult updateCheckResult,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(updateCheckResult);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            new UpdateLaunchResult
            {
                Launched = false,
                ShouldCloseApplication = false,
                Message = "Updates are managed by Microsoft Store.",
            }
        );
    }
}
