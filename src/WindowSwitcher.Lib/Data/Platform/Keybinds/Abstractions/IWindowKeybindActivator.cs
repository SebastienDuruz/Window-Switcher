using System.Threading;
using System.Threading.Tasks;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;

/// <summary>
/// Activates a runtime window target from a stable target id.
/// </summary>
public interface IWindowKeybindActivator
{
    /// <summary>
    /// Raised after a window has been activated through a keybind target.
    /// Event data contains the activated <c>WindowId</c>.
    /// </summary>
    event EventHandler<string>? WindowActivated;

    /// <summary>
    /// Tries to activate a matching runtime window for the target id.
    /// </summary>
    /// <param name="targetId">Stable target id to activate.</param>
    /// <returns><see langword="true" /> when a matching target was activated.</returns>
    bool TryActivateTarget(string targetId);

    /// <summary>
    /// Asynchronously tries to activate a matching runtime window for the target id.
    /// </summary>
    /// <param name="targetId">Stable target id to activate.</param>
    /// <param name="cancellationToken">Token used to cancel the activation.</param>
    /// <returns><see langword="true" /> when a matching target was activated.</returns>
    Task<bool> TryActivateTargetAsync(
        string targetId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Updates the runtime cycle anchor after a window was activated by another interaction path,
    /// for example a mouse click on a preview.
    /// </summary>
    void NotifyWindowActivated(string windowId);
}
