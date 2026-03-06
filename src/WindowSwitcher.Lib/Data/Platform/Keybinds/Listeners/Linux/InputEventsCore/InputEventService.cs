using System.Threading.Channels;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Discovery;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Logging;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Models;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Options;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Reading;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Routing;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore;

/// <summary>
/// Default implementation of <see cref="IInputEventService"/> that discovers evdev devices
/// and multiplexes decoded events into a bounded asynchronous channel.
/// </summary>
public sealed class InputEventService : IInputEventService
{
    private readonly InputEventServiceOptions _options;
    private readonly InputDeviceDiscovery _discovery;
    private readonly EventDecoder _decoder;
    private readonly InputRouter _router;
    private readonly object _syncRoot = new();

    private readonly Dictionary<string, Task> _readerTasks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, InputDeviceInfo> _devicesByPath = new(StringComparer.Ordinal);

    private Channel<InputEvent> _channel;
    private CancellationTokenSource? _runCts;
    private Task? _autoDiscoverTask;
    private bool _isRunning;

    /// <summary>
    /// Creates a new service instance.
    /// </summary>
    /// <param name="options">
    /// Service options controlling discovery, filters, reconnect policy, and buffering.
    /// When <see langword="null"/>, default options are used.
    /// </param>
    public InputEventService(InputEventServiceOptions? options = null)
    {
        _options = options ?? new InputEventServiceOptions();
        _discovery = new InputDeviceDiscovery(_options.Logger);
        _decoder = new EventDecoder();
        _router = new InputRouter(_options);
        _channel = CreateChannel();
    }

    /// <inheritdoc />
    public event EventHandler<KeyEvent>? KeyDown;

    /// <inheritdoc />
    public event EventHandler<KeyEvent>? KeyUp;

