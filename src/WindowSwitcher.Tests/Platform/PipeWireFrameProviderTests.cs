using System.Diagnostics;
using Avalonia.Media.Imaging;
using Tmds.DBus;
using WindowSwitcher.Lib.Data.Platform.Diagnostics;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Pipewire;
using WindowSwitcher.Lib.Models;
using Xunit;

namespace WindowSwitcher.Tests.Platform;

public sealed class PipeWireFrameProviderTests
{
    [Fact]
    public void PortalStartResult_ExtractsLastPipeWireNodeId()
    {
        var results = new Dictionary<string, object>
        {
            ["streams"] = new (uint, IDictionary<string, object>)[]
            {
                (17, new Dictionary<string, object>()),
                (29, new Dictionary<string, object>()),
            },
        };

        bool extracted = PipeWirePortalClient.TryExtractPipeWireNodeId(results, out uint nodeId);

        Assert.True(extracted);
        Assert.Equal(29u, nodeId);
    }

    [Fact]
    public async Task ConcurrentRequests_CoalescePortalCreation()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var portal = new BlockingPortalClient();
        using var cache = new WaylandScreenCastMemoryCache();
        await using var provider = CreateProvider(portal, cache);

        Task<Bitmap?> first = provider.RequestAsync("42", new ScreenshotRequest());
        await portal.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Task<Bitmap?> second = provider.RequestAsync("42", new ScreenshotRequest());
        portal.Release.TrySetResult();

        _ = await Task.WhenAll(first, second);

