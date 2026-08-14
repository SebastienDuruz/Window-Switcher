using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;
using WindowSwitcher.Lib.Data.Updates.Abstractions;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Updates;

/// <summary>
/// Checks updates from the Window Switcher GitHub releases feed.
/// </summary>
public sealed class GitHubAppUpdateService : IAppUpdateService
{
    private const string ReleasesLatestEndpoint =
        "https://api.github.com/repos/SebastienDuruz/Window-Switcher/releases/latest";
    private const int MaxReleaseNotesLength = 1200;

    private readonly HttpClient _httpClient;
    private readonly IUpdateLinkLauncher _updateLinkLauncher;

    /// <summary>
    /// Creates a GitHub-based app update service.
    /// </summary>
    public GitHubAppUpdateService(HttpClient httpClient)
        : this(httpClient, new ProcessUpdateLinkLauncher()) { }

    internal GitHubAppUpdateService(HttpClient httpClient, IUpdateLinkLauncher updateLinkLauncher)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(updateLinkLauncher);

        _httpClient = httpClient;
        _updateLinkLauncher = updateLinkLauncher;
    }

    /// <inheritdoc />
    public async Task<UpdateCheckResult> CheckForUpdatesAsync(
        string currentVersion,
        CancellationToken cancellationToken = default
    )
    {
        if (!TryParseComparableVersion(currentVersion, out Version currentComparableVersion))
        {
            return new UpdateCheckResult
            {
                CurrentVersion = currentVersion,
                Message = "Current version format is invalid.",
            };
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesLatestEndpoint);
        request.Headers.UserAgent.Add(
            new ProductInfoHeaderValue("WindowSwitcher", NormalizeVersionLabel(currentVersion))
        );
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json")
        );
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult
            {
                CurrentVersion = currentVersion,
                Message = $"Unable to reach release feed: {ex.Message}",
            };
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult
                {
                    CurrentVersion = currentVersion,
                    Message = $"Release feed returned HTTP {(int)response.StatusCode}.",
                };
            }

            string json = await response
                .Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            JObject payload;
            try
            {
                payload = JObject.Parse(json);
            }
            catch (Exception ex)
            {
                return new UpdateCheckResult
                {
                    CurrentVersion = currentVersion,
                    Message = $"Release feed payload is invalid: {ex.Message}",
                };
            }

            string latestRawVersion = payload.Value<string>("tag_name") ?? string.Empty;
            if (!TryParseComparableVersion(latestRawVersion, out Version latestComparableVersion))
            {
                return new UpdateCheckResult
                {
                    CurrentVersion = currentVersion,
                    Message = "Latest release version format is invalid.",
                };
            }

            string normalizedCurrent = NormalizeVersionLabel(currentVersion);
            string normalizedLatest = NormalizeVersionLabel(latestRawVersion);
            if (latestComparableVersion <= currentComparableVersion)
            {
                return new UpdateCheckResult
                {
                    CurrentVersion = normalizedCurrent,
                    LatestVersion = normalizedLatest,
                };
            }

            string? releasePageUrl = payload.Value<string>("html_url");
            string? releaseNotes = TrimReleaseNotes(payload.Value<string>("body"));
            (string? assetName, string? assetUrl) = SelectBestAsset(payload["assets"] as JArray);

            return new UpdateCheckResult
            {
                CurrentVersion = normalizedCurrent,
                LatestVersion = normalizedLatest,
                IsUpdateAvailable = true,
                ReleasePageUrl = releasePageUrl,
                ReleaseNotes = releaseNotes,
                AssetName = assetName,
                AssetDownloadUrl = assetUrl,
                Message = string.IsNullOrWhiteSpace(assetUrl)
                    ? "Update found but no compatible direct asset was detected."
                    : null,
            };
        }
    }

    /// <inheritdoc />
    public Task<UpdateLaunchResult> LaunchUpdateAsync(
        UpdateCheckResult updateCheckResult,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(updateCheckResult);
        cancellationToken.ThrowIfCancellationRequested();

        string? launchTarget = !string.IsNullOrWhiteSpace(updateCheckResult.AssetDownloadUrl)
            ? updateCheckResult.AssetDownloadUrl
            : updateCheckResult.ReleasePageUrl;
        if (string.IsNullOrWhiteSpace(launchTarget))
        {
            return Task.FromResult(
                new UpdateLaunchResult
                {
                    Launched = false,
                    ShouldCloseApplication = false,
                    Message = "No update launch target is available.",
                }
            );
        }

        if (_updateLinkLauncher.TryLaunch(launchTarget, out string? launchError))
        {
            return Task.FromResult(
                new UpdateLaunchResult
                {
                    Launched = true,
                    ShouldCloseApplication = true,
                    LaunchTarget = launchTarget,
                }
            );
        }

        return Task.FromResult(
            new UpdateLaunchResult
            {
                Launched = false,
                ShouldCloseApplication = false,
                LaunchTarget = launchTarget,
                Message = launchError ?? "Unable to start update target.",
            }
        );
    }

    private static (string? Name, string? Url) SelectBestAsset(JArray? assets)
    {
        if (assets is null || assets.Count == 0)
            return (null, null);

        string runtimeToken = GetRuntimeToken();
        string extension = OperatingSystem.IsWindows() ? ".exe" : ".AppImage";

        string? fallbackName = null;
        string? fallbackUrl = null;

        foreach (JToken token in assets)
        {
            if (token is not JObject asset)
                continue;

            string? name = asset.Value<string>("name");
            string? url = asset.Value<string>("browser_download_url");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
                continue;

            if (!name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                continue;

            if (
                name.Contains(runtimeToken, StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            )
            {
                return (name, url);
            }

            fallbackName ??= name;
            fallbackUrl ??= url;
        }

        return (fallbackName, fallbackUrl);
    }

    private static string GetRuntimeToken()
    {
        string architectureToken = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            _ => "x64",
        };

        if (OperatingSystem.IsWindows())
            return $"win-{architectureToken}";
        if (OperatingSystem.IsLinux())
            return $"linux-{architectureToken}";

        return architectureToken;
    }

    private static string? TrimReleaseNotes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string trimmed = value.Trim();
        if (trimmed.Length <= MaxReleaseNotesLength)
            return trimmed;

        return $"{trimmed[..MaxReleaseNotesLength]}...";
    }

    private static bool TryParseComparableVersion(string? rawVersion, out Version version)
    {
        version = new Version();
        if (string.IsNullOrWhiteSpace(rawVersion))
            return false;

        string normalized = NormalizeVersionLabel(rawVersion);
        if (!Version.TryParse(normalized, out Version? parsedVersion) || parsedVersion is null)
            return false;

        version = parsedVersion;
        return true;
    }

    internal static string NormalizeVersionLabel(string rawVersion)
    {
        if (string.IsNullOrWhiteSpace(rawVersion))
            return string.Empty;

        string normalized = rawVersion.Trim();
        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[1..];

        int separatorIndex = normalized.IndexOfAny(new[] { '-', '+' });
        if (separatorIndex > 0)
            normalized = normalized[..separatorIndex];

        return normalized;
    }
}

internal interface IUpdateLinkLauncher
{
    bool TryLaunch(string launchTarget, out string? errorMessage);
}

internal sealed class ProcessUpdateLinkLauncher : IUpdateLinkLauncher
{
    public bool TryLaunch(string launchTarget, out string? errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(launchTarget);

        try
        {
            Process? process = Process.Start(
                new ProcessStartInfo { FileName = launchTarget, UseShellExecute = true }
            );
            if (process is null)
            {
                errorMessage = "The operating system did not start the update target.";
                return false;
            }

            errorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }
}
