using Newtonsoft.Json;
using Serilog.Events;
using WindowSwitcher.Lib.Data.Platform.Commands.Dependencies;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.KWin;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.KWin.Abstractions;
using WindowSwitcher.Lib.Models;
using WindowSwitcher.Tests.TestLogging;
using Xunit;

namespace WindowSwitcher.Tests.Platform;

public sealed class KWinWaylandWindowAccessorTests
{
    private static readonly Guid NativeId = Guid.Parse("f66232b1-8542-48ef-8c53-a8151f0bc453");
    private static readonly Guid X11Id = Guid.Parse("38f7ee35-a08c-40f5-b921-db03bd6a35e2");

    [Fact]
    [Trait("Category", "Integration")]
    public async Task NativeAccessor_ListsWellFormedWindowIds_OnKdeWayland()
    {
        if (
            LinuxSessionDetector.GetSessionKind() != LinuxSessionKind.Wayland
            || LinuxSessionDetector.GetDesktopKind() != LinuxDesktopKind.Kde
        )
            return;

        Serilog.ILogger logger = TestLogger.Create(out CollectingSink sink);
        await using var sut = new KWinWaylandWindowAccessor(
            new KWinScriptingClient(logger),
            (_, _) => Task.FromResult(string.Empty),
            logger,
            TimeProvider.System
        );

        IReadOnlyCollection<WindowConfig>? windows = await sut.TryGetWindowsAsync();

        Assert.True(
            windows is not null,
            string.Join(
                Environment.NewLine,
                sink.Events.Select(logEvent => $"{logEvent.RenderMessage()} {logEvent.Exception}")
            )
        );
        Assert.All(
            windows,
            window =>
                Assert.Matches(
                    "^kwin:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$",
                    window.WindowId
                )
        );
    }

    [Fact]
    public async Task TryGetWindowsAsync_ListsOnlyNativeApplicationWindows()
    {
        int otherProcessId = Environment.ProcessId + 10_000;
        var client = new FakeKWinScriptingClient
        {
            WatcherSnapshot = Snapshot(
                Window(NativeId, "Calculator", otherProcessId),
                Window(X11Id, "Legacy", otherProcessId, x11: true),
                Window(Guid.NewGuid(), "Panel", otherProcessId, normal: false),
                Window(Guid.NewGuid(), "Hidden", otherProcessId, skipTaskbar: true),
                Window(Guid.NewGuid(), "Self", Environment.ProcessId)
            ),
        };
        int resolverCalls = 0;
        await using KWinWaylandWindowAccessor sut = CreateSut(
            client,
            out _,
            (_, _) =>
            {
                resolverCalls++;
                return Task.FromResult("kcalc");
            }
        );

        IReadOnlyCollection<WindowConfig>? first = await sut.TryGetWindowsAsync();
        IReadOnlyCollection<WindowConfig>? second = await sut.TryGetWindowsAsync();

        WindowConfig window = Assert.Single(Assert.IsAssignableFrom<IReadOnlyCollection<WindowConfig>>(first));
        Assert.Equal($"kwin:{NativeId:D}", window.WindowId);
        Assert.Equal("Calculator", window.WindowTitle);
        Assert.Equal("kcalc", window.ProcessName);
        Assert.Single(Assert.IsAssignableFrom<IReadOnlyCollection<WindowConfig>>(second));
        Assert.Equal(1, resolverCalls);
        Assert.Equal(1, client.ConnectCalls);
    }

    [Fact]
    public async Task TryGetWindowsAsync_ReflectsLaterSnapshots()
    {
        var client = new FakeKWinScriptingClient
        {
            WatcherSnapshot = Snapshot(Window(NativeId, "Before", 0)),
        };
        await using KWinWaylandWindowAccessor sut = CreateSut(client, out _);
        await sut.TryGetWindowsAsync();

        await sut.BridgeObject.PushSnapshotAsync(Snapshot(Window(NativeId, "After", 0)));
        IReadOnlyCollection<WindowConfig>? windows = await sut.TryGetWindowsAsync();

        Assert.Equal("After", Assert.Single(Assert.IsAssignableFrom<IReadOnlyCollection<WindowConfig>>(windows)).WindowTitle);
    }