        Assert.Equal(1, portal.OpenCount);
    }

    [Fact]
    public async Task RestoreToken_IsPassedThenConsumedBeforePortalRequest()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var portal = new RecordingPortalClient();
        using var cache = new WaylandScreenCastMemoryCache();
        string[] aliases = ["process_title:editor|document", "title:document", "window:42"];
        cache.SetRestoreToken(aliases, "restore-1");
        await using var provider = CreateProvider(portal, cache);

        _ = await provider.RequestAsync("42", new ScreenshotRequest());

        Assert.Equal("restore-1", portal.RestoreToken);
        Assert.Null(cache.TakeRestoreToken(aliases));
    }

    [Theory]
    [InlineData("EDITOR")]
    [InlineData("Browser")]
    public async Task DuplicatedTitle_DoesNotReuseAnotherWindowRestoreToken(
        string secondProcessName
    )
    {
        if (!OperatingSystem.IsLinux())
            return;

        var portal = new RecordingPortalClient();
        var accessor = new FakeWinAccessor(
            new WindowConfig
            {
                WindowId = "42",
                ProcessName = "Editor",
                WindowTitle = "Document",
            },
            new WindowConfig
            {
                WindowId = "84",
                ProcessName = secondProcessName,
                WindowTitle = " document ",
            }
        );
        using var cache = new WaylandScreenCastMemoryCache();
        cache.SetRestoreToken(
            ["process_title:editor|document", "title:document", "window:42"],
            "restore-first"
        );
        await using var provider = CreateProvider(accessor, portal, cache);
        var notifications = new List<PreviewSelectionPromptEventArgs>();
        provider.SelectionPromptChanged += (_, eventArgs) => notifications.Add(eventArgs);

        _ = await provider.RequestAsync("84", new ScreenshotRequest());

        Assert.Null(portal.RestoreToken);
        Assert.Equal("84", accessor.RaisedWindowId);
        Assert.True(accessor.WasRaisedBefore(() => portal.OpenSequence));
        Assert.Equal(
            [
                new PreviewSelectionPromptEventArgs("84", IsPending: true),
                new PreviewSelectionPromptEventArgs("84", IsPending: false),
            ],
            notifications
        );
    }

    [Fact]
    public async Task MissingRestoreToken_RaisesTargetBeforeOpeningPicker()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var portal = new RecordingPortalClient();
        var accessor = new FakeWinAccessor();
        using var cache = new WaylandScreenCastMemoryCache();
        await using var provider = CreateProvider(accessor, portal, cache);

        _ = await provider.RequestAsync("42", new ScreenshotRequest());

        Assert.Equal("42", accessor.RaisedWindowId);
        Assert.True(accessor.WasRaisedBefore(() => portal.OpenSequence));
    }

    [Fact]
    public async Task MissingRestoreToken_ReportsSelectionPromptLifetime()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var portal = new RecordingPortalClient();
        using var cache = new WaylandScreenCastMemoryCache();
        await using var provider = CreateProvider(portal, cache);
        var notifications = new List<PreviewSelectionPromptEventArgs>();
        provider.SelectionPromptChanged += (_, eventArgs) => notifications.Add(eventArgs);

        _ = await provider.RequestAsync("42", new ScreenshotRequest());

        Assert.Equal(
            [
                new PreviewSelectionPromptEventArgs("42", IsPending: true),
                new PreviewSelectionPromptEventArgs("42", IsPending: false),
            ],
            notifications
        );
    }

    [Fact]
    public void ResetSelection_ClearsEveryAliasForTargetTokenOnly()
    {
        using var cache = new WaylandScreenCastMemoryCache();
        string[] targetAliases = ["process_title:editor|document", "window:42"];
        string[] otherAliases = ["process_title:terminal|shell", "window:84"];
        cache.SetRestoreToken(targetAliases, "restore-target");
        cache.SetRestoreToken(otherAliases, "restore-other");
        using var provider = CreateProvider(new RecordingPortalClient(), cache);

        provider.ResetSelection("42");

        Assert.Null(cache.TakeRestoreToken(targetAliases));
        Assert.Equal("restore-other", cache.TakeRestoreToken(otherAliases));
    }

    private static PipeWireFrameProvider CreateProvider(
        IPipeWirePortalClient portal,
        WaylandScreenCastMemoryCache cache
    )
    {
        return CreateProvider(new FakeWinAccessor(), portal, cache);
    }

    private static PipeWireFrameProvider CreateProvider(
        FakeWinAccessor accessor,
        IPipeWirePortalClient portal,
        WaylandScreenCastMemoryCache cache
    )
    {
        return new PipeWireFrameProvider(
            accessor,
            portal,
            new UnusedNativeStreamFactory(),
            new RecordingDiagnostics(),
            cache
        );
    }

    private sealed class BlockingPortalClient : IPipeWirePortalClient
    {
        private int _openCount;

        internal TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int OpenCount => Volatile.Read(ref _openCount);

        public async Task<PortalCapture?> OpenAsync(
            string? restoreToken,
            CancellationToken cancellationToken
        )
        {
            Interlocked.Increment(ref _openCount);
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return null;
        }

        public Task CloseAsync(string sessionPath, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public void Dispose() { }
    }

    private sealed class RecordingPortalClient : IPipeWirePortalClient
    {
        internal string? RestoreToken { get; private set; }
        internal long OpenSequence { get; private set; }

        public Task<PortalCapture?> OpenAsync(
            string? restoreToken,
            CancellationToken cancellationToken
        )
        {
            RestoreToken = restoreToken;
            OpenSequence = Stopwatch.GetTimestamp();
            return Task.FromResult<PortalCapture?>(null);
        }

        public Task CloseAsync(string sessionPath, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public void Dispose() { }
    }

    private sealed class UnusedNativeStreamFactory : IPipeWireNativeStreamFactory
    {
        public Task<IPipeWireNativeStream?> CreateAsync(
            CloseSafeHandle remoteHandle,
            uint pipeWireNodeId,
            int width,
            int height,
            CancellationToken cancellationToken
        )
        {
            throw new InvalidOperationException(
                "No portal capture should reach the stream factory."
            );
        }
    }

    private sealed class RecordingDiagnostics : IPlatformDiagnostics
    {
        public void Information(string message) { }

        public void Warning(string message) { }

        public void Error(string message, Exception? exception = null) { }
    }

    private sealed class FakeWinAccessor : WinAccessorBase
    {
        private readonly IReadOnlyCollection<WindowConfig> _windows;
        private long _raiseSequence;

        internal FakeWinAccessor(params WindowConfig[] windows)
        {
            _windows =
                windows.Length > 0
                    ? windows
                    :
                    [
                        new WindowConfig
                        {
                            WindowId = "42",
                            ProcessName = "Editor",
                            WindowTitle = "Document",
                        },
                    ];
        }

        internal string? RaisedWindowId { get; private set; }

        internal bool WasRaisedBefore(Func<long> getOtherSequence)
        {
            return _raiseSequence > 0 && _raiseSequence <= getOtherSequence();
        }

        public override Task<IReadOnlyCollection<WindowConfig>> GetWindowsAsync(
            CancellationToken cancellationToken = default
        ) => Task.FromResult(_windows);

        public override Task<bool> TryActivateWindowAsync(
            string windowId,
            CancellationToken cancellationToken = default
        )
        {
            RaisedWindowId = windowId;
            _raiseSequence = Stopwatch.GetTimestamp();
            return Task.FromResult(true);
        }

        public override Task<bool> TryRenameWindowAsync(
            string windowId,
            string windowTitle,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(true);
    }
}
