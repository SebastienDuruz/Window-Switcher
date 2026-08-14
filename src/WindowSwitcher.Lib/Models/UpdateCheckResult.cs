namespace WindowSwitcher.Lib.Models;

/// <summary>
/// Represents the outcome of checking whether a newer application release exists.
/// </summary>
public sealed record UpdateCheckResult
{
    /// <summary>
    /// Currently running application version.
    /// </summary>
    public string CurrentVersion { get; init; } = string.Empty;

    /// <summary>
    /// Indicates whether a newer version is available.
    /// </summary>
    public bool IsUpdateAvailable { get; init; }

    /// <summary>
    /// Latest version found from the release feed.
    /// </summary>
    public string? LatestVersion { get; init; }

    /// <summary>
    /// Human-facing release page URL.
    /// </summary>
    public string? ReleasePageUrl { get; init; }

    /// <summary>
    /// Selected release asset file name for the current platform.
    /// </summary>
    public string? AssetName { get; init; }

    /// <summary>
    /// Direct download URL for the selected release asset.
    /// </summary>
    public string? AssetDownloadUrl { get; init; }

    /// <summary>
    /// Optional trimmed release notes text.
    /// </summary>
    public string? ReleaseNotes { get; init; }

    /// <summary>
    /// Error or status message when checking updates fails or cannot be completed.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Indicates whether at least one launch target is available.
    /// </summary>
    public bool CanStartUpdate =>
        !string.IsNullOrWhiteSpace(AssetDownloadUrl) || !string.IsNullOrWhiteSpace(ReleasePageUrl);
}
