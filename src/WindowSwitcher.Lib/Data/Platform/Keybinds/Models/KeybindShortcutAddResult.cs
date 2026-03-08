namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Models;

/// <summary>
/// Result returned when registering a shortcut for a target.
/// </summary>
public sealed class KeybindShortcutAddResult
{
    private KeybindShortcutAddResult(KeybindRegistrationResult status, string message)
    {
        Status = status;
        Message = message;
    }

    /// <summary>
    /// Gets the registration status.
    /// </summary>
    public KeybindRegistrationResult Status { get; }

    /// <summary>
    /// Gets the user-facing message associated with the operation.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets whether the shortcut was added successfully.
    /// </summary>
    public bool Succeeded => Status == KeybindRegistrationResult.Added;

    /// <summary>
    /// Creates a successful add result.
    /// </summary>
    public static KeybindShortcutAddResult Added()
    {
        return new KeybindShortcutAddResult(KeybindRegistrationResult.Added, string.Empty);
    }

    /// <summary>
    /// Creates an invalid result.
    /// </summary>
    public static KeybindShortcutAddResult Invalid(string message)
    {
        return new KeybindShortcutAddResult(KeybindRegistrationResult.Invalid, message);
    }

    /// <summary>
    /// Creates a duplicate result.
    /// </summary>
    public static KeybindShortcutAddResult Duplicate(string message)
    {
        return new KeybindShortcutAddResult(KeybindRegistrationResult.Duplicate, message);
    }

    /// <summary>
    /// Creates a conflict result.
    /// </summary>
    public static KeybindShortcutAddResult Conflict(string message)
    {
        return new KeybindShortcutAddResult(KeybindRegistrationResult.Conflict, message);
    }
}
