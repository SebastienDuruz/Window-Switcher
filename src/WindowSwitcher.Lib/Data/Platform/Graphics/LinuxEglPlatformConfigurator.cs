namespace WindowSwitcher.Lib.Data.Platform.Graphics;

/// <summary>
/// Configures EGL defaults for Avalonia's X11 backend on Linux.
/// </summary>
public static class LinuxEglPlatformConfigurator
{
    /// <summary>
    /// Gets whether EGL was configured for Avalonia's X11 backend before process startup.
    /// </summary>
    public static bool IsX11Configured =>
        string.Equals(
            Environment.GetEnvironmentVariable("EGL_PLATFORM"),
            "x11",
            StringComparison.OrdinalIgnoreCase
        );
}
