using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;

/// <summary>
/// Provides asynchronous access to the operating system's top-level windows.
/// </summary>
public abstract class WinAccessorBase : IDisposable
{
    /// <summary>
    /// Gets the current top-level window snapshot.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the query.</param>
    /// <returns>The current window snapshot.</returns>
    public abstract Task<IReadOnlyCollection<WindowConfig>> GetWindowsAsync(
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Attempts to activate a window.
    /// </summary>
    /// <param name="windowId">Stable operating-system window identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns><see langword="true" /> when the activation request was accepted.</returns>
    public abstract Task<bool> TryActivateWindowAsync(
        string windowId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Attempts to rename a window.
    /// </summary>
    /// <param name="windowId">Stable operating-system window identifier.</param>
    /// <param name="windowTitle">New title to assign.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns><see langword="true" /> when the rename request was accepted.</returns>
    public abstract Task<bool> TryRenameWindowAsync(
        string windowId,
        string windowTitle,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Releases operating-system resources owned by the accessor.
    /// </summary>
    public virtual void Dispose() { }
}
