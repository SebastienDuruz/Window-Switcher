using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;

/// <summary>
/// High-level service exposing global keyboard events to the application.
/// </summary>
public interface IGlobalKeyboardService : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Raised when a global keyboard event is captured.
    /// </summary>
    event EventHandler<GlobalKeyEventArgs>? KeyEvent;

    /// <summary>
    /// Gets whether the service is currently listening for global keyboard events.
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