    [Fact]
    public async Task TryGetWindowsAsync_KeepsPreviousListWhenSnapshotIsUnreadable()
    {
        var client = new FakeKWinScriptingClient
        {
            WatcherSnapshot = Snapshot(Window(NativeId, "Kept", 0)),
        };
        await using KWinWaylandWindowAccessor sut = CreateSut(client, out CollectingSink sink);
        await sut.TryGetWindowsAsync();

        await sut.BridgeObject.PushSnapshotAsync("{ not json");
        IReadOnlyCollection<WindowConfig>? windows = await sut.TryGetWindowsAsync();

        Assert.Equal("Kept", Assert.Single(Assert.IsAssignableFrom<IReadOnlyCollection<WindowConfig>>(windows)).WindowTitle);
        Assert.True(sink.Contains(LogEventLevel.Warning, "unreadable window list"));
        Assert.DoesNotContain(sink.Events, logEvent => logEvent.RenderMessage().Contains("Kept"));
    }

    [Fact]
    public async Task TryGetWindowsAsync_ReturnsNullAndWaitsBeforeRetryingWhenKWinIsUnavailable()
    {
        var client = new FakeKWinScriptingClient { BusName = null };
        var timeProvider = new ManualTimeProvider();
        await using KWinWaylandWindowAccessor sut = CreateSut(client, out _, timeProvider: timeProvider);

        Assert.Null(await sut.TryGetWindowsAsync());
        Assert.Null(await sut.TryGetWindowsAsync());
        Assert.Equal(1, client.ConnectCalls);

        timeProvider.Advance(TimeSpan.FromSeconds(31));
        Assert.Null(await sut.TryGetWindowsAsync());
        Assert.Equal(2, client.ConnectCalls);
    }

    [Fact]
    public async Task TryActivateWindowAsync_RunsOneShotScriptAndReportsOutcome()
    {
        var client = new FakeKWinScriptingClient
        {
            WatcherSnapshot = Snapshot(Window(NativeId, "Calculator", 0)),
            ActivationResult = true,
        };
        await using KWinWaylandWindowAccessor sut = CreateSut(client, out _);

        bool activated = await sut.TryActivateWindowAsync($"kwin:{NativeId:D}");

        Assert.True(activated);
        string activationPlugin = Assert.Single(
            client.LoadedPlugins,
            name => name.StartsWith("windowswitcher_activate_", StringComparison.Ordinal)
        );
        Assert.Contains(activationPlugin, client.UnloadedPlugins);
        Assert.Contains(KWinScripts.FormatWindowId(NativeId), client.Scripts[activationPlugin]);
    }

    [Fact]
    public async Task TryActivateWindowAsync_ReturnsFalseWhenKWinDoesNotFindTheWindow()
    {
        var client = new FakeKWinScriptingClient
        {
            WatcherSnapshot = Snapshot(),
            ActivationResult = false,
        };
        await using KWinWaylandWindowAccessor sut = CreateSut(client, out _);

        Assert.False(await sut.TryActivateWindowAsync($"kwin:{NativeId:D}"));
    }

    [Theory]
    [InlineData("0x01e00007")]
    [InlineData("kwin:")]
    [InlineData("kwin:not-a-guid")]
    [InlineData("kwin:00000000-0000-0000-0000-000000000000")]
    public async Task TryActivateWindowAsync_RejectsForeignOrInvalidIdentifiers(string windowId)
    {
        var client = new FakeKWinScriptingClient { WatcherSnapshot = Snapshot() };
        await using KWinWaylandWindowAccessor sut = CreateSut(client, out _);

        Assert.False(await sut.TryActivateWindowAsync(windowId));
        Assert.Empty(client.LoadedPlugins);
    }

    [Fact]
    public async Task TryRenameWindowAsync_IsNotSupported()
    {
        var client = new FakeKWinScriptingClient { WatcherSnapshot = Snapshot() };
        await using KWinWaylandWindowAccessor sut = CreateSut(client, out CollectingSink sink);

        Assert.False(await sut.TryRenameWindowAsync($"kwin:{NativeId:D}", "Renamed"));
        Assert.True(sink.Contains(LogEventLevel.Information, "does not allow renaming"));
    }

    [Fact]
    public async Task DisposeAsync_UnloadsTheWatcherOnceStarted()
    {
        var client = new FakeKWinScriptingClient { WatcherSnapshot = Snapshot() };
        KWinWaylandWindowAccessor sut = CreateSut(client, out _);
        await sut.TryGetWindowsAsync();

        await sut.DisposeAsync();

        Assert.Contains(KWinWaylandWindowAccessor.WatcherPluginName, client.UnloadedPlugins);
        Assert.True(client.Disposed);
    }

