using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Channels;
using Serilog;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Discovery;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Linux;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Models;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux;

/// <summary>
/// Linux implementation backed by evdev device grabs plus a uinput virtual keyboard.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class LinuxGlobalKeyboardListener : IGlobalKeyboardListener
{
    private static readonly TimeSpan DefaultReconciliationInterval = TimeSpan.FromSeconds(2);
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _deviceGate = new(1, 1);
    private readonly ILinuxInputDeviceDiscovery _discovery;
    private readonly ILinuxKeyboardForwarderFactory _forwarderFactory;
    private readonly ILogger _logger;
    private readonly TimeSpan _reconciliationInterval;
    private readonly EventDecoder _decoder = new();
    private readonly Dictionary<string, DeviceRoutingState> _routingByDevice = (
        new(StringComparer.Ordinal)
    );
    private CancellationTokenSource? _runCts;
    private CancellationTokenSource? _readerCts;
    private Channel<LinuxCaptureEvent>? _channel;
    private Task[] _readerTasks = [];
    private Task? _processorTask;
    private Task? _reconciliationTask;
    private ILinuxKeyboardForwarder? _forwarder;
    private string? _deviceSignature;
    private int _forwardingFailed;
    private bool _isDisposed;

    /// <summary>
    /// Creates the Linux evdev/uinput keyboard listener.
    /// </summary>
    public LinuxGlobalKeyboardListener()
        : this(
            new InputDeviceDiscovery(),
            new LinuxKeyboardForwarderFactory(),
            Log.ForContext<LinuxGlobalKeyboardListener>(),
            DefaultReconciliationInterval
        ) { }

    internal LinuxGlobalKeyboardListener(
        ILinuxInputDeviceDiscovery discovery,
        ILinuxKeyboardForwarderFactory forwarderFactory,
        ILogger logger,
        TimeSpan reconciliationInterval
    )
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(forwarderFactory);
        ArgumentNullException.ThrowIfNull(logger);
        if (reconciliationInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(reconciliationInterval));

        _discovery = discovery;
        _forwarderFactory = forwarderFactory;
        _logger = logger;
        _reconciliationInterval = reconciliationInterval;
    }

    /// <inheritdoc />
    public IKeyboardInputFilter? InputFilter { get; set; }

    /// <inheritdoc />
    public event EventHandler<GlobalKeyEventArgs>? KeyEvent;

    /// <inheritdoc />
    public bool IsRunning { get; private set; }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (IsRunning)
                return;

            IReadOnlyList<InputDeviceInfo> devices = await _discovery
                .DiscoverAsync(cancellationToken)
                .ConfigureAwait(false);
            EnsureKeyboardAccess(devices);
            InputDeviceInfo[] keyboardDevices = SelectKeyboardDevices(devices);

            var runCts = new CancellationTokenSource();
            _runCts = runCts;
            _channel = Channel.CreateBounded<LinuxCaptureEvent>(
                new BoundedChannelOptions(2_048)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.Wait,
                }
            );

            _processorTask = Task.Run(
                () => ProcessEventsAsync(_channel.Reader),
                CancellationToken.None
            );
            await StartDeviceGenerationAsync(keyboardDevices, runCts.Token, cancellationToken)
                .ConfigureAwait(false);
            _reconciliationTask = Task.Run(
                () => RunReconciliationLoopAsync(runCts.Token),
                CancellationToken.None
            );

            IsRunning = true;
        }
        catch
        {
            await CleanupAfterFailedStartAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsRunning)
                return;

            CancellationTokenSource? runCts = _runCts;
            Channel<LinuxCaptureEvent>? channel = _channel;
            Task? processorTask = _processorTask;
            Task? reconciliationTask = _reconciliationTask;

            IsRunning = false;

            runCts?.Cancel();
            if (reconciliationTask is not null)
                await reconciliationTask.ConfigureAwait(false);
            await StopDeviceGenerationAsync(CancellationToken.None).ConfigureAwait(false);

            channel?.Writer.TryComplete();

            if (processorTask is not null)
            {
                try
                {
                    await processorTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _logger.Error(exception, "Linux keyboard processor stopped unexpectedly");
                }
            }

            _runCts = null;
            _channel = null;
            _processorTask = null;
            _reconciliationTask = null;
            runCts?.Dispose();
            ResetRoutingState();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        try
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Linux keyboard listener shutdown failed");
        }

        KeyEvent = null;
        InputFilter = null;
        _lifecycleGate.Dispose();
        _deviceGate.Dispose();
        GC.SuppressFinalize(this);
    }

    internal static void EnsureKeyboardAccess(IReadOnlyList<InputDeviceInfo> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);

        bool hasAccessibleKeyboard = devices.Any(device =>
            device.Kind == DeviceKind.Keyboard
            && device.IsAccessible
            && !IsProjectVirtualKeyboard(device)
        );
        if (hasAccessibleKeyboard)
            return;

        InputDeviceInfo[] deniedDevices = devices.Where(device => !device.IsAccessible).ToArray();
        if (deniedDevices.Length == 0)
            return;

        throw new LinuxInputAccessException(deniedDevices.Select(device => device.Path).ToArray());
    }

    internal static InputDeviceInfo[] SelectKeyboardDevices(IReadOnlyList<InputDeviceInfo> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        return devices
            .Where(device =>
                device.Kind == DeviceKind.Keyboard
                && device.IsAccessible
                && !IsProjectVirtualKeyboard(device)
            )
            .OrderBy(device => device.Path, StringComparer.Ordinal)
            .ToArray();
    }

    internal static string BuildDeviceSignature(IReadOnlyCollection<InputDeviceInfo> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        return string.Join(
            ';',
            devices
                .OrderBy(device => device.Path, StringComparer.Ordinal)
                .Select(device =>
                    $"{device.Path}|{device.VendorId:x4}|{device.ProductId:x4}|{string.Join(',', device.Caps.EventTypes.Order())}|{string.Join(',', device.Caps.KeyCodes.Order())}"
                )
        );
    }

    private static bool IsProjectVirtualKeyboard(InputDeviceInfo device) =>
        device.VendorId == LinuxUinputKeyboardForwarder.VendorId
        && device.ProductId == LinuxUinputKeyboardForwarder.ProductId
        && string.Equals(
            device.Name,
            LinuxUinputKeyboardForwarder.DeviceName,
            StringComparison.Ordinal
        );

    private async Task RunReconciliationLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_reconciliationInterval, cancellationToken).ConfigureAwait(false);
                IReadOnlyList<InputDeviceInfo> devices = await _discovery
                    .DiscoverAsync(cancellationToken)
                    .ConfigureAwait(false);
                await ReconcileDevicesAsync(devices, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The listener is stopping: leaving the loop is the expected outcome.
                return;
            }
            catch (LinuxInputAccessException exception)
            {
                _logger.Warning(exception, "Linux evdev keyboard access was refused");
                await StopGenerationForUnavailableInputAsync().ConfigureAwait(false);
            }
            catch (LinuxUinputAccessException exception)
            {
                _logger.Warning(exception, "Linux uinput virtual keyboard access was refused");
                await StopGenerationForUnavailableInputAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Linux keyboard reconciliation failed");
            }
        }
    }

    private async Task ReconcileDevicesAsync(
        IReadOnlyList<InputDeviceInfo> devices,
        CancellationToken cancellationToken
    )
    {
        EnsureKeyboardAccess(devices);
        InputDeviceInfo[] keyboards = SelectKeyboardDevices(devices);
        string signature = BuildDeviceSignature(keyboards);

        await _deviceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (
                string.Equals(_deviceSignature, signature, StringComparison.Ordinal)
                && _readerCts is not { IsCancellationRequested: true }
            )
                return;

            await StopDeviceGenerationCoreAsync(CancellationToken.None).ConfigureAwait(false);
            await StartDeviceGenerationAsync(
                    keyboards,
                    _runCts?.Token ?? cancellationToken,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        finally
        {
            _deviceGate.Release();
        }
    }

    private async Task StopGenerationForUnavailableInputAsync()
    {
        await _deviceGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await StopDeviceGenerationCoreAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _deviceGate.Release();
        }
    }

    private async Task StartDeviceGenerationAsync(
        IReadOnlyCollection<InputDeviceInfo> keyboardDevices,
        CancellationToken runToken,
        CancellationToken startupToken
    )
    {
        _deviceSignature = BuildDeviceSignature(keyboardDevices);
        if (keyboardDevices.Count == 0)
        {
            _logger.Information("No physical Linux keyboard is currently available");
            return;
        }

        ILinuxKeyboardForwarder? forwarder = await _forwarderFactory
            .CreateAsync(keyboardDevices, startupToken)
            .ConfigureAwait(false);
        CancellationTokenSource? readerCts = CancellationTokenSource.CreateLinkedTokenSource(
            runToken
        );
        try
        {
            ChannelWriter<LinuxCaptureEvent> writer =
                _channel?.Writer
                ?? throw new InvalidOperationException(
                    "Linux keyboard event channel is unavailable."
                );
            _forwarder = forwarder;
            Volatile.Write(ref _forwardingFailed, 0);
            _readerCts = readerCts;
            CancellationTokenSource activeReaderCts = readerCts;
            _readerTasks = keyboardDevices
                .Select(device =>
                    Task.Run(
                        () =>
                            ReadDeviceLoopAsync(
                                device.Path,
                                writer,
                                activeReaderCts,
                                activeReaderCts.Token
                            ),
                        CancellationToken.None
                    )
                )
                .ToArray();
            forwarder = null;
            readerCts = null;
        }
        finally
        {
            forwarder?.Dispose();
            readerCts?.Dispose();
        }
    }

    private async Task StopDeviceGenerationAsync(CancellationToken cancellationToken)
    {
        await _deviceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopDeviceGenerationCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _deviceGate.Release();
        }
    }

    private async Task StopDeviceGenerationCoreAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? readerCts = _readerCts;
        Task[] readerTasks = _readerTasks;
        ILinuxKeyboardForwarder? forwarder = _forwarder;
        _readerCts = null;
        _readerTasks = [];
        _deviceSignature = null;

        readerCts?.Cancel();
        try
        {
            if (readerTasks.Length > 0)
            {
                try
                {
                    await Task.WhenAll(readerTasks).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    _logger.Error(exception, "Linux evdev readers stopped unexpectedly");
                }
            }
            await DrainCapturedEventsAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _forwarder = null;
            readerCts?.Dispose();
            forwarder?.Dispose();
            ResetRoutingState();
            ResetFilterState();
        }
    }

    private async Task DrainCapturedEventsAsync(CancellationToken cancellationToken)
    {
        Channel<LinuxCaptureEvent>? channel = _channel;
        if (channel is null || _processorTask is null || _processorTask.IsCompleted)
            return;

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        await channel
            .Writer.WriteAsync(LinuxCaptureEvent.CreateBarrier(completion), cancellationToken)
            .ConfigureAwait(false);
        await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ReadDeviceLoopAsync(
        string devicePath,
        ChannelWriter<LinuxCaptureEvent> writer,
        CancellationTokenSource generationCancellation,
        CancellationToken cancellationToken
    )
    {
        byte[] readBuffer = new byte[NativeInputEvent.Size * 64];
        byte[] parseBuffer = new byte[readBuffer.Length + NativeInputEvent.Size];

        while (!cancellationToken.IsCancellationRequested)
        {
            int fd = LinuxNative.OpenReadOnlyNonBlocking(devicePath);
            if (fd < 0)
            {
                int openErrno = LinuxNative.GetLastErrno();
                if (LinuxNative.IsPermissionError(openErrno))
                {
                    _logger.Warning(
                        "Reading Linux evdev device {DevicePath} was refused (errno={Errno})",
                        devicePath,
                        openErrno
                    );
                    generationCancellation.Cancel();
                    return;
                }

                await DelayReconnectAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            bool grabbed = false;
            try
            {
                if (LinuxNative.Ioctl(fd, LinuxIoctl.EviocGrab, 1) < 0)
                {
                    int grabErrno = LinuxNative.GetLastErrno();
                    if (LinuxNative.IsPermissionError(grabErrno))
                    {
                        _logger.Warning(
                            "Grabbing Linux evdev device {DevicePath} was refused (errno={Errno})",
                            devicePath,
                            grabErrno
                        );
                        generationCancellation.Cancel();
                        return;
                    }
                    throw new IOException($"EVIOCGRAB({devicePath}) failed (errno={grabErrno}).");
                }

                grabbed = true;
                int buffered = 0;

                while (!cancellationToken.IsCancellationRequested)
                {
                    nint bytesRead = LinuxNative.Read(fd, readBuffer, readBuffer.Length);
                    if (bytesRead > 0)
                    {
                        Buffer.BlockCopy(readBuffer, 0, parseBuffer, buffered, (int)bytesRead);
                        buffered += (int)bytesRead;

                        int offset = 0;
                        while (buffered - offset >= NativeInputEvent.Size)
                        {
                            NativeInputEvent nativeEvent = MemoryMarshal.Read<NativeInputEvent>(
                                parseBuffer.AsSpan(offset, NativeInputEvent.Size)
                            );
                            bool written = await WriteCaptureEventAsync(
                                    writer,
                                    LinuxCaptureEvent.CreateNative(devicePath, nativeEvent),
                                    cancellationToken
                                )
                                .ConfigureAwait(false);
                            if (!written)
                            {
                                generationCancellation.Cancel();
                                break;
                            }
                            offset += NativeInputEvent.Size;
                        }

                        if (offset > 0)
                        {
                            buffered -= offset;
                            if (buffered > 0)
                                Buffer.BlockCopy(parseBuffer, offset, parseBuffer, 0, buffered);
                        }

                        continue;
                    }

                    if (bytesRead == 0)
                        throw new DeviceDisconnectedException();

                    int errno = LinuxNative.GetLastErrno();
                    if (errno == LinuxNative.Eintr)
                        continue;
                    if (LinuxNative.IsWouldBlockError(errno))
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(5), cancellationToken)
                            .ConfigureAwait(false);
                        continue;
                    }
                    if (LinuxNative.IsDisconnectError(errno))
                        throw new DeviceDisconnectedException();

                    throw new IOException($"read({devicePath}) failed (errno={errno}).");
                }
            }
            catch (DeviceDisconnectedException)
            {
                // Unplugged or suspended devices are expected: the loop reopens them after a delay.
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The reader generation is stopping: leaving the loop is the expected outcome.
                break;
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Linux evdev reader for {DevicePath} failed", devicePath);
            }
            finally
            {
                if (grabbed)
                    _ = LinuxNative.Ioctl(fd, LinuxIoctl.EviocGrab, 0);

                _ = LinuxNative.Close(fd);
            }

            await DelayReconnectAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> WriteCaptureEventAsync(
        ChannelWriter<LinuxCaptureEvent> writer,
        LinuxCaptureEvent captureEvent,
        CancellationToken cancellationToken
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(250));
        try
        {
            await writer.WriteAsync(captureEvent, timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.Error("Linux keyboard event queue remained saturated");
            return false;
        }
        catch (ChannelClosedException)
        {
            // The listener is stopping and no longer accepts events; the reader stops writing.
            return false;
        }
    }

    private async Task ProcessEventsAsync(ChannelReader<LinuxCaptureEvent> reader)
    {
        while (await reader.WaitToReadAsync(CancellationToken.None).ConfigureAwait(false))
        {
            while (reader.TryRead(out LinuxCaptureEvent captureEvent))
            {
                if (captureEvent.BarrierCompletion is not null)
                {
                    captureEvent.BarrierCompletion.TrySetResult();
                    continue;
                }
                if (Volatile.Read(ref _forwardingFailed) != 0)
                    continue;

                try
                {
                    ProcessNativeEvent(captureEvent);
                }
                catch (LinuxUinputAccessException exception)
                {
                    _logger.Warning(exception, "Linux uinput virtual keyboard access was refused");
                    ReleaseGrabsAfterForwardingFailure();
                }
                catch (Exception exception)
                {
                    _logger.Error(exception, "Linux keyboard event processing failed");
                    ReleaseGrabsAfterForwardingFailure();
                }
            }
        }
    }

    private void ProcessNativeEvent(LinuxCaptureEvent captureEvent)
    {
        InputEvent decoded = _decoder.Decode(captureEvent.DevicePath, captureEvent.NativeEvent);
        DeviceRoutingState routingState = GetRoutingState(captureEvent.DevicePath);

        if (decoded is SyncEvent)
        {
            HandleSync(captureEvent.NativeEvent, routingState);
            return;
        }

        if (decoded is not KeyEvent keyEvent)
            return;

        if (keyEvent.State == KeyState.Unknown)
        {
            ForwardCurrentEvent(captureEvent.NativeEvent, routingState);
            return;
        }

        GlobalKeyEventArgs eventArgs = GlobalKeyboardEventMapper.FromLinux(
            keyEvent,
            keyEvent.State == KeyState.Up ? GlobalKeyState.Up : GlobalKeyState.Down,
            isRepeat: keyEvent.State == KeyState.Repeat
        );

        KeyboardFilterDecision decision = EvaluateFilter(eventArgs);
        ApplyDecision(decision, captureEvent.NativeEvent, routingState);
        Emit(eventArgs);
    }

    internal void ProcessNativeEventForTesting(string devicePath, NativeInputEvent nativeEvent)
    {
        ProcessNativeEvent(LinuxCaptureEvent.CreateNative(devicePath, nativeEvent));
    }

    private KeyboardFilterDecision EvaluateFilter(GlobalKeyEventArgs eventArgs)
    {
        IKeyboardInputFilter? filter = InputFilter;
        if (filter is null)
            return KeyboardFilterDecision.Forward();

        try
        {
            return filter.ProcessEvent(eventArgs);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Linux keyboard filter failed");
            return KeyboardFilterDecision.Forward();
        }
    }

    private void ApplyDecision(
        KeyboardFilterDecision decision,
        NativeInputEvent nativeEvent,
        DeviceRoutingState routingState
    )
    {
        if (decision.Routing == KeyboardEventRouting.Forward)
            ForwardCurrentEvent(nativeEvent, routingState);
    }

    private void ForwardCurrentEvent(NativeInputEvent nativeEvent, DeviceRoutingState routingState)
    {
        _forwarder?.Forward(nativeEvent);
        routingState.ShouldForwardSync = true;
    }

    private void HandleSync(NativeInputEvent nativeEvent, DeviceRoutingState routingState)
    {
        // A SYN_REPORT is only meaningful when at least one event of its frame reached the virtual keyboard.
        if (routingState.ShouldForwardSync)
            _forwarder?.Forward(nativeEvent);

        routingState.ShouldForwardSync = false;
    }

    private void Emit(GlobalKeyEventArgs eventArgs)
    {
        Delegate[] subscribers = KeyEvent?.GetInvocationList() ?? [];
        foreach (Delegate subscriber in subscribers)
        {
            try
            {
                ((EventHandler<GlobalKeyEventArgs>)subscriber).Invoke(this, eventArgs);
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Linux keyboard event subscriber failed");
            }
        }
    }

    private void ResetRoutingState()
    {
        _routingByDevice.Clear();
        Volatile.Write(ref _forwardingFailed, 0);
    }

    private void ResetFilterState()
    {
        // Key releases that happened while no reader was active are lost, so the filter must forget its key state.
        try
        {
            InputFilter?.Reset();
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Linux keyboard filter reset failed");
        }
    }

    private DeviceRoutingState GetRoutingState(string devicePath)
    {
        if (!_routingByDevice.TryGetValue(devicePath, out DeviceRoutingState? state))
        {
            state = new DeviceRoutingState();
            _routingByDevice[devicePath] = state;
        }
        return state;
    }

    private void ReleaseGrabsAfterForwardingFailure()
    {
        Volatile.Write(ref _forwardingFailed, 1);
        try
        {
            _readerCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The reader generation was already torn down, so its grabs are already released.
        }
    }

    private static async Task DelayReconnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during shutdown: the caller observes the cancellation on its loop check.
        }
    }

    private async Task CleanupAfterFailedStartAsync()
    {
        CancellationTokenSource? runCts = _runCts;
        Channel<LinuxCaptureEvent>? channel = _channel;
        Task? processorTask = _processorTask;
        Task? reconciliationTask = _reconciliationTask;
        IsRunning = false;

        try
        {
            runCts?.Cancel();
            if (reconciliationTask is not null)
                await reconciliationTask.ConfigureAwait(false);
            await StopDeviceGenerationAsync(CancellationToken.None).ConfigureAwait(false);
            channel?.Writer.TryComplete();
            if (processorTask is not null)
                await processorTask.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Linux keyboard startup cleanup failed");
        }
        finally
        {
            _runCts = null;
            _channel = null;
            _processorTask = null;
            _reconciliationTask = null;
            runCts?.Dispose();
            ResetRoutingState();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(LinuxGlobalKeyboardListener));
    }

    private readonly record struct LinuxCaptureEvent
    {
        private LinuxCaptureEvent(
            string devicePath,
            NativeInputEvent nativeEvent,
            TaskCompletionSource? barrierCompletion
        )
        {
            DevicePath = devicePath;
            NativeEvent = nativeEvent;
            BarrierCompletion = barrierCompletion;
        }

        internal string DevicePath { get; }
        internal NativeInputEvent NativeEvent { get; }
        internal TaskCompletionSource? BarrierCompletion { get; }

        internal static LinuxCaptureEvent CreateNative(
            string devicePath,
            NativeInputEvent nativeEvent
        ) => new(devicePath, nativeEvent, null);

        internal static LinuxCaptureEvent CreateBarrier(TaskCompletionSource completion) =>
            new(string.Empty, default, completion);
    }

    private sealed class DeviceRoutingState
    {
        internal bool ShouldForwardSync { get; set; }
    }

    private sealed class DeviceDisconnectedException : Exception;
}
