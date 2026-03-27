namespace WindowSwitcher.Lib.Models;

/// <summary>
/// Represents the outcome of starting an update target.
/// </summary>
public sealed record UpdateLaunchResult
{
    /// <summary>
    /// Indicates whether launching the update target succeeded.
    /// </summary>
    public bool Launched { get; init; }

    /// <summary>
    /// Indicates whether the application should close after launch.
    /// </summary>
    public bool ShouldCloseApplication { get; init; }

    /// <summary>
    /// Target that was launched (asset URL or release page URL).
    /// </summary>
    public string? LaunchTarget { get; init; }

    /// <summary>
    /// Error or status message when launch fails.
    /// </summary>
    public string? Message { get; init; }
}
