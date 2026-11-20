using System;
using System.Threading;
using System.Threading.Tasks;

namespace WindowSwitcher.Windows.Services.Preview;

/// <summary>
/// Runs a refresh once a burst of requests has been quiet for the given delay, as during an
/// interactive resize. The pending refresh can also be run at once (pointer released) or dropped.
/// </summary>
internal sealed class DebouncedRefresh
{
    private readonly Lock _sync = new();
    private readonly TimeSpan _delay;
    private readonly Action _refresh;
    private readonly Func<bool> _isClosing;
    private CancellationTokenSource? _cancellation;
    private bool _pending;

    /// <param name="delay">Quiet period before the refresh runs.</param>
    /// <param name="refresh">The refresh to run.</param>
    /// <param name="isClosing">When true, a pending refresh is dropped instead of run.</param>
    public DebouncedRefresh(TimeSpan delay, Action refresh, Func<bool> isClosing)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(refresh);
        ArgumentNullException.ThrowIfNull(isClosing);
        _delay = delay;
        _refresh = refresh;
        _isClosing = isClosing;
    }

    /// <summary>
    /// Requests a refresh, restarting the quiet period.
    /// </summary>
    public void Schedule()
    {
        var cancellation = new CancellationTokenSource();
        CancellationTokenSource? previous;
        lock (_sync)
        {
            previous = _cancellation;
            _cancellation = cancellation;
            _pending = true;
        }
        CancelQuietly(previous);
        _ = RunAfterDelayAsync(cancellation);
    }

    /// <summary>
    /// Runs the pending refresh now, if any.
    /// </summary>
    public void Flush() => Stop(runPendingRefresh: true);

    /// <summary>
    /// Drops the pending refresh, if any.
    /// </summary>
    public void Cancel() => Stop(runPendingRefresh: false);

    private void Stop(bool runPendingRefresh)
    {
        CancellationTokenSource? cancellation;
        bool pending;
        lock (_sync)
        {
            cancellation = _cancellation;
            _cancellation = null;
            pending = _pending;
            _pending = false;
        }
        CancelQuietly(cancellation);
        if (runPendingRefresh && pending && !_isClosing())
            _refresh();
    }

    private static void CancelQuietly(CancellationTokenSource? cancellation)
    {
        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The superseded run already finished and disposed its source: nothing to cancel.
        }
    }

    private async Task RunAfterDelayAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(_delay, cancellation.Token).ConfigureAwait(false);
            if (TryConsume(cancellation))
                _refresh();
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer request, flushed or cancelled: this run is dropped.
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private bool TryConsume(CancellationTokenSource cancellation)
    {
        lock (_sync)
        {
            if (!ReferenceEquals(_cancellation, cancellation))
                return false;
            _cancellation = null;
            bool run = _pending && !_isClosing();
            _pending = false;
            return run;
        }
    }
}
