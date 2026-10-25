using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Serilog;
using Tmds.DBus;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.KWin.Abstractions;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.KWin;

/// <summary>
/// Lists and activates native Wayland windows on KDE Plasma through KWin scripts.
/// XWayland windows are left to the EWMH accessor.
/// </summary>
internal sealed class KWinWaylandWindowAccessor : WinAccessorBase, IAsyncDisposable
{
    internal const string WindowIdPrefix = "kwin:";

    // A fixed name lets the next start unload a watcher left behind by a crashed instance.
    internal const string WatcherPluginName = "windowswitcher_window_watcher";
    private static readonly TimeSpan FirstSnapshotTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ActivationTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StartRetryDelay = TimeSpan.FromSeconds(30);

    private readonly IKWinScriptingClient _client;
    private readonly Func<int, CancellationToken, Task<string>> _processNameResolver;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Bridge _bridge;
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly ConcurrentDictionary<int, string> _processNames = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pendingActivations =
        new(StringComparer.Ordinal);
    private readonly TaskCompletionSource _firstSnapshot = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private IReadOnlyList<KWinWindow> _windows = [];
    private volatile string? _busName;
    private long? _lastStartFailureTimestamp;
    private volatile bool _disposed;

    public KWinWaylandWindowAccessor()
        : this(
            new KWinScriptingClient(Log.ForContext<KWinScriptingClient>()),
            LinuxProcessNameReader.ReadAsync,
            Log.ForContext<KWinWaylandWindowAccessor>(),
            TimeProvider.System
        ) { }

