using System;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;

namespace WindowSwitcher.Windows.Services.Preview;

/// <summary>
/// Keeps only the most recent preview frame and displays frames one at a time. A frame that is
/// replaced or rejected before display is disposed at once, so native buffers return to the
/// capture backend instead of piling up behind a slow UI thread.
/// </summary>
internal sealed class LatestPreviewFrameQueue
{
    private readonly Lock _sync = new();
    private readonly Func<PreviewFrame, CancellationToken, Task> _applyFrame;
    private readonly Func<bool> _acceptsFrames;
    private readonly Func<bool> _isClosing;
    private readonly ILogger _logger;
    private PreviewFrame? _pending;
    private int _drainScheduled;

    /// <param name="applyFrame">Displays a frame and takes ownership of it.</param>
    /// <param name="acceptsFrames">False while previews are disabled or the window is closing.</param>
    /// <param name="isClosing">True once the window is closing.</param>
    /// <param name="logger">Receives display failures.</param>
    public LatestPreviewFrameQueue(
        Func<PreviewFrame, CancellationToken, Task> applyFrame,
        Func<bool> acceptsFrames,
        Func<bool> isClosing,
        ILogger logger
    )
    {
        ArgumentNullException.ThrowIfNull(applyFrame);
        ArgumentNullException.ThrowIfNull(acceptsFrames);
        ArgumentNullException.ThrowIfNull(isClosing);
        ArgumentNullException.ThrowIfNull(logger);
        _applyFrame = applyFrame;
        _acceptsFrames = acceptsFrames;
        _isClosing = isClosing;
        _logger = logger;
    }

    /// <summary>
    /// Queues <paramref name="frame"/>, replacing a frame that is still waiting for display.
    /// </summary>
    public void Enqueue(PreviewFrame frame, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        PreviewFrame? disposeNow;
        bool scheduleDrain = false;
        lock (_sync)
        {
            if (cancellationToken.IsCancellationRequested || !_acceptsFrames())
            {
                disposeNow = frame;
            }
            else
            {
                disposeNow = _pending;
                _pending = frame;
                scheduleDrain = Interlocked.CompareExchange(ref _drainScheduled, 1, 0) == 0;
            }
        }

        disposeNow?.Dispose();
        if (scheduleDrain)
            _ = DrainAsync(cancellationToken);
    }

    /// <summary>
    /// Disposes the frame waiting for display, if any.
    /// </summary>
    public void DiscardPending()
    {
        PreviewFrame? frame;
        lock (_sync)
        {
            frame = _pending;
            _pending = null;
        }
        frame?.Dispose();
    }

    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                PreviewFrame? frame;
                lock (_sync)
                {
                    frame = _pending;
                    _pending = null;
                }
                if (frame is null)
                    break;
                await _applyFrame(frame, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Preview operation cancelled: the pending frame is released below.
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Preview frame could not be displayed");
        }
        finally
        {
            Interlocked.Exchange(ref _drainScheduled, 0);
            bool scheduleDrain = false;
            PreviewFrame? disposeNow = null;
            lock (_sync)
            {
                if (_isClosing() || cancellationToken.IsCancellationRequested)
                {
                    disposeNow = _pending;
                    _pending = null;
                }
                else if (_pending is not null)
                {
                    scheduleDrain = Interlocked.CompareExchange(ref _drainScheduled, 1, 0) == 0;
                }
            }
            disposeNow?.Dispose();
            if (scheduleDrain)
                _ = DrainAsync(cancellationToken);
        }
    }
}
