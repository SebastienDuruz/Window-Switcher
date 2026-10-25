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
/// Identifies the Linux desktop environments that have a dedicated integration.
/// </summary>
public enum LinuxDesktopKind
{
    /// <summary>KDE Plasma, whose KWin compositor accepts scripts over D-Bus.</summary>
    Kde,

    /// <summary>Any other desktop environment, or none.</summary>
    Other,
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

    /// <summary>
    /// Gets the active Linux desktop environment kind.
    /// </summary>
    public static LinuxDesktopKind GetDesktopKind()
    {
        return DetectDesktop(Environment.GetEnvironmentVariable, OperatingSystem.IsLinux());
    }

    internal static LinuxDesktopKind DetectDesktop(
        Func<string, string?> environmentResolver,
        bool isLinux
    )
    {
        ArgumentNullException.ThrowIfNull(environmentResolver);
        if (!isLinux)
            return LinuxDesktopKind.Other;

        // XDG_CURRENT_DESKTOP is a colon-separated list such as "KDE" or "ubuntu:GNOME".
        string currentDesktop = Normalize(environmentResolver("XDG_CURRENT_DESKTOP"));
        foreach (string desktop in currentDesktop.Split(':', StringSplitOptions.TrimEntries))
        {
            if (string.Equals(desktop, "kde", StringComparison.Ordinal))
                return LinuxDesktopKind.Kde;
        }

        return LinuxDesktopKind.Other;
    }

    internal static LinuxSessionKind Detect(Func<string, string?> environmentResolver, bool isLinux)
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
