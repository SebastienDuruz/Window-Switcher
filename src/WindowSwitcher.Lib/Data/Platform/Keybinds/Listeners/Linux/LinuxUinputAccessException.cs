namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux;

/// <summary>
/// Indicates that Linux keyboard reinjection cannot use uinput.
/// </summary>
internal sealed class LinuxUinputAccessException : InvalidOperationException
{
    /// <summary>
    /// Creates a categorized uinput access error.
    /// </summary>
    public LinuxUinputAccessException(LinuxUinputFailureKind failureKind, string message)
        : base(message)
    {
        FailureKind = failureKind;
    }

    /// <summary>
    /// Gets the category of uinput failure.
    /// </summary>
    public LinuxUinputFailureKind FailureKind { get; }
}

/// <summary>
/// Describes why Linux uinput reinjection is unavailable.
/// </summary>
internal enum LinuxUinputFailureKind
{
    /// <summary>The uinput device node does not exist.</summary>
    Missing,

    /// <summary>The uinput device node cannot be opened or written by this process.</summary>
    PermissionDenied,
}
