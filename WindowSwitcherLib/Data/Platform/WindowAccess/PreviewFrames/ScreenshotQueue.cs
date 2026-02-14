using System.Threading.Channels;
using Avalonia.Media.Imaging;
using WindowSwitcherLib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcherLib.Models;

namespace WindowSwitcherLib.Data.Platform.WindowAccess.PreviewFrames;

public sealed class ScreenshotQueue : IDisposable, IAsyncDisposable
{
    private static readonly Task<Bitmap?> NullBitmapTask = Task.FromResult<Bitmap?>(null);

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

    private readonly WinAccessorBase _accessorBase;
    private readonly Channel<string> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;

    private readonly object _sync = new();
    private readonly Dictionary<string, WindowEntry> _entries = new(StringComparer.Ordinal);
    private bool _disposed;

    public ScreenshotQueue(WinAccessorBase accessorBase)
    {
        ArgumentNullException.ThrowIfNull(accessorBase);
        _accessorBase = accessorBase;

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
            return NullBitmapTask;
        if (cancellationToken.IsCancellationRequested)
            return NullBitmapTask;

        Task<Bitmap?> requestTask;
        lock (_sync)
        {
            if (_disposed)
                return NullBitmapTask;

            WindowEntry entry = GetOrCreateEntry(windowId);

            if (entry.Forgotten)
                entry.Forgotten = false;

            requestTask = QueueOrAttachRequest(windowId, request, entry);
        }

        if (!cancellationToken.CanBeCanceled)
            return requestTask;

        return WaitOrNullAsync(requestTask, cancellationToken);
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
                    if (!TryBeginInFlight(windowId, out WindowEntry? entry, out ScreenshotRequest request, out TaskCompletionSource<Bitmap?> tcs))
                        continue;

                    Bitmap? bitmap = null;
                    try
                    {
                        bitmap = await _accessorBase
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

                    FinalizeInFlight(windowId, entry!, bitmap, tcs);
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

    private WindowEntry GetOrCreateEntry(string windowId)
    {
        if (_entries.TryGetValue(windowId, out WindowEntry? entry))
            return entry;

        entry = new WindowEntry();
        _entries[windowId] = entry;
        return entry;
    }

    private Task<Bitmap?> QueueOrAttachRequest(string windowId, ScreenshotRequest request, WindowEntry entry)
    {
        if (entry.InFlight)
        {
            if (entry.PendingTcs is null &&
                entry.InFlightTcs is not null &&
                entry.InFlightRequest == request)
            {
                return entry.InFlightTcs.Task;
            }

            entry.PendingRequest = request;
            entry.PendingTcs ??= NewTcs();
            return entry.PendingTcs.Task;
        }

        entry.PendingRequest = request;
        entry.PendingTcs ??= NewTcs();

        if (!entry.Queued)
        {
            entry.Queued = true;
            _ = _channel.Writer.TryWrite(windowId);
        }

        return entry.PendingTcs.Task;
    }

    private bool TryBeginInFlight(
        string windowId,
        out WindowEntry? entry,
        out ScreenshotRequest request,
        out TaskCompletionSource<Bitmap?> tcs)
    {
        lock (_sync)
        {
            if (!_entries.TryGetValue(windowId, out entry))
            {
                request = default;
                tcs = null!;
                return false;
            }

            entry.Queued = false;
            if (entry.InFlight || entry.PendingTcs is null)
            {
                request = default;
                tcs = null!;
                return false;
            }

            entry.InFlight = true;
            entry.InFlightRequest = entry.PendingRequest;
            entry.InFlightTcs = entry.PendingTcs;
            entry.PendingTcs = null;

            request = entry.InFlightRequest;
            tcs = entry.InFlightTcs;
            return true;
        }
    }

    private void FinalizeInFlight(
        string windowId,
        WindowEntry entry,
        Bitmap? bitmap,
        TaskCompletionSource<Bitmap?> inFlightTcs)
    {
        bool forgotten;
        bool shouldRequeue;

        lock (_sync)
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

        if (forgotten)
        {
            bitmap?.Dispose();
            inFlightTcs.TrySetResult(null);
        }
        else
        {
            inFlightTcs.TrySetResult(bitmap);
        }

        if (shouldRequeue)
            _ = _channel.Writer.TryWrite(windowId);
    }

    private static TaskCompletionSource<Bitmap?> NewTcs()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task<Bitmap?> WaitOrNullAsync(Task<Bitmap?> task, CancellationToken cancellationToken)
    {
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
