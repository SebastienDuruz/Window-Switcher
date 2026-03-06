namespace WindowSwitcher.Lib.Data.Platform.SystemInfo.Abstractions;

/// <summary>
/// Configures a floating window native handle for platform-specific behavior.
/// </summary>
public interface IFloatingWindowHandleConfigurator
{
    /// <summary>
    /// Applies platform-specific window handle configuration.
    /// </summary>
    void Configure(nint windowHandle);
}