    [Fact]
    public void TryParseSnapshot_SkipsEntriesWithoutValidIdentifier()
    {
        string json =
            $$"""[{"id":"{{KWinScripts.FormatWindowId(NativeId)}}","caption":"A","pid":5,"normal":true},{"id":"bad"},null]""";

        Assert.True(KWinWindow.TryParseSnapshot(json, out IReadOnlyList<KWinWindow> windows));

        KWinWindow window = Assert.Single(windows);
        Assert.Equal(NativeId, window.Id);
        Assert.Equal(5, window.ProcessId);
        Assert.True(window.IsListedNativeWindow);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("[")]
    public void TryParseSnapshot_RejectsPayloadsThatAreNotWindowArrays(string? json)
    {
        Assert.False(KWinWindow.TryParseSnapshot(json, out IReadOnlyList<KWinWindow> windows));
        Assert.Empty(windows);
    }

    [Fact]
    public void Scripts_EncodeInterpolatedValuesAsStringLiterals()
    {
        const string hostileBusName = ":1.42\"); workspace.activeWindow = null; (\"";

        string watcher = KWinScripts.BuildWatcher(hostileBusName);
        string activation = KWinScripts.BuildActivation(hostileBusName, NativeId, "request");

        string encoded = JsonConvert.ToString(hostileBusName);
        Assert.Contains($"const busName = {encoded};", watcher);
        Assert.Contains($"callDBus({encoded},", activation);
        Assert.Contains("\"{f66232b1-8542-48ef-8c53-a8151f0bc453}\"", activation);
        Assert.DoesNotContain("__", watcher);
        Assert.DoesNotContain("__", activation);
    }

    private static KWinWaylandWindowAccessor CreateSut(
        FakeKWinScriptingClient client,
        out CollectingSink sink,
        Func<int, CancellationToken, Task<string>>? processNameResolver = null,
        TimeProvider? timeProvider = null
    )
    {
        var sut = new KWinWaylandWindowAccessor(
            client,
            processNameResolver ?? ((_, _) => Task.FromResult(string.Empty)),
            TestLogger.Create(out sink),
            timeProvider ?? TimeProvider.System
        );
        client.Bridge = sut.BridgeObject;
        return sut;
    }

    private static string Snapshot(params object[] windows) => JsonConvert.SerializeObject(windows);

    private static object Window(
        Guid id,
        string caption,
        int processId,
        bool normal = true,
        bool skipTaskbar = false,
        bool x11 = false
    ) =>
        new
        {
            id = KWinScripts.FormatWindowId(id),
            caption,
            pid = processId,
            normal,
            skipTaskbar,
            x11,
        };

    private sealed class FakeKWinScriptingClient : IKWinScriptingClient
    {
        public IKWinWindowBridge? Bridge { get; set; }
        public string? BusName { get; init; } = ":1.42";
        public string? WatcherSnapshot { get; init; }
        public bool ActivationResult { get; init; }
        public int ConnectCalls { get; private set; }
        public bool Disposed { get; private set; }
        public List<string> LoadedPlugins { get; } = [];
        public List<string> UnloadedPlugins { get; } = [];
        public Dictionary<string, string> Scripts { get; } = [];

        public Task<string?> ConnectAsync(
            IKWinWindowBridge bridge,
            CancellationToken cancellationToken
        )
        {
            ConnectCalls++;
            return Task.FromResult(BusName);
        }

        public async Task<bool> LoadAndRunAsync(
            string pluginName,
            string script,
            CancellationToken cancellationToken
        )
        {
            LoadedPlugins.Add(pluginName);
            Scripts[pluginName] = script;
            IKWinWindowBridge bridge = Bridge ?? throw new InvalidOperationException();
            if (pluginName == KWinWaylandWindowAccessor.WatcherPluginName)
            {
                if (WatcherSnapshot is not null)
                    await bridge.PushSnapshotAsync(WatcherSnapshot);
                return true;
            }

            string requestId = pluginName["windowswitcher_activate_".Length..];
            await bridge.CompleteActivationAsync(requestId, ActivationResult);
            return true;
        }

        public Task UnloadAsync(string pluginName, CancellationToken cancellationToken)
        {
            UnloadedPlugins.Add(pluginName);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan delay) => _timestamp += delay.Ticks;
    }
}
