namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Models;

/// <summary>
/// Normalized key event payload produced by platform listeners.
/// </summary>
public sealed class GlobalKeyEventArgs : EventArgs
{
    /// <summary>
    /// Platform source name, for example <c>Windows</c> or <c>Linux</c>.
    /// </summary>
    public required string Platform { get; init; }

    /// <summary>
    /// Stable key code identifier suitable for diagnostics and logging.
    /// </summary>
    public required string KeyCode { get; init; }

    /// <summary>
    /// Human-readable key name when available.
    /// </summary>
    public string? KeyName { get; init; }

    /// <summary>
    /// Key state.
    /// </summary>
    public required GlobalKeyState State { get; init; }

    /// <summary>
    /// Indicates whether the event corresponds to an auto-repeat while the key remains held.
    /// </summary>
    public bool IsRepeat { get; init; }

    /// <summary>
    /// Event timestamp assigned by the application.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Optional source device identifier when available.
    /// </summary>
    public string? DeviceId { get; init; }
}
