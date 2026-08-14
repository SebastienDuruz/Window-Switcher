using System.Diagnostics;
using Avalonia.Media.Imaging;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.X11;

internal sealed class X11PreviewFrameProvider
    : IPreviewFrameProvider,
        IStreamingPreviewFrameProvider
{
    private static readonly TimeSpan TargetFrameInterval = TimeSpan.FromMilliseconds(40);
    private readonly object _sessionsSync = new();
    private readonly Dictionary<string, X11WindowCaptureSession> _sessions = new(
        StringComparer.Ordinal
    );
    private bool _disposed;

    public X11PreviewFrameProvider(WinAccessorBase accessorBase)
    {
        ArgumentNullException.ThrowIfNull(accessorBase);
    }

    public static bool IsSupported()
    {
        return X11WindowCaptureSession.IsSupported();
    }

    public Task<Bitmap?> RequestAsync(
        string windowId,
        ScreenshotRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (_disposed || string.IsNullOrWhiteSpace(windowId))
            return Task.FromResult<Bitmap?>(null);
        if (cancellationToken.IsCancellationRequested)
            return Task.FromResult<Bitmap?>(null);

        X11WindowCaptureSession? session = GetOrCreateSession(windowId);
        if (session is null)
            return Task.FromResult<Bitmap?>(null);

        Bitmap? frame = session.CaptureFrame(request);
        if (frame is null)
            RemoveSession(windowId, session);

        return Task.FromResult(frame);
    }

    public async IAsyncEnumerable<Bitmap> StreamAsync(
        string windowId,
        ScreenshotRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default
    )
    {
        if (_disposed || string.IsNullOrWhiteSpace(windowId))
            yield break;

        X11WindowCaptureSession? session = GetOrCreateSession(windowId);
        if (session is null)
            yield break;

        Bitmap? initialFrame = session.CaptureFrame(request);
        if (initialFrame is not null)
        {
            yield return initialFrame;
        }
        else
        {
            RemoveSession(windowId, session);
            yield break;
        }

        long lastFrameTimestamp = Stopwatch.GetTimestamp();
        while (!cancellationToken.IsCancellationRequested && !_disposed)
        {
            bool hasDamage;
            try
            {
                hasDamage = await session
                    .WaitForDamageAsync(request.TimeoutMs, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }

            if (!hasDamage)
                continue;

            try
            {
                await DelayUntilNextFrameAsync(lastFrameTimestamp, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }

            Bitmap? frame = session.CaptureFrame(request);
            if (frame is not null)
            {
                yield return frame;
                lastFrameTimestamp = Stopwatch.GetTimestamp();
                continue;
            }

            RemoveSession(windowId, session);
            yield break;
        }
    }

    private static Task DelayUntilNextFrameAsync(
        long lastFrameTimestamp,
        CancellationToken cancellationToken
    )
    {
        TimeSpan elapsed = Stopwatch.GetElapsedTime(lastFrameTimestamp);
        TimeSpan remaining = TargetFrameInterval - elapsed;
        return remaining <= TimeSpan.Zero
            ? Task.CompletedTask
            : Task.Delay(remaining, cancellationToken);
    }

    public void SuspendWindow(string windowId)
    {
        RemoveSession(windowId);
    }

    public void ForgetWindow(string windowId)
    {
        RemoveSession(windowId);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        DisposeSessions();
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;

        _disposed = true;
        DisposeSessions();
        return ValueTask.CompletedTask;
    }

    private X11WindowCaptureSession? GetOrCreateSession(string windowId)
    {
        if (_disposed)
            return null;

        lock (_sessionsSync)
        {
            if (_disposed)
                return null;
            if (_sessions.TryGetValue(windowId, out X11WindowCaptureSession? existing))
                return existing;

            X11WindowCaptureSession? created = X11WindowCaptureSession.TryCreate(windowId);
            if (created is null)
                return null;

            _sessions[windowId] = created;
            return created;
        }
    }

    private void RemoveSession(string windowId)
    {
        RemoveSession(windowId, expectedSession: null);
    }

    private void RemoveSession(string windowId, X11WindowCaptureSession? expectedSession)
    {
        if (string.IsNullOrWhiteSpace(windowId))
            return;

        X11WindowCaptureSession? session = null;
        lock (_sessionsSync)
        {
            if (
                _sessions.TryGetValue(windowId, out session)
                && (expectedSession is null || ReferenceEquals(session, expectedSession))
            )
            {
                _sessions.Remove(windowId);
            }
            else
            {
                session = null;
            }
        }

        session?.Dispose();
    }

    private void DisposeSessions()
    {
        List<X11WindowCaptureSession> sessions;
        lock (_sessionsSync)
        {
            sessions = _sessions.Values.ToList();
            _sessions.Clear();
        }

        foreach (X11WindowCaptureSession session in sessions)
            session.Dispose();
    }
}
