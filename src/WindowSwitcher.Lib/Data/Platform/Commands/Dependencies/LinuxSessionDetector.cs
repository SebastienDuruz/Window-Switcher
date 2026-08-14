namespace WindowSwitcher.Lib.Data.Platform.Commands.Dependencies;

/// <summary>
/// Identifies the supported Linux desktop session families.
/// </summary>
public enum LinuxSessionKind
{
    /// <summary>The process is attached to an X11 session.</summary>
    X11,

    /// <summary>The process is attached to a Wayland session.</summary>
    Wayland,

    /// <summary>No supported Linux display session could be identified.</summary>
    Unsupported,
}

/// <summary>
/// Detects the active Linux display session without invoking external commands.
/// </summary>
public static class LinuxSessionDetector
{
    /// <summary>
    /// Gets the active Linux session kind.
    /// </summary>
    public static LinuxSessionKind GetSessionKind()
    {
        return Detect(Environment.GetEnvironmentVariable, OperatingSystem.IsLinux());
    }

    /// <summary>
    /// Gets the normalized session name, or <see langword="null" /> when unsupported.
    /// </summary>
    public static string? GetSessionType()
    {
        return GetSessionKind() switch
        {
            LinuxSessionKind.X11 => "x11",
            LinuxSessionKind.Wayland => "wayland",
            _ => null,
        };
    }

    internal static LinuxSessionKind Detect(
        Func<string, string?> environmentResolver,
        bool isLinux
    )
    {
        ArgumentNullException.ThrowIfNull(environmentResolver);
        if (!isLinux)
            return LinuxSessionKind.Unsupported;

        string sessionType = Normalize(environmentResolver("XDG_SESSION_TYPE"));
        if (string.Equals(sessionType, "x11", StringComparison.Ordinal))
            return LinuxSessionKind.X11;
        if (string.Equals(sessionType, "wayland", StringComparison.Ordinal))
            return LinuxSessionKind.Wayland;

        if (!string.IsNullOrWhiteSpace(environmentResolver("WAYLAND_DISPLAY")))
            return LinuxSessionKind.Wayland;
        if (!string.IsNullOrWhiteSpace(environmentResolver("DISPLAY")))
            return LinuxSessionKind.X11;

        return LinuxSessionKind.Unsupported;
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
    }
}
