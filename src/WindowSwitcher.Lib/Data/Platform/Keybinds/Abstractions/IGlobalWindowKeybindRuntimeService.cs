namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;

/// <summary>
/// Runtime service wiring global key events to configured window keybind actions.
/// </summary>
public interface IGlobalWindowKeybindRuntimeService : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Gets whether runtime matching is currently active.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Starts runtime keybind matching.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops runtime keybind matching.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}
