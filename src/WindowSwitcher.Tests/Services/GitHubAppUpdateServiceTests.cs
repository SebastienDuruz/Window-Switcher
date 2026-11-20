using System.Net;
using System.Runtime.InteropServices;
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
                      "name": "WindowSwitcher-setup-1.0.0-x86_64.exe",
                      "browser_download_url": "https://example.invalid/windows.exe"
                    },
                    {
                      "name": "WindowSwitcher-setup-1.0.0-arm64.exe",
                      "browser_download_url": "https://example.invalid/windows-arm64.exe"
                    },
                    {
                      "name": "WindowSwitcher-1.0.0-x86_64.AppImage",
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
        {
            bool isArm64 = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
            Assert.Equal(
                isArm64
                    ? "WindowSwitcher-setup-1.0.0-arm64.exe"
                    : "WindowSwitcher-setup-1.0.0-x86_64.exe",
                result.AssetName
            );
            Assert.Equal(
                isArm64
                    ? "https://example.invalid/windows-arm64.exe"
                    : "https://example.invalid/windows.exe",
                result.AssetDownloadUrl
            );
        }
        else
        {
            Assert.Equal("WindowSwitcher-1.0.0-x86_64.AppImage", result.AssetName);
            Assert.Equal("https://example.invalid/linux.AppImage", result.AssetDownloadUrl);
        }
    }

    [Theory]
    [InlineData("WindowSwitcher-1.0.0-linux-x64.AppImage")]
    [InlineData("WindowSwitcher-1.0.0-aarch64.AppImage")]
    [InlineData("WindowSwitcher-1.0.0-x86_64.AppImage.zsync")]
    public async Task CheckForUpdatesAsync_FallsBackToReleasePage_WhenLinuxAssetIsIncompatible(
        string assetName
    )
    {
        Assert.SkipUnless(
            OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64,
            "Requires Linux x64, the only platform that publishes an AppImage."
        );

        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""
                {
                  "tag_name": "v1.0.0",
                  "html_url": "https://example.invalid/release",
                  "assets": [
                    {
                      "name": "{{assetName}}",
                      "browser_download_url": "https://example.invalid/incompatible"
                    }
                  ]
                }
                """,
                Encoding.UTF8,
                "application/json"
            ),
        });
        var launcher = new CapturingLauncher();
        var sut = new GitHubAppUpdateService(new HttpClient(handler), launcher);

        UpdateCheckResult result = await sut.CheckForUpdatesAsync("0.9.0");

        Assert.True(result.IsUpdateAvailable);
        Assert.Null(result.AssetName);
        Assert.Null(result.AssetDownloadUrl);
        Assert.True(result.CanStartUpdate);

        UpdateLaunchResult launchResult = await sut.LaunchUpdateAsync(result);

        Assert.True(launchResult.Launched);
        Assert.Equal("https://example.invalid/release", launcher.LastTarget);
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

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("V1.2.3+build.7", "1.2.3")]
    [InlineData(" 1.2.3-beta.1 ", "1.2.3")]
    [InlineData("1.2", "1.2")]
    [InlineData("  ", "")]
    public void NormalizeVersionLabel_StripsPrefixAndPrereleaseMetadata(
        string rawVersion,
        string expected
    )
    {
        Assert.Equal(expected, GitHubAppUpdateService.NormalizeVersionLabel(rawVersion));
    }

    [Fact]
    public async Task CheckForUpdatesAsync_SendsGitHubApiHeaders()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            captured = request;
            return JsonResponse("""{ "tag_name": "v0.9.0" }""");
        });
        var sut = new GitHubAppUpdateService(new HttpClient(handler), new CapturingLauncher());

        _ = await sut.CheckForUpdatesAsync("v0.9.0-dev");

        Assert.NotNull(captured);
        Assert.Equal(
            "https://api.github.com/repos/SebastienDuruz/Window-Switcher/releases/latest",
            captured.RequestUri?.ToString()
        );
        Assert.Equal("WindowSwitcher/0.9.0", captured.Headers.UserAgent.ToString());
        Assert.Contains(
            captured.Headers.Accept,
            header => header.MediaType == "application/vnd.github+json"
        );
        Assert.True(captured.Headers.Contains("X-GitHub-Api-Version"));
    }

    [Fact]
    public async Task CheckForUpdatesAsync_DoesNotCallFeed_WhenCurrentVersionIsInvalid()
    {
        int calls = 0;
        var handler = new StubHttpMessageHandler(_ =>
        {
            calls++;
            return JsonResponse("""{ "tag_name": "v1.0.0" }""");
        });
        var sut = new GitHubAppUpdateService(new HttpClient(handler), new CapturingLauncher());

        UpdateCheckResult result = await sut.CheckForUpdatesAsync("development");

        Assert.False(result.IsUpdateAvailable);
        Assert.Equal("Current version format is invalid.", result.Message);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ReturnsMessage_WhenFeedIsUnreachable()
    {
        var handler = new StubHttpMessageHandler(_ =>
            throw new HttpRequestException("Name resolution failed")
        );
        var sut = new GitHubAppUpdateService(new HttpClient(handler), new CapturingLauncher());

        UpdateCheckResult result = await sut.CheckForUpdatesAsync("0.9.0");

        Assert.False(result.IsUpdateAvailable);
        Assert.Equal("Unable to reach release feed: Name resolution failed", result.Message);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_PropagatesCancellationInsteadOfReportingFailure()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse("{}"));
        var sut = new GitHubAppUpdateService(new HttpClient(handler), new CapturingLauncher());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sut.CheckForUpdatesAsync("0.9.0", cancellation.Token)
        );
    }

    [Theory]
    [InlineData("not json", "Release feed payload is invalid")]
    [InlineData("""{ "tag_name": "nightly" }""", "Latest release version format is invalid.")]
    [InlineData(
        """{ "html_url": "https://example.invalid" }""",
        "Latest release version format is invalid."
    )]
    public async Task CheckForUpdatesAsync_ReturnsMessage_WhenPayloadIsUnusable(
        string payload,
        string expectedMessage
    )
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse(payload));
        var sut = new GitHubAppUpdateService(new HttpClient(handler), new CapturingLauncher());

        UpdateCheckResult result = await sut.CheckForUpdatesAsync("0.9.0");

        Assert.False(result.IsUpdateAvailable);
        Assert.StartsWith(expectedMessage, result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_IgnoresPrereleaseOfCurrentVersion()
    {
        var handler = new StubHttpMessageHandler(_ =>
            JsonResponse("""{ "tag_name": "v1.0.0-rc.1" }""")
        );
        var sut = new GitHubAppUpdateService(new HttpClient(handler), new CapturingLauncher());

        UpdateCheckResult result = await sut.CheckForUpdatesAsync("1.0.0");

        Assert.False(result.IsUpdateAvailable);
        Assert.Equal("1.0.0", result.LatestVersion);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_TruncatesLongReleaseNotes()
    {
        string body = new('x', 1500);
        var handler = new StubHttpMessageHandler(_ =>
            JsonResponse($$"""{ "tag_name": "v2.0.0", "body": "  {{body}}  " }""")
        );
        var sut = new GitHubAppUpdateService(new HttpClient(handler), new CapturingLauncher());

        UpdateCheckResult result = await sut.CheckForUpdatesAsync("1.0.0");

        Assert.Equal(new string('x', 1200) + "...", result.ReleaseNotes);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ExplainsMissingAsset_WhenReleaseHasNoCompatibleFile()
    {
        var handler = new StubHttpMessageHandler(_ =>
            JsonResponse(
                """{ "tag_name": "v2.0.0", "html_url": "https://example.invalid/release", "assets": [] }"""
            )
        );
        var sut = new GitHubAppUpdateService(new HttpClient(handler), new CapturingLauncher());

        UpdateCheckResult result = await sut.CheckForUpdatesAsync("1.0.0");

        Assert.True(result.IsUpdateAvailable);
        Assert.Null(result.AssetDownloadUrl);
        Assert.True(result.CanStartUpdate);
        Assert.Equal("Update found but no compatible direct asset was detected.", result.Message);
    }

    [Theory]
    [InlineData("handler missing", "handler missing")]
    [InlineData(null, "Unable to start update target.")]
    public async Task LaunchUpdateAsync_ReturnsFailure_WhenLauncherFails(
        string? launcherError,
        string expectedMessage
    )
    {
        var launcher = new CapturingLauncher { Succeeds = false, Error = launcherError };
        var sut = new GitHubAppUpdateService(
            new HttpClient(new StubHttpMessageHandler(_ => JsonResponse("{}"))),
            launcher
        );

        UpdateLaunchResult result = await sut.LaunchUpdateAsync(
            new UpdateCheckResult { ReleasePageUrl = "https://example.invalid/release" }
        );

        Assert.False(result.Launched);
        Assert.False(result.ShouldCloseApplication);
        Assert.Equal("https://example.invalid/release", result.LaunchTarget);
        Assert.Equal(expectedMessage, result.Message);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
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
            // Real handlers abort on cancellation; the service must rethrow instead of reporting it.
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(callback(request));
        }
    }

    private sealed class CapturingLauncher : IUpdateLinkLauncher
    {
        public bool Succeeds { get; init; } = true;
        public string? Error { get; init; }
        public string? LastTarget { get; private set; }

        public bool TryLaunch(string launchTarget, out string? errorMessage)
        {
            LastTarget = launchTarget;
            errorMessage = Succeeds ? null : Error;
            return Succeeds;
        }
    }
}