    /// <inheritdoc />
    public IReadOnlyList<InputDeviceInfo> Devices
    {
        get
        {
            lock (_syncRoot)
            {
                // Expose a stable snapshot so callers never observe concurrent dictionary mutations.
                return _devicesByPath.Values.OrderBy(static d => d.Path, StringComparer.Ordinal).ToArray();
            }
        }
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken ct = default)
    {
        lock (_syncRoot)
        {
            if (_isRunning)
            {
                return;
            }

            // A fresh channel is created on every start cycle; readers from a previous run are already stopped.
            _channel = CreateChannel();
            _runCts = new CancellationTokenSource();
            _isRunning = true;
        }

        try
        {
            // Link external startup cancellation with service lifetime cancellation.
            using var startupCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _runCts!.Token);
            await RefreshReadersAsync(startupCts.Token).ConfigureAwait(false);

            if (_options.AutoDiscover)
            {
                // Periodic discovery runs in the background until StopAsync cancels the run token.
                _autoDiscoverTask = Task.Run(() => AutoDiscoverLoopAsync(_runCts.Token), _runCts.Token);
            }

            Log(InputLogLevel.Info, "InputEventService started");
        }
        catch
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken ct = default)
    {
        Task[] readerTasks;
        Task? discoverTask;
        CancellationTokenSource? runCts;

        lock (_syncRoot)
        {
            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;
            runCts = _runCts;
            discoverTask = _autoDiscoverTask;
            readerTasks = _readerTasks.Values.ToArray();

            _runCts = null;
            _autoDiscoverTask = null;
            _readerTasks.Clear();
        }

        // Signal all loops first so they can exit before we await them.
        runCts?.Cancel();

        var tasksToAwait = readerTasks;
        if (discoverTask is not null)
        {
            tasksToAwait = [.. tasksToAwait, discoverTask];
        }

        if (tasksToAwait.Length > 0)
        {
            await Task.WhenAll(tasksToAwait).WaitAsync(ct).ConfigureAwait(false);
        }

        // Complete the channel after producers are done so consumers can finish cleanly.
        _channel.Writer.TryComplete();
        runCts?.Dispose();

        Log(InputLogLevel.Info, "InputEventService stopped");
    }

    /// <inheritdoc />
    public IAsyncEnumerable<InputEvent> GetEventsAsync(CancellationToken ct = default)
    {
        return _channel.Reader.ReadAllAsync(ct);
    }

    private async Task AutoDiscoverLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Discovery may race with device hotplug; repeated scans keep the set convergent.
                await RefreshReadersAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log(InputLogLevel.Warn, "Auto-discovery failed", ex);
            }

            try
            {
                await Task.Delay(_options.DiscoveryInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task RefreshReadersAsync(CancellationToken ct)
    {
        var targetPaths = new HashSet<string>(StringComparer.Ordinal);

        if (_options.IncludeDevicePaths is { Length: > 0 })
        {
            foreach (var explicitPath in _options.IncludeDevicePaths)
            {
                if (!string.IsNullOrWhiteSpace(explicitPath))
                {
                    targetPaths.Add(explicitPath);
                }
            }
        }

        if (_options.AutoDiscover)
        {
            var discovered = await _discovery.DiscoverAsync(ct).ConfigureAwait(false);
            MergeDevices(discovered);

            foreach (var device in discovered)
            {
                targetPaths.Add(device.Path);
            }
        }
        else if (targetPaths.Count > 0)
        {
            var explicitInfos = new List<InputDeviceInfo>();
            foreach (var targetPath in targetPaths)
            {
                var info = await _discovery.ProbeAsync(targetPath, ct).ConfigureAwait(false);
                if (info is not null)
                {
                    explicitInfos.Add(info);
                }
            }

            MergeDevices(explicitInfos);
        }

        foreach (var path in targetPaths)
        {
            ct.ThrowIfCancellationRequested();
            await EnsureReaderForPathAsync(path, ct).ConfigureAwait(false);
        }
    }

    private async Task EnsureReaderForPathAsync(string path, CancellationToken ct)
    {
        lock (_syncRoot)
        {
            if (_readerTasks.ContainsKey(path))
            {
                return;
            }
        }

        var info = await GetOrProbeInfoAsync(path, ct).ConfigureAwait(false);
        if (info is null || !_router.ShouldTrackDevice(info))
        {
            return;
        }

        var reader = new EvdevReader(
            path,
            _options.ReadBufferEvents,
            _options.ReconnectOnDisconnect,
            _options.ReconnectDelay,
            _options.Logger);

        var runToken = _runCts?.Token ?? CancellationToken.None;
        var task = Task.Run(() => reader.RunAsync(HandleNativeEventAsync, runToken), runToken);

        lock (_syncRoot)
        {
            if (!_isRunning)
            {
                return;
            }

            _readerTasks[path] = task;
        }

        _ = task.ContinueWith(
            _ => RemoveReader(path),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        Log(InputLogLevel.Info, $"Reader registered: {path}");
    }

    private async Task<InputDeviceInfo?> GetOrProbeInfoAsync(string path, CancellationToken ct)
    {
        lock (_syncRoot)
        {
            if (_devicesByPath.TryGetValue(path, out var existing))
            {
                return existing;
            }
        }

        var probed = await _discovery.ProbeAsync(path, ct).ConfigureAwait(false);
        if (probed is null)
        {
            return null;
        }

        MergeDevices([probed]);
        return probed;
    }

    private ValueTask HandleNativeEventAsync(string devicePath, Linux.NativeInputEvent nativeEvent, CancellationToken ct)
    {
        var decoded = _decoder.Decode(devicePath, nativeEvent);
        if (!_router.ShouldEmit(decoded))
        {
            return ValueTask.CompletedTask;
        }

        // Prefer non-blocking write; fall back to async write only when the bounded channel is saturated.
        if (!_channel.Writer.TryWrite(decoded))
        {
            return WriteWithBackpressureAsync(decoded, ct);
        }

        EmitHighLevelEvents(decoded);
        return ValueTask.CompletedTask;
    }

    private async ValueTask WriteWithBackpressureAsync(InputEvent inputEvent, CancellationToken ct)
    {
        try
        {
            await _channel.Writer.WriteAsync(inputEvent, ct).ConfigureAwait(false);
            EmitHighLevelEvents(inputEvent);
        }
        catch (ChannelClosedException)
        {
            // Stop/teardown can close the channel while readers are still unwinding.
        }
    }

    private void EmitHighLevelEvents(InputEvent inputEvent)
    {
        if (inputEvent is not KeyEvent keyEvent)
        {
            return;
        }

        try
        {
            if (keyEvent.State == KeyState.Down)
            {
                KeyDown?.Invoke(this, keyEvent);
            }
            else if (keyEvent.State == KeyState.Up)
            {
                KeyUp?.Invoke(this, keyEvent);
            }
        }
        catch (Exception ex)
        {
            // Handlers are user code; swallow to keep device reading alive.
            Log(InputLogLevel.Warn, "Key event subscriber failed", ex);
        }
    }

    private void MergeDevices(IEnumerable<InputDeviceInfo> devices)
    {
        lock (_syncRoot)
        {
            foreach (var device in devices)
            {
                var isNew = !_devicesByPath.ContainsKey(device.Path);
                _devicesByPath[device.Path] = device;

                if (isNew)
                {
                    Log(InputLogLevel.Info,
                        $"Discovered {device.Path} kind={device.Kind} name=\"{device.Name}\" accessible={device.IsAccessible}");
                }
            }
        }
    }

    private void RemoveReader(string path)
    {
        lock (_syncRoot)
        {
            _readerTasks.Remove(path);
        }
    }

    private Channel<InputEvent> CreateChannel()
    {
        var channelOptions = new BoundedChannelOptions(Math.Max(1, _options.ChannelCapacity))
        {
            FullMode = _options.ChannelFullMode,
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        };

        // Multiple device readers publish concurrently to a single fan-in channel.
        return Channel.CreateBounded<InputEvent>(channelOptions);
    }

    private void Log(InputLogLevel level, string message, Exception? ex = null)
    {
        _options.Logger?.Invoke(level, message, ex);
    }
}
