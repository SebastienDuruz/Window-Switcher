namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Logging;

/// <summary>
/// Logging severity levels emitted by the input service components.
/// </summary>
public enum InputLogLevel
{
    /// <summary>
    /// Verbose diagnostic information intended for troubleshooting.
    /// </summary>
    Debug,

    /// <summary>
    /// Normal lifecycle and operational messages.
    /// </summary>
    Info,

    /// <summary>
    /// Recoverable problems such as permission denials or disconnects.
    /// </summary>
    Warn,

    /// <summary>
    /// Non-recoverable failures.
    /// </summary>
    Error
}

/// <summary>
/// Delegate used to receive service log messages.
/// </summary>
/// <param name="level">Severity level of the log message.</param>
/// <param name="message">Human-readable message.</param>
/// <param name="exception">Optional exception associated with the log entry.</param>
public delegate void InputLogHandler(InputLogLevel level, string message, Exception? exception);
