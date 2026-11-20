using WindowSwitcher.Lib.Data.Platform.SystemInfo.Abstractions;
using WindowSwitcher.Lib.Data.Updates.Abstractions;
using WindowSwitcher.Lib.Models;
using WindowSwitcher.ViewModels;
using Xunit;

namespace WindowSwitcher.Tests.Windows;

public sealed class AppInfoViewModelTests
{
    private static readonly PlatformAppInfoSnapshot Snapshot = new(
        OsDescription: "Test OS",
        FrameworkDescription: ".NET Test",
        ProcessArchitecture: "X64",
        UiBackend: "X11",
        ConfigPath: "/tmp/config.json",
        PreviewMode: "PipeWire",
        DependencyStatus: "session: x11"
    );

    [Fact]
    public void Constructor_ShowsPlatformSnapshotAndApplicationVersion()
    {
        AppInfoViewModel sut = CreateSut(new FakeAppUpdateService());

        Assert.Equal("Test OS", sut.OsDescription);
        Assert.Equal(".NET Test", sut.FrameworkDescription);
        Assert.Equal("X64", sut.ProcessArchitecture);
        Assert.Equal("X11", sut.UiBackend);
        Assert.Equal("/tmp/config.json", sut.ConfigPath);
        Assert.Equal("PipeWire", sut.PreviewMode);
        Assert.Equal("session: x11", sut.LinuxDependenciesStatus);
        Assert.Matches(@"^\d+\.\d+\.\d+$", sut.AppVersion);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ReportsAvailableVersion()
    {
        var updates = new FakeAppUpdateService
        {
            CheckResult = new UpdateCheckResult
            {
                IsUpdateAvailable = true,
                LatestVersion = "9.0.0",
            },
        };
        AppInfoViewModel sut = CreateSut(updates);

        bool available = await sut.CheckForUpdatesAsync(force: true, showUpToDateMessage: true);

        Assert.True(available);
        Assert.True(sut.IsUpdateAvailable);
        Assert.True(sut.CanUpdateNow);
        Assert.Equal("9.0.0", sut.LatestVersion);
        Assert.Equal("Version 9.0.0 is available.", sut.UpdateStatus);
        Assert.Equal([sut.AppVersion], updates.CheckedVersions);
        Assert.False(sut.IsCheckingForUpdates);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ReportsGenericMessage_WhenLatestVersionIsUnknown()
    {
        AppInfoViewModel sut = CreateSut(
            new FakeAppUpdateService
            {
                CheckResult = new UpdateCheckResult { IsUpdateAvailable = true },
            }
        );

        await sut.CheckForUpdatesAsync(force: true, showUpToDateMessage: true);

        Assert.Equal("A new version is available.", sut.UpdateStatus);
        Assert.Equal(string.Empty, sut.LatestVersion);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ReportsFailureMessage()
    {
        AppInfoViewModel sut = CreateSut(
            new FakeAppUpdateService
            {
                CheckResult = new UpdateCheckResult { Message = "GitHub unreachable" },
            }
        );

        bool available = await sut.CheckForUpdatesAsync(force: true, showUpToDateMessage: true);

        Assert.False(available);
        Assert.False(sut.CanUpdateNow);
        Assert.Equal("Update check failed: GitHub unreachable", sut.UpdateStatus);
    }

    [Theory]
    [InlineData(true, "You are up to date.")]
    [InlineData(false, "No update available.")]
    public async Task CheckForUpdatesAsync_ReportsUpToDateState(
        bool showUpToDateMessage,
        string expectedStatus
    )
    {
        AppInfoViewModel sut = CreateSut(new FakeAppUpdateService());

        bool available = await sut.CheckForUpdatesAsync(force: true, showUpToDateMessage);

        Assert.False(available);
        Assert.Equal(expectedStatus, sut.UpdateStatus);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ReusesRecentResult_WhenNotForced()
    {
        var updates = new FakeAppUpdateService
        {
            CheckResult = new UpdateCheckResult
            {
                IsUpdateAvailable = true,
                LatestVersion = "9.0.0",
            },
        };
        AppInfoViewModel sut = CreateSut(updates);

        await sut.CheckForUpdatesAsync(force: false, showUpToDateMessage: false);
        bool cached = await sut.CheckForUpdatesAsync(force: false, showUpToDateMessage: false);
        await sut.CheckForUpdatesAsync(force: true, showUpToDateMessage: false);

        Assert.True(cached);
        Assert.Equal(2, updates.CheckedVersions.Count);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ChecksAgain_OnceTheCachedResultIsFiveMinutesOld()
    {
        var updates = new FakeAppUpdateService();
        var clock = new ManualTimeProvider();
        var sut = new AppInfoViewModel(
            new FakePlatformAppInfoProvider(),
            updates,
            () => { },
            clock
        );

        await sut.CheckForUpdatesAsync(force: false, showUpToDateMessage: false);
        clock.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
        await sut.CheckForUpdatesAsync(force: false, showUpToDateMessage: false);
        Assert.Single(updates.CheckedVersions);

        clock.Advance(TimeSpan.FromSeconds(1));
        await sut.CheckForUpdatesAsync(force: false, showUpToDateMessage: false);
        Assert.Equal(2, updates.CheckedVersions.Count);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_DoesNotStartSecondCheck_WhileOneIsRunning()
    {
        var pendingCheck = new TaskCompletionSource<UpdateCheckResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var updates = new FakeAppUpdateService { PendingCheck = pendingCheck };
        AppInfoViewModel sut = CreateSut(updates);

        Task<bool> first = sut.CheckForUpdatesAsync(force: true, showUpToDateMessage: true);
        Assert.True(sut.IsCheckingForUpdates);
        Assert.False(sut.CheckForUpdatesCommand.CanExecute(null));

        bool second = await sut.CheckForUpdatesAsync(force: true, showUpToDateMessage: true);
        pendingCheck.SetResult(new UpdateCheckResult { IsUpdateAvailable = true });
        bool firstResult = await first;

        Assert.False(second);
        Assert.True(firstResult);
        Assert.Single(updates.CheckedVersions);
        Assert.True(sut.CheckForUpdatesCommand.CanExecute(null));
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ClearsCheckingState_WhenServiceThrows()
    {
        AppInfoViewModel sut = CreateSut(
            new FakeAppUpdateService { CheckFailure = new HttpRequestException("offline") }
        );

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            sut.CheckForUpdatesAsync(force: true, showUpToDateMessage: true)
        );

        Assert.False(sut.IsCheckingForUpdates);
        Assert.True(sut.CheckForUpdatesCommand.CanExecute(null));
    }

    [Fact]
    public async Task UpdateNowCommand_LaunchesUpdateAndRequestsShutdown()
    {
        var checkResult = new UpdateCheckResult
        {
            IsUpdateAvailable = true,
            LatestVersion = "9.0.0",
            AssetDownloadUrl = "https://example.invalid/app",
        };
        var updates = new FakeAppUpdateService
        {
            CheckResult = checkResult,
            LaunchResult = new UpdateLaunchResult { Launched = true },
        };
        int shutdownRequests = 0;
        AppInfoViewModel sut = CreateSut(updates, () => shutdownRequests++);
        await sut.CheckForUpdatesAsync(force: true, showUpToDateMessage: true);

        await sut.UpdateNowCommand.ExecuteAsync(null);

        Assert.Same(checkResult, Assert.Single(updates.LaunchedUpdates));
        Assert.Equal(1, shutdownRequests);
        Assert.Equal("Update target started. Closing application...", sut.UpdateStatus);
        Assert.False(sut.IsStartingUpdate);
    }

    [Fact]
    public async Task UpdateNowCommand_KeepsApplicationOpen_WhenLaunchFails()
    {
        var updates = new FakeAppUpdateService
        {
            CheckResult = new UpdateCheckResult { IsUpdateAvailable = true },
            LaunchResult = new UpdateLaunchResult { Launched = false, Message = "no handler" },
        };
        int shutdownRequests = 0;
        AppInfoViewModel sut = CreateSut(updates, () => shutdownRequests++);
        await sut.CheckForUpdatesAsync(force: true, showUpToDateMessage: true);

        await sut.UpdateNowCommand.ExecuteAsync(null);

        Assert.Equal(0, shutdownRequests);
        Assert.Equal("Unable to start update: no handler", sut.UpdateStatus);
        Assert.False(sut.IsStartingUpdate);
        Assert.True(sut.CanUpdateNow);
    }

    [Fact]
    public async Task UpdateNowCommand_ChecksFirst_AndDoesNothingWhenNoUpdateExists()
    {
        var updates = new FakeAppUpdateService();
        AppInfoViewModel sut = CreateSut(updates);

        await sut.UpdateNowCommand.ExecuteAsync(null);

        Assert.Single(updates.CheckedVersions);
        Assert.Empty(updates.LaunchedUpdates);
        Assert.Equal("You are up to date.", sut.UpdateStatus);
    }

    private static AppInfoViewModel CreateSut(
        FakeAppUpdateService updates,
        Action? requestShutdown = null
    )
    {
        return new AppInfoViewModel(
            new FakePlatformAppInfoProvider(),
            updates,
            requestShutdown ?? (() => { })
        );
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed class FakePlatformAppInfoProvider : IPlatformAppInfoProvider
    {
        public PlatformAppInfoSnapshot GetSnapshot() => Snapshot;
    }

    private sealed class FakeAppUpdateService : IAppUpdateService
    {
        public UpdateCheckResult CheckResult { get; init; } = new();
        public TaskCompletionSource<UpdateCheckResult>? PendingCheck { get; init; }
        public Exception? CheckFailure { get; init; }
        public UpdateLaunchResult LaunchResult { get; init; } = new();
        public List<string> CheckedVersions { get; } = [];
        public List<UpdateCheckResult> LaunchedUpdates { get; } = [];

        public Task<UpdateCheckResult> CheckForUpdatesAsync(
            string currentVersion,
            CancellationToken cancellationToken = default
        )
        {
            CheckedVersions.Add(currentVersion);
            if (CheckFailure is not null)
                return Task.FromException<UpdateCheckResult>(CheckFailure);

            return PendingCheck?.Task ?? Task.FromResult(CheckResult);
        }

        public Task<UpdateLaunchResult> LaunchUpdateAsync(
            UpdateCheckResult updateCheckResult,
            CancellationToken cancellationToken = default
        )
        {
            LaunchedUpdates.Add(updateCheckResult);
            return Task.FromResult(LaunchResult);
        }
    }
}
