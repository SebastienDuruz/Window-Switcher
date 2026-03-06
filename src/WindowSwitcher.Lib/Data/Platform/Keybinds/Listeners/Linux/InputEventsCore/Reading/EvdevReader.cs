using System.Runtime.InteropServices;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Linux;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Logging;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Reading;

/// <summary>
/// Reads native <c>input_event</c> frames from one evdev node and forwards them to a callback.
/// </summary>
/// <remarks>
/// This reader is Linux-specific and depends on non-blocking <c>open/read</c> semantics.
/// </remarks>
internal sealed class EvdevReader
{
    private readonly string _devicePath;
    private readonly int _readBufferEvents;
    private readonly bool _reconnectOnDisconnect;
    private readonly TimeSpan _reconnectDelay;
    private readonly InputLogHandler? _log;

    /// <summary>
    /// Creates a reader bound to a specific evdev path.
    /// </summary>
    /// <param name="devicePath">Absolute device path (for example <c>/dev/input/event5</c>).</param>
    /// <param name="readBufferEvents">Target number of native events to request per read.</param>
    /// <param name="reconnectOnDisconnect">Whether to reopen the device after disconnect/read failures.</param>
    /// <param name="reconnectDelay">Delay between reconnect attempts.</param>
    /// <param name="logger">Optional log callback.</param>
    public EvdevReader(
        string devicePath,
        int readBufferEvents,
        bool reconnectOnDisconnect,
        TimeSpan reconnectDelay,
        InputLogHandler? logger)
    {
        _devicePath = devicePath;
        _readBufferEvents = Math.Max(1, readBufferEvents);
        _reconnectOnDisconnect = reconnectOnDisconnect;
        _reconnectDelay = reconnectDelay <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(500) : reconnectDelay;
        _log = logger;
    }

    /// <summary>
    /// Starts the read/reconnect loop until cancellation is requested or reconnect policy stops retries.
    /// </summary>
    /// <param name="onInputEvent">Callback invoked for each parsed native input event.</param>
    /// <param name="ct">Cancellation token that stops reading and reconnect attempts.</param>
    /// <returns>A task that completes when the reader exits.</returns>
    public async Task RunAsync(
        Func<string, NativeInputEvent, CancellationToken, ValueTask> onInputEvent,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var fd = LinuxNative.OpenReadOnlyNonBlocking(_devicePath);
            if (fd < 0)
            {
                var openErrno = LinuxNative.GetLastErrno();
                _log?.Invoke(InputLogLevel.Warn, $"Failed to open {_devicePath} (errno={openErrno})", null);

                if (!_reconnectOnDisconnect || LinuxNative.IsPermissionError(openErrno))
                {
                    return;
                }

                await DelayReconnectAsync(ct).ConfigureAwait(false);
                continue;
            }

            _log?.Invoke(InputLogLevel.Info, $"Reader started for {_devicePath}", null);

            try
            {
                await ReadLoopAsync(fd, onInputEvent, ct).ConfigureAwait(false);
            }
            catch (DeviceDisconnectedException)
            {
                _log?.Invoke(InputLogLevel.Warn, $"Device disconnected: {_devicePath}", null);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log?.Invoke(InputLogLevel.Warn, $"Reader failed for {_devicePath}", ex);

                if (!_reconnectOnDisconnect)
                {
                    return;
                }
            }
            finally
            {
                _ = LinuxNative.Close(fd);
                _log?.Invoke(InputLogLevel.Info, $"Reader stopped for {_devicePath}", null);
            }

            if (!_reconnectOnDisconnect)
            {
                return;
            }

            await DelayReconnectAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task ReadLoopAsync(
        int fd,
        Func<string, NativeInputEvent, CancellationToken, ValueTask> onInputEvent,
        CancellationToken ct)
    {
        var eventSize = NativeInputEvent.Size;
        var readBuffer = new byte[eventSize * _readBufferEvents];
        var parseBuffer = new byte[readBuffer.Length + eventSize];
        var buffered = 0;

        while (!ct.IsCancellationRequested)
        {
            var bytesRead = LinuxNative.Read(fd, readBuffer, readBuffer.Length);

            if (bytesRead > 0)
            {
                // Keep incomplete trailing bytes between reads to preserve frame boundaries.
                Buffer.BlockCopy(readBuffer, 0, parseBuffer, buffered, (int)bytesRead);
                buffered += (int)bytesRead;

                var offset = 0;
                while (buffered - offset >= eventSize)
                {
                    var native = MemoryMarshal.Read<NativeInputEvent>(parseBuffer.AsSpan(offset, eventSize));
                    await onInputEvent(_devicePath, native, ct).ConfigureAwait(false);
                    offset += eventSize;
                }

                if (offset > 0)
                {
                    buffered -= offset;
                    if (buffered > 0)
                    {
                        Buffer.BlockCopy(parseBuffer, offset, parseBuffer, 0, buffered);
                    }
                }

                continue;
            }

            if (bytesRead == 0)
            {
                throw new DeviceDisconnectedException();
            }

            var errno = LinuxNative.GetLastErrno();

            if (errno == LinuxNative.Eintr)
            {
                continue;
            }

            if (LinuxNative.IsWouldBlockError(errno))
            {
                // Non-blocking fd: briefly back off instead of spinning at 100% CPU.
                await Task.Delay(TimeSpan.FromMilliseconds(5), ct).ConfigureAwait(false);
                continue;
            }

            if (LinuxNative.IsDisconnectError(errno))
            {
                throw new DeviceDisconnectedException();
            }

            throw new IOException($"read({_devicePath}) failed (errno={errno})");
        }
    }

    private async Task DelayReconnectAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(_reconnectDelay, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected during shutdown.
        }
    }

    private sealed class DeviceDisconnectedException : Exception;
}
