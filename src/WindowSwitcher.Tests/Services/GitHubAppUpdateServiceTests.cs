using System.Net;
using System.Text;
using WindowSwitcher.Lib.Data.Updates;
using WindowSwitcher.Lib.Models;
using Xunit;

namespace WindowSwitcher.Tests.Services;

public sealed class GitHubAppUpdateServiceTests
{
    [Fact]
    public async Task CheckForUpdatesAsync_ReturnsAvailableUpdate_WhenReleaseIsNewer()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "tag_name": "v1.0.0",
                  "html_url": "https://github.com/SebastienDuruz/Window-Switcher/releases/tag/v1.0.0",
                  "body": "Important fixes",
                  "assets": [
                    {
                      "name": "WindowSwitcher-setup-1.0.0-win-x64.exe",
                      "browser_download_url": "https://example.invalid/windows.exe"
                    },
                    {
                      "name": "WindowSwitcher-1.0.0-linux-x64.AppImage",
                      "browser_download_url": "https://example.invalid/linux.AppImage"
                    }
                  ]
                }
                """,
                Encoding.UTF8,
                "application/json"
            ),
        });
        var sut = new GitHubAppUpdateService(new HttpClient(handler), new CapturingLauncher());

        UpdateCheckResult result = await sut.CheckForUpdatesAsync("0.9.0");

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("0.9.0", result.CurrentVersion);
        Assert.Equal("1.0.0", result.LatestVersion);
        Assert.NotNull(result.ReleasePageUrl);
        Assert.True(result.CanStartUpdate);
        Assert.False(string.IsNullOrWhiteSpace(result.ReleaseNotes));
        if (OperatingSystem.IsWindows())
            Assert.EndsWith(".exe", result.AssetName, StringComparison.OrdinalIgnoreCase);
        else
            Assert.EndsWith(".AppImage", result.AssetName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ReturnsNoUpdate_WhenLatestVersionMatchesCurrent()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "tag_name": "0.9.0",
                  "html_url": "https://github.com/SebastienDuruz/Window-Switcher/releases/tag/v0.9.0",
                  "assets": []
                }
                """,
                Encoding.UTF8,
                "application/json"
            ),
        });
        var sut = new GitHubAppUpdateService(new HttpClient(handler), new CapturingLauncher());

        UpdateCheckResult result = await sut.CheckForUpdatesAsync("0.9.0");

        Assert.False(result.IsUpdateAvailable);
        Assert.Equal("0.9.0", result.CurrentVersion);
        Assert.Equal("0.9.0", result.LatestVersion);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ReturnsMessage_WhenFeedReturnsFailureStatus()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(
            HttpStatusCode.Forbidden
        )
        {
            Content = new StringContent(string.Empty, Encoding.UTF8, "application/json"),
        });
        var sut = new GitHubAppUpdateService(new HttpClient(handler), new CapturingLauncher());

        UpdateCheckResult result = await sut.CheckForUpdatesAsync("0.9.0");

        Assert.False(result.IsUpdateAvailable);
        Assert.Contains("HTTP 403", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaunchUpdateAsync_PrefersAssetDownloadUrl_WhenAvailable()
    {
        var launcher = new CapturingLauncher();
        var sut = new GitHubAppUpdateService(
            new HttpClient(
                new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))
            ),
            launcher
        );
        var updateResult = new UpdateCheckResult
        {
            CurrentVersion = "0.9.0",
            LatestVersion = "1.0.0",
            IsUpdateAvailable = true,
            AssetDownloadUrl = "https://example.invalid/download.exe",
            ReleasePageUrl = "https://example.invalid/release",
        };

        UpdateLaunchResult launchResult = await sut.LaunchUpdateAsync(updateResult);

        Assert.True(launchResult.Launched);
        Assert.True(launchResult.ShouldCloseApplication);
        Assert.Equal("https://example.invalid/download.exe", launcher.LastTarget);
    }

    [Fact]
    public async Task LaunchUpdateAsync_ReturnsFailure_WhenNoTargetExists()
    {
        var sut = new GitHubAppUpdateService(
            new HttpClient(
                new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))
            ),
            new CapturingLauncher()
        );

        UpdateLaunchResult launchResult = await sut.LaunchUpdateAsync(
            new UpdateCheckResult
            {
                CurrentVersion = "0.9.0",
                LatestVersion = "1.0.0",
                IsUpdateAvailable = true,
            }
        );

        Assert.False(launchResult.Launched);
        Assert.False(launchResult.ShouldCloseApplication);
        Assert.Contains("No update launch target", launchResult.Message, StringComparison.Ordinal);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> callback
    ) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(callback(request));
        }
    }

    private sealed class CapturingLauncher : IUpdateLinkLauncher
    {
        public string? LastTarget { get; private set; }

        public bool TryLaunch(string launchTarget, out string? errorMessage)
        {
            LastTarget = launchTarget;
            errorMessage = null;
            return true;
        }
    }
}
