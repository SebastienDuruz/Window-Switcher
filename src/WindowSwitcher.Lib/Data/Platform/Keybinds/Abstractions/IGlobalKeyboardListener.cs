using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;

/// <summary>
/// Listens to keyboard input at system scope, independently of application focus.
/// </summary>
public interface IGlobalKeyboardListener : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Raised when a global keyboard event is captured.
    /// </summary>
    event EventHandler<GlobalKeyEventArgs>? KeyEvent;

    /// <summary>
    /// Gets whether the listener is currently running.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Starts keyboard listening.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops keyboard listening.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}