    internal KWinWaylandWindowAccessor(
        IKWinScriptingClient client,
        Func<int, CancellationToken, Task<string>> processNameResolver,
        ILogger logger,
        TimeProvider timeProvider
    )
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(processNameResolver);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _client = client;
        _processNameResolver = processNameResolver;
        _logger = logger;
        _timeProvider = timeProvider;
        _bridge = new Bridge(this);
    }

    /// <summary>Gets the D-Bus object the KWin scripts call back.</summary>
    internal IKWinWindowBridge BridgeObject => _bridge;

    public override async Task<IReadOnlyCollection<WindowConfig>?> TryGetWindowsAsync(
        CancellationToken cancellationToken = default
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (await EnsureStartedAsync(cancellationToken).ConfigureAwait(false) is null)
            return null;

        int currentProcessId = Environment.ProcessId;
        KWinWindow[] windows = Volatile
            .Read(ref _windows)
            .Where(window => window.IsListedNativeWindow && window.ProcessId != currentProcessId)
            .ToArray();

        int[] activeProcessIds = windows
            .Select(window => window.ProcessId)
            .Where(processId => processId > 0)
            .Distinct()
            .ToArray();
        foreach (int staleProcessId in _processNames.Keys.Except(activeProcessIds).ToArray())
            _processNames.TryRemove(staleProcessId, out _);
        foreach (int processId in activeProcessIds)
        {
            if (_processNames.ContainsKey(processId))
                continue;

            _processNames[processId] = await _processNameResolver(processId, cancellationToken)
                .ConfigureAwait(false);
        }

        return windows
            .Select(window => new WindowConfig
            {
                WindowId = FormatWindowId(window.Id),
                WindowTitle = window.Caption,
                ProcessName = _processNames.GetValueOrDefault(window.ProcessId, string.Empty),
            })
            .ToArray();
    }

    public override async Task<bool> TryActivateWindowAsync(
        string windowId,
        CancellationToken cancellationToken = default
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!TryParseWindowId(windowId, out Guid nativeWindowId))
            return false;

        string? busName = await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        if (busName is null)
            return false;

        string requestId = Guid.NewGuid().ToString("N");
        string pluginName = $"windowswitcher_activate_{requestId}";
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _pendingActivations[requestId] = completion;
        try
        {
            string script = KWinScripts.BuildActivation(busName, nativeWindowId, requestId);
            if (
                !await _client
                    .LoadAndRunAsync(pluginName, script, cancellationToken)
                    .ConfigureAwait(false)
            )
                return false;

            return await completion
                .Task.WaitAsync(ActivationTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.Warning("KWin activation script did not report back in time");
            return false;
        }
        finally
        {
            _pendingActivations.TryRemove(requestId, out _);
            await _client.UnloadAsync(pluginName, CancellationToken.None).ConfigureAwait(false);
        }
    }

    public override Task<bool> TryRenameWindowAsync(
        string windowId,
        string windowTitle,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(windowTitle);
        _logger.Information("KWin does not allow renaming native Wayland windows");
        return Task.FromResult(false);
    }

    public override void Dispose()
    {
        // Unloading the watcher needs an asynchronous D-Bus call; without it the watcher stays
        // loaded until the next start replaces it under the same plugin name.
        if (_disposed)
            return;

        _disposed = true;
        _ = _client.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_busName is not null)
            await _client
                .UnloadAsync(WatcherPluginName, CancellationToken.None)
                .ConfigureAwait(false);
        await _client.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Gets whether a window identifier belongs to this accessor.</summary>
    internal static bool IsKWinWindowId([NotNullWhen(true)] string? windowId) =>
        windowId?.StartsWith(WindowIdPrefix, StringComparison.Ordinal) == true;

    internal static string FormatWindowId(Guid windowId) => $"{WindowIdPrefix}{windowId:D}";

    internal static bool TryParseWindowId(string? windowId, out Guid nativeWindowId)
    {
        nativeWindowId = Guid.Empty;
        return IsKWinWindowId(windowId)
            && Guid.TryParse(windowId.AsSpan(WindowIdPrefix.Length), out nativeWindowId)
            && nativeWindowId != Guid.Empty;
    }

    private async Task<string?> EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (_busName is { } startedBusName)
            return startedBusName;

        await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_busName is { } busNameAfterWait)
                return busNameAfterWait;
            if (
                _lastStartFailureTimestamp is { } failureTimestamp
                && _timeProvider.GetElapsedTime(failureTimestamp) < StartRetryDelay
            )
                return null;

            string? busName = await StartWatcherAsync(cancellationToken).ConfigureAwait(false);
            if (busName is null)
            {
                _lastStartFailureTimestamp = _timeProvider.GetTimestamp();
                return null;
            }

            _lastStartFailureTimestamp = null;
            _busName = busName;
            _logger.Information("KWin window watcher started");
            return busName;
        }
        finally
        {
            _startGate.Release();
        }
    }

    private async Task<string?> StartWatcherAsync(CancellationToken cancellationToken)
    {
        string? busName = await _client
            .ConnectAsync(_bridge, cancellationToken)
            .ConfigureAwait(false);
        if (busName is null)
            return null;

        bool started = await _client
            .LoadAndRunAsync(WatcherPluginName, KWinScripts.BuildWatcher(busName), cancellationToken)
            .ConfigureAwait(false);
        if (!started)
            return null;

        try
        {
            await _firstSnapshot
                .Task.WaitAsync(FirstSnapshotTimeout, cancellationToken)
                .ConfigureAwait(false);
            return busName;
        }
        catch (TimeoutException)
        {
            _logger.Warning("KWin window watcher did not report any window list in time");
            await _client
                .UnloadAsync(WatcherPluginName, CancellationToken.None)
                .ConfigureAwait(false);
            return null;
        }
    }

    private void OnSnapshot(string snapshotJson)
    {
        if (_disposed)
            return;

        if (!KWinWindow.TryParseSnapshot(snapshotJson, out IReadOnlyList<KWinWindow> windows))
        {
            _logger.Warning("KWin window watcher sent an unreadable window list");
            return;
        }

        Volatile.Write(ref _windows, windows);
        _firstSnapshot.TrySetResult();
    }

    private void OnActivationCompleted(string requestId, bool activated)
    {
        if (_pendingActivations.TryGetValue(requestId, out TaskCompletionSource<bool>? completion))
            completion.TrySetResult(activated);
    }

    private sealed class Bridge(KWinWaylandWindowAccessor owner) : IKWinWindowBridge
    {
        public ObjectPath ObjectPath => KWinScripts.BridgePath;

        public Task PushSnapshotAsync(string snapshotJson)
        {
            owner.OnSnapshot(snapshotJson);
            return Task.CompletedTask;
        }

        public Task CompleteActivationAsync(string requestId, bool activated)
        {
            owner.OnActivationCompleted(requestId, activated);
            return Task.CompletedTask;
        }
    }
}
