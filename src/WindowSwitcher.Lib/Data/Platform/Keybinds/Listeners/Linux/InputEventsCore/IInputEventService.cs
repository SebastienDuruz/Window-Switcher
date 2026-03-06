using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Models;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore;

/// <summary>
/// Streams decoded Linux input events from tracked evdev devices.
/// </summary>
public interface IInputEventService
{
    /// <summary>
    /// Raised for decoded key press events (<see cref="KeyState.Down"/>).
    /// </summary>
    event EventHandler<KeyEvent>? KeyDown;

    /// <summary>
    /// Raised for decoded key release events (<see cref="KeyState.Up"/>).
    /// </summary>
    event EventHandler<KeyEvent>? KeyUp;

    /// <summary>
    /// Snapshot of known devices discovered or probed by the service.
    /// </summary>
    IReadOnlyList<InputDeviceInfo> Devices { get; }

    /// <summary>
    /// Starts device discovery/readers and begins publishing decoded events.
    /// </summary>
    /// <param name="ct">Cancellation token used to abort startup work.</param>
    /// <returns>A task that completes when startup finishes.</returns>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>
    /// Stops background readers and completes the event stream.
    /// </summary>
    /// <param name="ct">Cancellation token used while waiting for graceful stop.</param>
    /// <returns>A task that completes when stop processing finishes.</returns>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns an asynchronous stream of decoded input events.
    /// </summary>
    /// <param name="ct">Cancellation token used by the asynchronous enumeration.</param>
    /// <returns>Decoded events emitted in arrival order per device reader.</returns>
    IAsyncEnumerable<InputEvent> GetEventsAsync(CancellationToken ct = default);
}
