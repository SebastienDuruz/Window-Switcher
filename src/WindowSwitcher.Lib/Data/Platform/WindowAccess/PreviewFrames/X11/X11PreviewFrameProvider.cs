using System.Diagnostics;
using System.Runtime.CompilerServices;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.X11;

internal sealed class X11PreviewFrameProvider : IPreviewFrameProvider
{
    private readonly object _sessionsSync = new();
    private readonly Dictionary<string, IX11WindowCaptureSession> _sessions = new(
        StringComparer.Ordinal
    );
    private readonly Func<string, IX11WindowCaptureSession?> _sessionFactory;
    private bool _disposed;

    public X11PreviewFrameProvider(WinAccessorBase accessorBase)
        : this(X11WindowCaptureSession.TryCreate)
    {
        ArgumentNullException.ThrowIfNull(accessorBase);
    }

    internal X11PreviewFrameProvider(Func<string, IX11WindowCaptureSession?> sessionFactory)
    {
        ArgumentNullException.ThrowIfNull(sessionFactory);
        _sessionFactory = sessionFactory;
    }

    public static bool IsSupported()
    {
        return X11WindowCaptureSession.IsSupported();
    }

    public async IAsyncEnumerable<PreviewFrame> StreamAsync(
        string windowId,
        ScreenshotRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        if (_disposed || string.IsNullOrWhiteSpace(windowId))
            yield break;

        IX11WindowCaptureSession? session = GetOrCreateSession(windowId);
        if (session is null)
            yield break;

        long lastFrameTimestamp = 0;
        bool captureDue = true;
        while (!cancellationToken.IsCancellationRequested && !_disposed)
        {
            if (!captureDue)
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
            }

            try
            {
                await DelayUntilNextFrameAsync(lastFrameTimestamp, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }

            lastFrameTimestamp = Stopwatch.GetTimestamp();
            if (!session.TryCaptureFrame(request, out NativeBgraPreviewFrame? frame))
            {
                RemoveSession(windowId, session);
                yield break;
            }

            // A skipped capture is retried on the next frame interval, even without new damage.
            captureDue = frame is null;
            if (frame is not null)
                yield return frame;
        }
    }

    private static Task DelayUntilNextFrameAsync(
        long lastFrameTimestamp,
        CancellationToken cancellationToken
    )
    {
        TimeSpan elapsed = Stopwatch.GetElapsedTime(lastFrameTimestamp);
        TimeSpan remaining = PreviewFrameTiming.FrameInterval - elapsed;
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

    private IX11WindowCaptureSession? GetOrCreateSession(string windowId)
    {
        if (_disposed)
            return null;

        lock (_sessionsSync)
        {
            if (_disposed)
                return null;
            if (_sessions.TryGetValue(windowId, out IX11WindowCaptureSession? existing))
                return existing;

            IX11WindowCaptureSession? created = _sessionFactory(windowId);
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

    private void RemoveSession(string windowId, IX11WindowCaptureSession? expectedSession)
    {
        if (string.IsNullOrWhiteSpace(windowId))
            return;

        IX11WindowCaptureSession? session = null;
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
        List<IX11WindowCaptureSession> sessions;
        lock (_sessionsSync)
        {
            sessions = _sessions.Values.ToList();
            _sessions.Clear();
        }

        foreach (IX11WindowCaptureSession session in sessions)
            session.Dispose();
    }
}
