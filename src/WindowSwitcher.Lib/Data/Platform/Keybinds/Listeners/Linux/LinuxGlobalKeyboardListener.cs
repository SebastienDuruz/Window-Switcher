using System.Runtime.InteropServices;
using System.Threading.Channels;
using System.Runtime.Versioning;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Diagnostics;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Discovery;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Linux;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Logging;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Models;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux;

/// <summary>
/// Linux implementation backed by evdev device grabs plus a uinput virtual keyboard.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxGlobalKeyboardListener : IGlobalKeyboardListener
{
    private const string PermissionDiagnostic =
        "Reading /dev/input/event* requires elevated access. Use root, add the user to the input group, or configure udev rules.";
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly InputDeviceDiscovery _discovery = new(HandleInputServiceLog);
    private readonly EventDecoder _decoder = new();
    private readonly List<NativeInputEvent> _bufferedEvents = [];
    private CancellationTokenSource? _runCts;
    private Channel<LinuxCaptureEvent>? _channel;
    private Task[] _readerTasks = [];
    private Task? _processorTask;
    private LinuxUinputKeyboardForwarder? _forwarder;
    private bool _currentFrameHasBufferedEvents;
    private bool _currentFrameShouldForwardSync;
    private bool _isDisposed;

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

            InputDeviceInfo[] keyboardDevices = devices
                .Where(device => device.Kind == DeviceKind.Keyboard && device.IsAccessible)
                .ToArray();

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

            if (keyboardDevices.Length > 0)
                _forwarder = new LinuxUinputKeyboardForwarder(keyboardDevices);

            _processorTask = Task.Run(
                () => ProcessEventsAsync(_channel.Reader),
                CancellationToken.None
            );
            _readerTasks = keyboardDevices
                .Select(device =>
                    Task.Run(
                        () => ReadDeviceLoopAsync(device.Path, _channel.Writer, runCts.Token),
                        CancellationToken.None
                    )
                )
                .ToArray();

            IsRunning = true;
            GlobalKeyboardTrace.Info("Linux global keyboard listener started.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await CleanupAfterFailedStartAsync().ConfigureAwait(false);
            GlobalKeyboardTrace.Error("Linux global keyboard listener failed to start.", ex);
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
            Task[] readerTasks = _readerTasks;
            Task? processorTask = _processorTask;
            LinuxUinputKeyboardForwarder? forwarder = _forwarder;

            _runCts = null;
            _channel = null;
            _readerTasks = [];
            _processorTask = null;
            _forwarder = null;
            IsRunning = false;

            runCts?.Cancel();

            if (readerTasks.Length > 0)
                await Task.WhenAll(readerTasks).ConfigureAwait(false);

            channel?.Writer.TryComplete();

            if (processorTask is not null)
                await processorTask.WaitAsync(cancellationToken).ConfigureAwait(false);

            runCts?.Dispose();
            forwarder?.Dispose();
            ResetBufferedState();

            GlobalKeyboardTrace.Info("Linux global keyboard listener stopped.");
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
        catch (Exception ex)
        {
            GlobalKeyboardTrace.Warning(
                $"Linux global keyboard listener shutdown reported a non-fatal error: {ex.Message}"
            );
        }

        KeyEvent = null;
        InputFilter = null;
        _lifecycleGate.Dispose();
        GC.SuppressFinalize(this);
    }

    internal static void EnsureKeyboardAccess(IReadOnlyList<InputDeviceInfo> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);

        bool hasAccessibleKeyboard = devices.Any(device =>
            device.Kind == DeviceKind.Keyboard && device.IsAccessible
        );
        if (hasAccessibleKeyboard)
            return;

        InputDeviceInfo[] deniedDevices = devices.Where(device => !device.IsAccessible).ToArray();
        if (deniedDevices.Length == 0)
        {
            GlobalKeyboardTrace.Warning(
                "Linux global keyboard listener started but no keyboard device was discovered."
            );
            return;
        }

        string paths = string.Join(", ", deniedDevices.Select(device => device.Path));
        throw new InvalidOperationException(
            $"Global keyboard startup failed on Linux due to inaccessible input devices ({paths}). {PermissionDiagnostic}"
        );
    }

    private async Task ReadDeviceLoopAsync(
        string devicePath,
        ChannelWriter<LinuxCaptureEvent> writer,
        CancellationToken cancellationToken)
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
                    GlobalKeyboardTrace.Warning(
                        $"Failed to open {devicePath} for exclusive keyboard capture (errno={openErrno})."
                    );
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
                            await writer
                                .WriteAsync(new LinuxCaptureEvent(devicePath, nativeEvent), cancellationToken)
                                .ConfigureAwait(false);
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
                GlobalKeyboardTrace.Warning($"Linux keyboard device disconnected: {devicePath}");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                GlobalKeyboardTrace.Warning(
                    $"Linux keyboard reader failed for {devicePath}: {ex.Message}"
                );
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

    private async Task ProcessEventsAsync(ChannelReader<LinuxCaptureEvent> reader)
    {
        while (await reader.WaitToReadAsync(CancellationToken.None).ConfigureAwait(false))
        {
            while (reader.TryRead(out LinuxCaptureEvent captureEvent))
            {
                ProcessNativeEvent(captureEvent);
            }
        }
    }

    private void ProcessNativeEvent(LinuxCaptureEvent captureEvent)
    {
        InputEvent decoded = _decoder.Decode(captureEvent.DevicePath, captureEvent.NativeEvent);

        if (decoded is SyncEvent)
        {
            HandleSync(captureEvent.NativeEvent);
            return;
        }

        if (decoded is not KeyEvent keyEvent)
            return;

        if (keyEvent.State == KeyState.Unknown)
        {
            ForwardCurrentEvent(captureEvent.NativeEvent);
            return;
        }

        GlobalKeyEventArgs eventArgs = GlobalKeyboardEventMapper.FromLinux(
            keyEvent,
            keyEvent.State == KeyState.Up ? GlobalKeyState.Up : GlobalKeyState.Down,
            isRepeat: keyEvent.State == KeyState.Repeat
        );

        KeyboardFilterDecision decision = EvaluateFilter(eventArgs);
        ApplyDecision(decision, captureEvent.NativeEvent);
        Emit(eventArgs);
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
        catch (Exception ex)
        {
            GlobalKeyboardTrace.Warning($"Linux keyboard filter failed: {ex.Message}");
            return KeyboardFilterDecision.Forward();
        }
    }

    private void ApplyDecision(KeyboardFilterDecision decision, NativeInputEvent nativeEvent)
    {
        if (decision.DiscardBufferedEvents)
        {
            _bufferedEvents.Clear();
            _currentFrameHasBufferedEvents = false;
        }

        if (decision.FlushBufferedEvents)
        {
            if (_bufferedEvents.Count > 0)
                _forwarder?.Forward(_bufferedEvents);

            _bufferedEvents.Clear();
            _currentFrameHasBufferedEvents = false;
        }

        switch (decision.Routing)
        {
            case KeyboardEventRouting.Buffer:
                _bufferedEvents.Add(nativeEvent);
                _currentFrameHasBufferedEvents = true;
                break;

            case KeyboardEventRouting.Consume:
                break;

            case KeyboardEventRouting.Forward:
                ForwardCurrentEvent(nativeEvent);
                break;
        }
    }

    private void ForwardCurrentEvent(NativeInputEvent nativeEvent)
    {
        _forwarder?.Forward(nativeEvent);
        _currentFrameShouldForwardSync = true;
    }

    private void HandleSync(NativeInputEvent nativeEvent)
    {
        if (_currentFrameHasBufferedEvents)
            _bufferedEvents.Add(nativeEvent);

        if (_currentFrameShouldForwardSync)
            _forwarder?.Forward(nativeEvent);

        _currentFrameHasBufferedEvents = false;
        _currentFrameShouldForwardSync = false;
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
            catch (Exception ex)
            {
                GlobalKeyboardTrace.Warning(
                    $"Global keyboard subscriber failed for key {eventArgs.KeyCode}: {ex.Message}"
                );
            }
        }
    }

    private void ResetBufferedState()
    {
        _bufferedEvents.Clear();
        _currentFrameHasBufferedEvents = false;
        _currentFrameShouldForwardSync = false;
    }

    private static async Task DelayReconnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during shutdown.
        }
    }

    private static void HandleInputServiceLog(
        InputLogLevel level,
        string message,
        Exception? exception)
    {
        string logMessage = $"Linux InputEvents.Core: {message}";
        switch (level)
        {
            case InputLogLevel.Debug:
                GlobalKeyboardTrace.Debug(logMessage);
                break;
            case InputLogLevel.Info:
                GlobalKeyboardTrace.Info(logMessage);
                break;
            case InputLogLevel.Warn:
                GlobalKeyboardTrace.Warning(logMessage);
                break;
            case InputLogLevel.Error:
                GlobalKeyboardTrace.Error(logMessage, exception);
                return;
            default:
                GlobalKeyboardTrace.Debug(logMessage);
                break;
        }

        if (exception is not null && level >= InputLogLevel.Warn)
            GlobalKeyboardTrace.Error(logMessage, exception);
    }

    private async Task CleanupAfterFailedStartAsync()
    {
        CancellationTokenSource? runCts = _runCts;
        Channel<LinuxCaptureEvent>? channel = _channel;
        Task[] readerTasks = _readerTasks;
        Task? processorTask = _processorTask;
        LinuxUinputKeyboardForwarder? forwarder = _forwarder;

        _runCts = null;
        _channel = null;
        _readerTasks = [];
        _processorTask = null;
        _forwarder = null;
        IsRunning = false;

        try
        {
            runCts?.Cancel();
            if (readerTasks.Length > 0)
                await Task.WhenAll(readerTasks).ConfigureAwait(false);

            channel?.Writer.TryComplete();
            if (processorTask is not null)
                await processorTask.ConfigureAwait(false);
        }
        catch (Exception cleanupException)
        {
            GlobalKeyboardTrace.Warning(
                $"Linux global keyboard listener cleanup reported a non-fatal error: {cleanupException.Message}"
            );
        }
        finally
        {
            runCts?.Dispose();
            forwarder?.Dispose();
            ResetBufferedState();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(LinuxGlobalKeyboardListener));
    }

    private readonly record struct LinuxCaptureEvent(string DevicePath, NativeInputEvent NativeEvent);

    private sealed class DeviceDisconnectedException : Exception;
}
