using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace WindowSwitcherLib.Data.WindowAccess;

public sealed class ScreenshotQueue : IDisposable, IAsyncDisposable
{
    private sealed class WindowEntry
    {
        public bool Queued;
        public bool InFlight;
        public bool Forgotten;
        public ScreenshotRequest PendingRequest;
        public TaskCompletionSource<Bitmap?>? PendingTcs;
        public TaskCompletionSource<Bitmap?>? InFlightTcs;
        public ScreenshotRequest InFlightRequest;
    }

    private readonly WindowAccessor _accessor;
    private readonly Channel<string> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;

    private readonly object _sync = new();
    private readonly Dictionary<string, WindowEntry> _entries = new(StringComparer.Ordinal);
    private bool _disposed;

    public ScreenshotQueue(WindowAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        _accessor = accessor;

        _channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        _worker = Task.Run(RunWorkerAsync);
    }

    public Task<Bitmap?> RequestAsync(string windowId, ScreenshotRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(windowId))
            return Task.FromResult<Bitmap?>(null);
        if (cancellationToken.IsCancellationRequested)
            return Task.FromResult<Bitmap?>(null);

        Task<Bitmap?> task;
        lock (_sync)
        {
            if (_disposed)
                return Task.FromResult<Bitmap?>(null);

            if (!_entries.TryGetValue(windowId, out WindowEntry? entry))
            {
                entry = new WindowEntry();
                _entries[windowId] = entry;
            }

            if (entry.Forgotten)
                entry.Forgotten = false;

            if (entry.InFlight)
            {
                if (entry.PendingTcs is not null)
                {
                    entry.PendingRequest = request;
                    task = entry.PendingTcs.Task;
                }
                else if (entry.InFlightTcs is not null && entry.InFlightRequest == request)
                {
                    task = entry.InFlightTcs.Task;
                }
                else
                {
                    entry.PendingRequest = request;
                    entry.PendingTcs ??= NewTcs();
                    task = entry.PendingTcs.Task;
                }
            }
            else
            {
                entry.PendingRequest = request;
                entry.PendingTcs ??= NewTcs();
                task = entry.PendingTcs.Task;

                if (!entry.Queued)
                {
                    entry.Queued = true;
                    _ = _channel.Writer.TryWrite(windowId);
                }
            }
        }

        return WaitOrNullAsync(task, cancellationToken);
    }

    public void ForgetWindow(string windowId)
    {
        if (string.IsNullOrWhiteSpace(windowId))
            return;

        TaskCompletionSource<Bitmap?>? pendingToCancel = null;
        lock (_sync)
        {
            if (!_entries.TryGetValue(windowId, out WindowEntry? entry))
                return;

            entry.Forgotten = true;

            pendingToCancel = entry.PendingTcs;
            entry.PendingTcs = null;
            entry.Queued = false;

            if (!entry.InFlight)
                _entries.Remove(windowId);
        }

        pendingToCancel?.TrySetResult(null);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        _cts.Cancel();
        _channel.Writer.TryComplete();
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        try { await _worker.ConfigureAwait(false); } catch { }
        _cts.Dispose();
    }

    private async Task RunWorkerAsync()
    {
        try
        {
            while (await _channel.Reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
            {
                while (_channel.Reader.TryRead(out string? windowId))
                {
                    WindowEntry? entry;
                    ScreenshotRequest request;
                    TaskCompletionSource<Bitmap?> tcs;

                    lock (_sync)
                    {
                        if (!_entries.TryGetValue(windowId, out entry))
                            continue;

                        entry.Queued = false;

                        if (entry.InFlight)
                            continue;

                        if (entry.PendingTcs is null)
                            continue;

                        entry.InFlight = true;
                        entry.InFlightRequest = entry.PendingRequest;
                        entry.InFlightTcs = entry.PendingTcs;

                        entry.PendingTcs = null;

                        request = entry.InFlightRequest;
                        tcs = entry.InFlightTcs;
                    }

                    Bitmap? bitmap = null;
                    try
                    {
                        bitmap = await _accessor
                            .TakeScreenshotAsync(windowId, request, _cts.Token)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Cancellation path.
                    }
                    catch
                    {
                        // WindowAccessor handles expected errors/logging.
                    }

                    bool shouldRequeue;
                    bool forgotten;
                    lock (_sync)
                    {
                        if (entry is not null)
                        {
                            entry.InFlight = false;
                            entry.InFlightTcs = null;
                            forgotten = entry.Forgotten;
                            shouldRequeue = !forgotten && entry.PendingTcs is not null && !entry.Queued;
                            if (shouldRequeue)
                                entry.Queued = true;

                            if (forgotten)
                                _entries.Remove(windowId);
                        }
                        else
                        {
                            shouldRequeue = false;
                            forgotten = false;
                        }
                    }

                    if (forgotten)
                    {
                        bitmap?.Dispose();
                        tcs.TrySetResult(null);
                    }
                    else
                    {
                        tcs.TrySetResult(bitmap);
                    }

                    if (shouldRequeue)
                        _ = _channel.Writer.TryWrite(windowId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown path.
        }
        finally
        {
            List<TaskCompletionSource<Bitmap?>> toCancel = new();
            lock (_sync)
            {
                foreach (WindowEntry entry in _entries.Values)
                {
                    if (entry.PendingTcs is not null)
                        toCancel.Add(entry.PendingTcs);
                    if (entry.InFlightTcs is not null)
                        toCancel.Add(entry.InFlightTcs);
                }
                _entries.Clear();
            }

            foreach (TaskCompletionSource<Bitmap?> tcs in toCancel)
                tcs.TrySetResult(null);
        }
    }

    private static TaskCompletionSource<Bitmap?> NewTcs()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task<Bitmap?> WaitOrNullAsync(Task<Bitmap?> task, CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
            return await task.ConfigureAwait(false);

        try
        {
            return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }
}
