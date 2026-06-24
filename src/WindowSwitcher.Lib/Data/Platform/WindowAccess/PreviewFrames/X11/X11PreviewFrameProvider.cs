using Avalonia.Media.Imaging;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.X11;

internal sealed class X11PreviewFrameProvider
    : IPreviewFrameProvider,
        IStreamingPreviewFrameProvider
{
    private const int FallbackPollingIntervalMs = 100;

    private readonly Lazy<IPreviewFrameProvider> _fallbackProvider;
    private readonly object _sessionsSync = new();
    private readonly Dictionary<string, X11WindowCaptureSession> _sessions = new(
        StringComparer.Ordinal
    );
    private bool _disposed;

    public X11PreviewFrameProvider(
        WinAccessorBase accessorBase,
        Func<IPreviewFrameProvider> fallbackProviderFactory
    )
    {
        ArgumentNullException.ThrowIfNull(accessorBase);
        ArgumentNullException.ThrowIfNull(fallbackProviderFactory);

        _fallbackProvider = new Lazy<IPreviewFrameProvider>(
            fallbackProviderFactory,
            LazyThreadSafetyMode.ExecutionAndPublication
        );
    }

    public static bool IsSupported()
    {
        return X11WindowCaptureSession.IsSupported();
    }

    public async Task<Bitmap?> RequestAsync(
        string windowId,
        ScreenshotRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (_disposed || string.IsNullOrWhiteSpace(windowId))
            return null;
        if (cancellationToken.IsCancellationRequested)
            return null;

        X11WindowCaptureSession? session = GetOrCreateSession(windowId);
        Bitmap? frame = session?.CaptureFrame(request);
        if (frame is not null)
            return frame;

        return await _fallbackProvider
            .Value
            .RequestAsync(windowId, request, cancellationToken)
            .ConfigureAwait(false);
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
        {
            await foreach (
                Bitmap fallbackFrame in StreamFallbackAsync(
                    windowId,
                    request,
                    cancellationToken
                )
            )
            {
                yield return fallbackFrame;
            }

            yield break;
        }

        Bitmap? initialFrame = session.CaptureFrame(request);
        if (initialFrame is not null)
            yield return initialFrame;

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

            Bitmap? frame = session.CaptureFrame(request);
            if (frame is not null)
            {
                yield return frame;
                continue;
            }

            RemoveSession(windowId);
            await foreach (
                Bitmap fallbackFrame in StreamFallbackAsync(
                    windowId,
                    request,
                    cancellationToken
                )
            )
            {
                yield return fallbackFrame;
            }

            yield break;
        }
    }

    public void SuspendWindow(string windowId)
    {
        RemoveSession(windowId);
        if (_fallbackProvider.IsValueCreated)
            _fallbackProvider.Value.SuspendWindow(windowId);
    }

    public void ForgetWindow(string windowId)
    {
        RemoveSession(windowId);
        if (_fallbackProvider.IsValueCreated)
            _fallbackProvider.Value.ForgetWindow(windowId);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        DisposeSessions();
        if (_fallbackProvider.IsValueCreated)
            _fallbackProvider.Value.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        DisposeSessions();
        if (_fallbackProvider.IsValueCreated)
            await _fallbackProvider.Value.DisposeAsync().ConfigureAwait(false);
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
        if (string.IsNullOrWhiteSpace(windowId))
            return;

        X11WindowCaptureSession? session = null;
        lock (_sessionsSync)
        {
            if (_sessions.TryGetValue(windowId, out session))
                _sessions.Remove(windowId);
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

    private async IAsyncEnumerable<Bitmap> StreamFallbackAsync(
        string windowId,
        ScreenshotRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken
    )
    {
        IPreviewFrameProvider fallbackProvider = _fallbackProvider.Value;
        if (fallbackProvider is IStreamingPreviewFrameProvider streamingProvider)
        {
            await foreach (
                Bitmap frame in streamingProvider.StreamAsync(windowId, request, cancellationToken)
            )
            {
                yield return frame;
            }

            yield break;
        }

        while (!cancellationToken.IsCancellationRequested && !_disposed)
        {
            Bitmap? frame = await fallbackProvider
                .RequestAsync(windowId, request, cancellationToken)
                .ConfigureAwait(false);
            if (frame is not null)
                yield return frame;

            await Task.Delay(FallbackPollingIntervalMs, cancellationToken).ConfigureAwait(false);
        }
    }

}
