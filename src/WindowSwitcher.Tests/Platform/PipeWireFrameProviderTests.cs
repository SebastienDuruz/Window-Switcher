using System.Diagnostics;
using System.Runtime.CompilerServices;
using Tmds.DBus;
using WindowSwitcher.Lib.Data.Platform.Diagnostics;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Pipewire;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Pipewire.Abstractions;
using WindowSwitcher.Lib.Models;
using Xunit;

namespace WindowSwitcher.Tests.Platform;

public sealed class PipeWireFrameProviderTests
{
    [Fact]
    public void PortalDbusContracts_ArePublicForRuntimeProxyGeneration()
    {
        Assert.True(typeof(IPipeWirePortalRequest).IsPublic);
        Assert.True(typeof(IPipeWirePortalScreenCast).IsPublic);
        Assert.True(typeof(IPipeWirePortalSession).IsPublic);
    }

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

        await using IAsyncEnumerator<PreviewFrame> firstEnumerator = provider
            .StreamAsync("42", new ScreenshotRequest())
            .GetAsyncEnumerator();
        Task<bool> first = firstEnumerator.MoveNextAsync().AsTask();
        await portal.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await using IAsyncEnumerator<PreviewFrame> secondEnumerator = provider
            .StreamAsync("42", new ScreenshotRequest())
            .GetAsyncEnumerator();
        Task<bool> second = secondEnumerator.MoveNextAsync().AsTask();
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

        await StartStreamAsync(provider, "42");

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

        await StartStreamAsync(provider, "84");

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

        await StartStreamAsync(provider, "42");

        Assert.Equal("42", accessor.RaisedWindowId);
        Assert.True(accessor.WasRaisedBefore(() => portal.OpenSequence));
    }

    [Fact]
    public async Task MissingRestoreToken_RepeatedPortalFailureRaisesTargetOnlyOnce()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var portal = new RecordingPortalClient();
        var accessor = new FakeWinAccessor();
        using var cache = new WaylandScreenCastMemoryCache();
        await using var provider = CreateProvider(accessor, portal, cache);

        await StartStreamAsync(provider, "42");
        await StartStreamAsync(provider, "42");

        Assert.Equal(2, portal.OpenCount);
        Assert.Equal(1, accessor.ActivationCount);

        provider.ResetSelection("42");
        await StartStreamAsync(provider, "42");

        Assert.Equal(2, accessor.ActivationCount);
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

        await StartStreamAsync(provider, "42");

        Assert.Equal(
            [
                new PreviewSelectionPromptEventArgs("42", IsPending: true),
                new PreviewSelectionPromptEventArgs("42", IsPending: false),
            ],
            notifications
        );
    }

    [Fact]
    public async Task ForgetWindow_DoesNotWaitForLeasedFramesBeforeReturning()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var stream = new LeaseAwareNativeStream();
        using var cache = new WaylandScreenCastMemoryCache();
        await using var provider = new PipeWireFrameProvider(
            new FakeWinAccessor(),
            new CapturePortalClient(),
            new SingleStreamFactory(stream),
            new RecordingDiagnostics(),
            cache
        );
        await using IAsyncEnumerator<PreviewFrame> frames = provider
            .StreamAsync("42", new ScreenshotRequest())
            .GetAsyncEnumerator();
        Assert.True(await frames.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        PreviewFrame leasedFrame = frames.Current;

        try
        {
            // The leased frame stands for a DMA-BUF frame still queued for the UI thread.
            await Task.Run(() => provider.ForgetWindow("42")).WaitAsync(TimeSpan.FromSeconds(5));
            await stream.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(stream.Disposed.Task.IsCompleted);
        }
        finally
        {
            leasedFrame.Dispose();
        }

        await stream.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
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

    private static async Task StartStreamAsync(PipeWireFrameProvider provider, string windowId)
    {
        await using IAsyncEnumerator<PreviewFrame> enumerator = provider
            .StreamAsync(windowId, new ScreenshotRequest())
            .GetAsyncEnumerator();
        _ = await enumerator.MoveNextAsync();
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
        private int _openCount;

        internal string? RestoreToken { get; private set; }
        internal long OpenSequence { get; private set; }
        internal int OpenCount => Volatile.Read(ref _openCount);

        public Task<PortalCapture?> OpenAsync(
            string? restoreToken,
            CancellationToken cancellationToken
        )
        {
            Interlocked.Increment(ref _openCount);
            RestoreToken = restoreToken;
            OpenSequence = Stopwatch.GetTimestamp();
            return Task.FromResult<PortalCapture?>(null);
        }

        public Task CloseAsync(string sessionPath, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public void Dispose() { }
    }

    private sealed class CapturePortalClient : IPipeWirePortalClient
    {
        public Task<PortalCapture?> OpenAsync(
            string? restoreToken,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult<PortalCapture?>(
                new PortalCapture(
                    "/session/42",
                    null,
                    7,
                    new CloseSafeHandle(new IntPtr(-1), ownsHandle: false)
                )
            );
        }

        public Task CloseAsync(string sessionPath, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public void Dispose() { }
    }

    private sealed class SingleStreamFactory(IPipeWireNativeStream stream)
        : IPipeWireNativeStreamFactory
    {
        public Task<IPipeWireNativeStream?> CreateAsync(
            CloseSafeHandle remoteHandle,
            uint pipeWireNodeId,
            int width,
            int height,
            CancellationToken cancellationToken
        )
        {
            remoteHandle.Dispose();
            return Task.FromResult<IPipeWireNativeStream?>(stream);
        }
    }

    /// <summary>
    /// Mirrors the native stream, whose teardown waits until every leased frame is released.
    /// </summary>
    private sealed class LeaseAwareNativeStream : IPipeWireNativeStream
    {
        private readonly ManualResetEventSlim _leaseReleased = new(initialState: true);

        internal TaskCompletionSource DisposeStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Disposed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void UpdateTargetDimensions(int width, int height) { }

        public void SetActive(bool active) { }

        public void DisableDmaBuf() { }

        public async IAsyncEnumerable<PreviewFrame> ReadFramesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken
        )
        {
            _leaseReleased.Reset();
            yield return new LeasedFrame(_leaseReleased.Set);
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        public void Dispose()
        {
            DisposeStarted.TrySetResult();
            _leaseReleased.Wait();
            Disposed.TrySetResult();
        }
    }

    private sealed class LeasedFrame(Action release) : PreviewFrame
    {
        private Action? _release = release;

        public override int WidthPx => 1;

        public override int HeightPx => 1;

        public override void Dispose()
        {
            Interlocked.Exchange(ref _release, null)?.Invoke();
        }
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
        private int _activationCount;
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
        internal int ActivationCount => Volatile.Read(ref _activationCount);

        internal bool WasRaisedBefore(Func<long> getOtherSequence)
        {
            return _raiseSequence > 0 && _raiseSequence <= getOtherSequence();
        }

        public override Task<IReadOnlyCollection<WindowConfig>?> TryGetWindowsAsync(
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyCollection<WindowConfig>?>(_windows);

        public override Task<bool> TryActivateWindowAsync(
            string windowId,
            CancellationToken cancellationToken = default
        )
        {
            Interlocked.Increment(ref _activationCount);
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
