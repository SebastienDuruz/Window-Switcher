using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Tmds.DBus;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Pipewire.Abstractions;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Pipewire;

/// <summary>
/// Captures Wayland window previews through the ScreenCast portal and libpipewire.
/// </summary>
public sealed class PipeWireFrameProvider
    : IPreviewFrameProvider,
        INativeBgraStreamingPreviewFrameProvider,
        IPreviewSelectionReset
{
    private const int DefaultWidth = 640;
    private const int DefaultHeight = 360;
    private const int RestartDelayMilliseconds = 300;
    private readonly WinAccessorBase _accessor;
    private readonly IPipeWirePortalClient _portalClient;
    private readonly IPipeWireNativeStreamFactory _streamFactory;
    private readonly IPipeWireDiagnostics _diagnostics;
    private readonly WaylandScreenCastMemoryCache _restoreTokenCache;
    private readonly SemaphoreSlim _portalCreationGate = new(1, 1);
    private readonly object _capturesSync = new();
    private readonly Dictionary<string, CaptureContext> _captures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<CaptureContext?>> _creationTasks = new(
        StringComparer.Ordinal
    );
    private readonly Dictionary<string, CancellationTokenSource> _creationCancellation = new(
        StringComparer.Ordinal
    );
    private readonly HashSet<Task> _portalCloseTasks = [];
    private bool _disposed;
    private Task _shutdownTask = Task.CompletedTask;

    /// <inheritdoc />
    public event EventHandler<PreviewSelectionPromptEventArgs>? SelectionPromptChanged;

    /// <summary>
    /// Initializes a Wayland PipeWire preview provider.
    /// </summary>
    /// <param name="accessorBase">Window accessor used to derive stable restore-token keys.</param>
    public PipeWireFrameProvider(WinAccessorBase accessorBase)
        : this(
            accessorBase,
            new PipeWirePortalClient(TracePipeWireDiagnostics.Instance),
            new PipeWireNativeStreamFactory(),
            TracePipeWireDiagnostics.Instance,
            WaylandScreenCastMemoryCache.Shared
        ) { }

    internal PipeWireFrameProvider(
        WinAccessorBase accessorBase,
        IPipeWirePortalClient portalClient,
        IPipeWireNativeStreamFactory streamFactory,
        IPipeWireDiagnostics diagnostics,
        WaylandScreenCastMemoryCache restoreTokenCache
    )
    {
        ArgumentNullException.ThrowIfNull(accessorBase);
        ArgumentNullException.ThrowIfNull(portalClient);
        ArgumentNullException.ThrowIfNull(streamFactory);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(restoreTokenCache);
        _accessor = accessorBase;
        _portalClient = portalClient;
        _streamFactory = streamFactory;
        _diagnostics = diagnostics;
        _restoreTokenCache = restoreTokenCache;
    }

    /// <inheritdoc />
    public async Task<Bitmap?> RequestAsync(
        string windowId,
        ScreenshotRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (!CanCapture(windowId))
            return null;

        CaptureContext? capture = await EnsureCaptureAsync(windowId, request, cancellationToken)
            .ConfigureAwait(false);
        if (capture is null)
            return null;

        NativeBgraPreviewFrame? frame = await capture
            .Stream.ReadFrameAsync(
                TimeSpan.FromMilliseconds(Math.Max(0, request.TimeoutMs)),
                cancellationToken
            )
            .ConfigureAwait(false);
        if (frame is null)
        {
            if (capture.Stream.IsFaulted)
                InvalidateCapture(windowId, capture);
            return null;
        }

        using (frame)
            return CreateBitmap(frame);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<NativeBgraPreviewFrame> StreamNativeBgraAsync(
        string windowId,
        ScreenshotRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        if (!CanCapture(windowId))
            yield break;

        while (!cancellationToken.IsCancellationRequested)
        {
            CaptureContext? capture = await EnsureCaptureAsync(windowId, request, cancellationToken)
                .ConfigureAwait(false);
            if (capture is null)
                yield break;

            await foreach (
                NativeBgraPreviewFrame frame in capture.Stream.ReadFramesAsync(cancellationToken)
            )
                yield return frame;

            if (cancellationToken.IsCancellationRequested)
                yield break;

            InvalidateCapture(windowId, capture);
            try
            {
                await Task.Delay(RestartDelayMilliseconds, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
        }
    }

    /// <inheritdoc />
    public void SuspendWindow(string windowId)
    {
        if (string.IsNullOrWhiteSpace(windowId))
            return;

        CaptureContext? capture;
        lock (_capturesSync)
            _captures.TryGetValue(windowId, out capture);
        capture?.Stream.SetActive(false);
    }

    /// <inheritdoc />
    public void ForgetWindow(string windowId)
    {
        if (string.IsNullOrWhiteSpace(windowId))
            return;

        CaptureContext? capture = null;
        CancellationTokenSource? creationCancellation = null;
        lock (_capturesSync)
        {
            _creationCancellation.TryGetValue(windowId, out creationCancellation);
            if (_captures.TryGetValue(windowId, out capture))
            {
                _captures.Remove(windowId);
                RegisterPortalCloseLocked(capture.PortalSessionPath);
            }
        }

        try
        {
            creationCancellation?.Cancel();
        }
        catch (ObjectDisposedException) { }
        if (capture is not null)
            DisposeStream(capture.Stream);
    }

    /// <inheritdoc />
    public void ResetSelection(string windowId)
    {
        if (string.IsNullOrWhiteSpace(windowId))
            return;

        _restoreTokenCache.ClearRestoreToken([$"window:{windowId.Trim().ToLowerInvariant()}"]);
        ForgetWindow(windowId);
    }

    /// <inheritdoc />
    public void ResetAllSelections()
    {
        string[] windowIds;
        lock (_capturesSync)
        {
            if (_disposed)
                return;
            windowIds = _captures
                .Keys.Concat(_creationTasks.Keys)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        _restoreTokenCache.Clear();
        foreach (string windowId in windowIds)
            ForgetWindow(windowId);
    }

    private bool CanCapture(string windowId)
    {
        lock (_capturesSync)
        {
            return !_disposed
                && !string.IsNullOrWhiteSpace(windowId)
                && RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        }
    }

    private async Task<CaptureContext?> EnsureCaptureAsync(
        string windowId,
        ScreenshotRequest request,
        CancellationToken cancellationToken
    )
    {
        Task<CaptureContext?> creationTask;
        CaptureContext? existing;
        lock (_capturesSync)
        {
            if (_captures.TryGetValue(windowId, out existing))
            {
                existing.UpdateDimensions(GetWidth(request), GetHeight(request));
                return existing;
            }

            if (!_creationTasks.TryGetValue(windowId, out creationTask!))
            {
                var cancellationSource = new CancellationTokenSource();
                _creationCancellation[windowId] = cancellationSource;
                creationTask = CreateAndRegisterCaptureAsync(
                    windowId,
                    GetWidth(request),
                    GetHeight(request),
                    cancellationSource.Token
                );
                _creationTasks[windowId] = creationTask;
            }
        }

        try
        {
            CaptureContext? capture = await creationTask
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            capture?.UpdateDimensions(GetWidth(request), GetHeight(request));
            return capture;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private async Task<CaptureContext?> CreateAndRegisterCaptureAsync(
        string windowId,
        int width,
        int height,
        CancellationToken cancellationToken
    )
    {
        CaptureContext? created = null;
        try
        {
            created = await CreateCaptureAsync(windowId, width, height, cancellationToken)
                .ConfigureAwait(false);
            if (created is null)
                return null;

            lock (_capturesSync)
            {
                if (_disposed)
                    return null;
                if (_captures.TryGetValue(windowId, out CaptureContext? existing))
                    return existing;
                _captures[windowId] = created;
                CaptureContext registered = created;
                created = null;
                return registered;
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception exception)
        {
            _diagnostics.Error("capture creation failed", exception);
            return null;
        }
        finally
        {
            if (created is not null)
                DisposeCapture(created);
            CancellationTokenSource? creationSource = null;
            lock (_capturesSync)
            {
                _creationTasks.Remove(windowId);
                if (_creationCancellation.TryGetValue(windowId, out creationSource))
                    _creationCancellation.Remove(windowId);
            }
            creationSource?.Dispose();
        }
    }

    private async Task<CaptureContext?> CreateCaptureAsync(
        string windowId,
        int width,
        int height,
        CancellationToken cancellationToken
    )
    {
        await _portalCreationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        PortalCapture? portalCapture = null;
        bool selectionPromptRaised = false;
        try
        {
            IReadOnlyList<string> restoreKeys = BuildRestoreKeys(windowId);
            string? restoreToken = _restoreTokenCache.TakeRestoreToken(restoreKeys);
            bool pickerExpected = string.IsNullOrWhiteSpace(restoreToken);
            if (pickerExpected)
            {
                selectionPromptRaised = true;
                NotifySelectionPrompt(windowId, isPending: true);
                await RaiseTargetBeforePickerAsync(windowId, cancellationToken)
                    .ConfigureAwait(false);
            }
            portalCapture = await _portalClient
                .OpenAsync(restoreToken, cancellationToken)
                .ConfigureAwait(false);
            if (portalCapture is null)
                return null;

            if (!string.IsNullOrWhiteSpace(portalCapture.RestoreToken))
                _restoreTokenCache.SetRestoreToken(restoreKeys, portalCapture.RestoreToken);

            IPipeWireNativeStream? stream = await _streamFactory
                .CreateAsync(
                    portalCapture.RemoteHandle,
                    portalCapture.PipeWireNodeId,
                    width,
                    height,
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (stream is null)
            {
                portalCapture.RemoteHandle.Dispose();
                return null;
            }

            var capture = new CaptureContext(portalCapture.SessionPath, stream, width, height);
            portalCapture = null;
            return capture;
        }
        finally
        {
            if (selectionPromptRaised)
                NotifySelectionPrompt(windowId, isPending: false);
            _portalCreationGate.Release();
            if (portalCapture is not null)
            {
                portalCapture.RemoteHandle.Dispose();
                QueuePortalClose(portalCapture.SessionPath);
            }
        }
    }

    private void NotifySelectionPrompt(string windowId, bool isPending)
    {
        try
        {
            SelectionPromptChanged?.Invoke(
                this,
                new PreviewSelectionPromptEventArgs(windowId, isPending)
            );
        }
        catch (Exception exception)
        {
            _diagnostics.Error("preview selection indicator callback failed", exception);
        }
    }

    private async Task RaiseTargetBeforePickerAsync(
        string windowId,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await _accessor.RaiseWindowAsync(windowId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _diagnostics.Error("target activation before portal selection failed", exception);
        }
    }

    private void InvalidateCapture(string windowId, CaptureContext capture)
    {
        bool removed;
        lock (_capturesSync)
        {
            removed =
                _captures.TryGetValue(windowId, out CaptureContext? current)
                && ReferenceEquals(current, capture)
                && _captures.Remove(windowId);
            if (removed)
                RegisterPortalCloseLocked(capture.PortalSessionPath);
        }
        if (removed)
            DisposeStream(capture.Stream);
    }

    private void DisposeCapture(CaptureContext capture)
    {
        try
        {
            DisposeStream(capture.Stream);
        }
        finally
        {
            QueuePortalClose(capture.PortalSessionPath);
        }
    }

    private void QueuePortalClose(string sessionPath)
    {
        lock (_capturesSync)
            RegisterPortalCloseLocked(sessionPath);
    }

    private void RegisterPortalCloseLocked(string sessionPath)
    {
        Task closeTask = ClosePortalSafelyAsync(sessionPath);
        _portalCloseTasks.Add(closeTask);
        _ = ObservePortalCloseAsync(closeTask);
    }

    private void DisposeStream(IPipeWireNativeStream stream)
    {
        try
        {
            stream.Dispose();
        }
        catch (Exception exception)
        {
            _diagnostics.Error("capture cleanup failed", exception);
        }
    }

    private async Task ObservePortalCloseAsync(Task closeTask)
    {
        await closeTask.ConfigureAwait(false);
        lock (_capturesSync)
            _portalCloseTasks.Remove(closeTask);
    }

    private async Task ClosePortalSafelyAsync(string sessionPath)
    {
        try
        {
            await _portalClient
                .CloseAsync(sessionPath, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _diagnostics.Error("portal session cleanup failed", exception);
        }
    }

    private IReadOnlyList<string> BuildRestoreKeys(string windowId)
    {
        var keys = new List<string>();
        var unique = new HashSet<string>(StringComparer.Ordinal);

        void Add(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            string normalized = value.Trim().ToLowerInvariant();
            if (unique.Add(normalized))
                keys.Add(normalized);
        }

        try
        {
            IReadOnlyCollection<WindowConfig> windows = _accessor.GetWindows();
            WindowConfig? selected = windows.FirstOrDefault(window =>
                string.Equals(window.WindowId, windowId, StringComparison.OrdinalIgnoreCase)
            );
            if (selected is not null)
            {
                string normalizedTitle = NormalizeRestoreIdentityPart(selected.WindowTitle);
                string normalizedProcess = NormalizeRestoreIdentityPart(selected.ProcessName);

                if (!string.IsNullOrWhiteSpace(normalizedTitle))
                {
                    int matchingTitleCount = windows.Count(window =>
                        string.Equals(
                            NormalizeRestoreIdentityPart(window.WindowTitle),
                            normalizedTitle,
                            StringComparison.Ordinal
                        )
                    );
                    if (matchingTitleCount == 1)
                        Add($"title:{normalizedTitle}");

                    if (!string.IsNullOrWhiteSpace(normalizedProcess))
                    {
                        int matchingProcessTitleCount = windows.Count(window =>
                            string.Equals(
                                NormalizeRestoreIdentityPart(window.WindowTitle),
                                normalizedTitle,
                                StringComparison.Ordinal
                            )
                            && string.Equals(
                                NormalizeRestoreIdentityPart(window.ProcessName),
                                normalizedProcess,
                                StringComparison.Ordinal
                            )
                        );
                        if (matchingProcessTitleCount == 1)
                            Add($"process_title:{normalizedProcess}|{normalizedTitle}");
                    }
                }
            }
        }
        catch (Exception exception)
        {
            _diagnostics.Error("window identity lookup failed", exception);
        }

        Add($"window:{windowId}");
        return keys;
    }

    private static string NormalizeRestoreIdentityPart(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
    }

    private static int GetWidth(ScreenshotRequest request)
    {
        return request.MaxWidthPx is > 0 ? request.MaxWidthPx.Value : DefaultWidth;
    }

    private static int GetHeight(ScreenshotRequest request)
    {
        return request.MaxHeightPx is > 0 ? request.MaxHeightPx.Value : DefaultHeight;
    }

    private static Bitmap? CreateBitmap(NativeBgraPreviewFrame frame)
    {
        if (frame.Data == IntPtr.Zero || frame.WidthPx <= 0 || frame.HeightPx <= 0)
            return null;

        var bitmap = new WriteableBitmap(
            new PixelSize(frame.WidthPx, frame.HeightPx),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque
        );
        using ILockedFramebuffer framebuffer = bitmap.Lock();
        if (
            framebuffer.Address != IntPtr.Zero
            && frame.TryCopyTo(framebuffer.Address, framebuffer.RowBytes)
        )
            return bitmap;
        bitmap.Dispose();
        return null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        List<CaptureContext> captures;
        List<CancellationTokenSource> cancellations;
        List<Task<CaptureContext?>> creations;
        TaskCompletionSource shutdownCompletion;
        lock (_capturesSync)
        {
            if (_disposed)
                return;
            _disposed = true;
            captures = _captures.Values.ToList();
            cancellations = _creationCancellation.Values.ToList();
            creations = _creationTasks.Values.ToList();
            _captures.Clear();
            shutdownCompletion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            _shutdownTask = shutdownCompletion.Task;
        }

        foreach (CancellationTokenSource cancellation in cancellations)
        {
            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException) { }
        }
        foreach (CaptureContext capture in captures)
            DisposeStream(capture.Stream);

        _ = CompleteShutdownAsync(captures, creations, shutdownCompletion);
    }

    private async Task CompleteShutdownAsync(
        IReadOnlyCollection<CaptureContext> captures,
        IReadOnlyCollection<Task<CaptureContext?>> creations,
        TaskCompletionSource completion
    )
    {
        try
        {
            await ShutdownPortalAsync(captures, creations).ConfigureAwait(false);
            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    private async Task ShutdownPortalAsync(
        IReadOnlyCollection<CaptureContext> captures,
        IReadOnlyCollection<Task<CaptureContext?>> creations
    )
    {
        try
        {
            await Task.WhenAll(creations).ConfigureAwait(false);
            Task[] queuedCloses;
            lock (_capturesSync)
                queuedCloses = _portalCloseTasks.ToArray();
            await Task.WhenAll(
                    queuedCloses.Concat(
                        captures.Select(capture =>
                            ClosePortalSafelyAsync(capture.PortalSessionPath)
                        )
                    )
                )
                .ConfigureAwait(false);
        }
        finally
        {
            _portalClient.Dispose();
            _portalCreationGate.Dispose();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Dispose();
        await _shutdownTask.ConfigureAwait(false);
    }
}

internal sealed class CaptureContext(
    string portalSessionPath,
    IPipeWireNativeStream stream,
    int width,
    int height
)
{
    private readonly object _syncRoot = new();
    private int _width = width;
    private int _height = height;

    internal string PortalSessionPath { get; } = portalSessionPath;
    internal IPipeWireNativeStream Stream { get; } = stream;

    internal void UpdateDimensions(int width, int height)
    {
        lock (_syncRoot)
        {
            if (_width == width && _height == height)
                return;
            _width = width;
            _height = height;
        }
        Stream.UpdateTargetDimensions(width, height);
    }
}

internal sealed record PortalCapture(
    string SessionPath,
    string? RestoreToken,
    uint PipeWireNodeId,
    CloseSafeHandle RemoteHandle
);

internal interface IPipeWirePortalClient : IDisposable
{
    Task<PortalCapture?> OpenAsync(string? restoreToken, CancellationToken cancellationToken);
    Task CloseAsync(string sessionPath, CancellationToken cancellationToken);
}

internal sealed class PipeWirePortalClient(IPipeWireDiagnostics diagnostics) : IPipeWirePortalClient
{
    private const string Destination = "org.freedesktop.portal.Desktop";
    private static readonly ObjectPath DesktopPath = new("/org/freedesktop/portal/desktop");
    private readonly object _syncRoot = new();
    private Connection? _connection;
    private string? _localName;
    private bool _disposed;

    public async Task<PortalCapture?> OpenAsync(
        string? restoreToken,
        CancellationToken cancellationToken
    )
    {
        Connection? connection = await EnsureConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        if (connection is null)
            return null;
        diagnostics.Information("portal session bus connected");

        IPipeWirePortalScreenCast screenCast = connection.CreateProxy<IPipeWirePortalScreenCast>(
            Destination,
            DesktopPath
        );
        string nonce = Guid.NewGuid().ToString("N");
        string sessionToken = $"ws_session_{nonce}";
        string? sessionPath = null;
        try
        {
            string createToken = $"ws_create_{nonce}";
            PortalResponse? create = await InvokeRequestAsync(
                    connection,
                    createToken,
                    () =>
                        screenCast.CreateSessionAsync(
                            new Dictionary<string, object>
                            {
                                ["handle_token"] = createToken,
                                ["session_handle_token"] = sessionToken,
                            }
                        ),
                    TimeSpan.FromMinutes(2),
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (create is null || create.Value.Code != 0)
                return null;
            diagnostics.Information("portal session created");

            sessionPath =
                ExtractObjectPath(create.Value.Results, "session_handle")
                ?? BuildSessionPath(sessionToken);
            if (string.IsNullOrWhiteSpace(sessionPath))
                return null;

            string selectToken = $"ws_select_{nonce}";
            var selectOptions = new Dictionary<string, object>
            {
                ["handle_token"] = selectToken,
                ["types"] = (uint)2,
                ["multiple"] = false,
                ["cursor_mode"] = (uint)2,
                ["persist_mode"] = (uint)2,
            };
            if (!string.IsNullOrWhiteSpace(restoreToken))
                selectOptions["restore_token"] = restoreToken;

            var sessionObjectPath = new ObjectPath(sessionPath);
            PortalResponse? select = await InvokeRequestAsync(
                    connection,
                    selectToken,
                    () => screenCast.SelectSourcesAsync(sessionObjectPath, selectOptions),
                    TimeSpan.FromMinutes(5),
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (select is null || select.Value.Code != 0)
                return null;
            diagnostics.Information("portal source selected");

            string startToken = $"ws_start_{nonce}";
            PortalResponse? start = await InvokeRequestAsync(
                    connection,
                    startToken,
                    () =>
                        screenCast.StartAsync(
                            sessionObjectPath,
                            string.Empty,
                            new Dictionary<string, object> { ["handle_token"] = startToken }
                        ),
                    TimeSpan.FromMinutes(5),
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (
                start is null
                || start.Value.Code != 0
                || !TryExtractPipeWireNodeId(start.Value.Results, out uint pipeWireNodeId)
            )
                return null;
            diagnostics.Information("portal stream started");

            CloseSafeHandle remoteHandle = await screenCast
                .OpenPipeWireRemoteAsync(sessionObjectPath, new Dictionary<string, object>())
                .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken)
                .ConfigureAwait(false);
            if (remoteHandle.IsInvalid || remoteHandle.IsClosed)
            {
                remoteHandle.Dispose();
                return null;
            }
            diagnostics.Information("portal PipeWire remote opened");

            string? newRestoreToken = ExtractString(start.Value.Results, "restore_token");
            var capture = new PortalCapture(
                sessionPath,
                newRestoreToken,
                pipeWireNodeId,
                remoteHandle
            );
            sessionPath = null;
            return capture;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (Exception exception)
        {
            diagnostics.Error("portal request failed", exception);
            return null;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(sessionPath))
                await CloseAsync(sessionPath, CancellationToken.None).ConfigureAwait(false);
        }
    }

    public async Task CloseAsync(string sessionPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionPath))
            return;
        Connection? connection;
        lock (_syncRoot)
            connection = _connection;
        if (connection is null)
            return;

        try
        {
            IPipeWirePortalSession session = connection.CreateProxy<IPipeWirePortalSession>(
                Destination,
                new ObjectPath(sessionPath)
            );
            await session
                .CloseAsync()
                .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (TimeoutException) { }
        catch (Exception exception)
        {
            diagnostics.Error("portal close failed", exception);
        }
    }

    private async Task<Connection?> EnsureConnectionAsync(CancellationToken cancellationToken)
    {
        Connection connection;
        lock (_syncRoot)
        {
            if (_disposed)
                return null;
            _connection ??= new Connection(
                new ClientConnectionOptions(Address.Session)
                {
                    SynchronizationContext = null,
                    AutoConnect = true,
                    RunContinuationsAsynchronously = true,
                }
            );
            connection = _connection;
        }

        try
        {
            ConnectionInfo info = await connection
                .ConnectAsync()
                .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken)
                .ConfigureAwait(false);
            lock (_syncRoot)
                _localName = info.LocalName;
            return connection;
        }
        catch (Exception exception)
        {
            diagnostics.Error("session bus connection failed", exception);
            return null;
        }
    }

    private async Task<PortalResponse?> InvokeRequestAsync(
        Connection connection,
        string handleToken,
        Func<Task<ObjectPath>> invoke,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        ObjectPath expectedPath = BuildRequestPath(handleToken);
        var completion = new TaskCompletionSource<PortalResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        IPipeWirePortalRequest request = connection.CreateProxy<IPipeWirePortalRequest>(
            Destination,
            expectedPath
        );
        using IDisposable watcher = await request
            .WatchResponseAsync(response =>
                completion.TrySetResult(
                    new PortalResponse(
                        response.Response,
                        response.Results ?? new Dictionary<string, object>()
                    )
                )
            )
            .ConfigureAwait(false);

        ObjectPath actualPath = await invoke()
            .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken)
            .ConfigureAwait(false);
        if (
            !string.Equals(actualPath.ToString(), expectedPath.ToString(), StringComparison.Ordinal)
        )
            return await WaitForResponseAsync(connection, actualPath, timeout, cancellationToken)
                .ConfigureAwait(false);

        try
        {
            return await completion
                .Task.WaitAsync(timeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    private static async Task<PortalResponse?> WaitForResponseAsync(
        Connection connection,
        ObjectPath requestPath,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        var completion = new TaskCompletionSource<PortalResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        IPipeWirePortalRequest request = connection.CreateProxy<IPipeWirePortalRequest>(
            Destination,
            requestPath
        );
        using IDisposable watcher = await request
            .WatchResponseAsync(response =>
                completion.TrySetResult(
                    new PortalResponse(
                        response.Response,
                        response.Results ?? new Dictionary<string, object>()
                    )
                )
            )
            .ConfigureAwait(false);
        try
        {
            return await completion
                .Task.WaitAsync(timeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    private ObjectPath BuildRequestPath(string handleToken)
    {
        string localName;
        lock (_syncRoot)
            localName =
                _localName ?? throw new InvalidOperationException("D-Bus name unavailable.");
        string sender = localName.TrimStart(':').Replace('.', '_');
        return new ObjectPath($"/org/freedesktop/portal/desktop/request/{sender}/{handleToken}");
    }

    private string? BuildSessionPath(string sessionToken)
    {
        string? localName;
        lock (_syncRoot)
            localName = _localName;
        if (string.IsNullOrWhiteSpace(localName))
            return null;
        string sender = localName.TrimStart(':').Replace('.', '_');
        return $"/org/freedesktop/portal/desktop/session/{sender}/{sessionToken}";
    }

    private static string? ExtractObjectPath(IDictionary<string, object> results, string key)
    {
        if (!results.TryGetValue(key, out object? value))
            return null;
        value = Unwrap(value);
        return value switch
        {
            ObjectPath path => path.ToString(),
            string text when text.StartsWith("/", StringComparison.Ordinal) => text,
            _ => null,
        };
    }

    private static string? ExtractString(IDictionary<string, object> results, string key)
    {
        if (!results.TryGetValue(key, out object? value))
            return null;
        value = Unwrap(value);
        return value is string text && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;
    }

    internal static bool TryExtractPipeWireNodeId(
        IDictionary<string, object> results,
        out uint pipeWireNodeId
    )
    {
        pipeWireNodeId = 0;
        if (!results.TryGetValue("streams", out object? value))
            return false;

        return TryExtractPipeWireNodeId(Unwrap(value), ref pipeWireNodeId)
            && pipeWireNodeId is not (LibPipeWireNative.PipeWireIdCore or uint.MaxValue);
    }

    private static bool TryExtractPipeWireNodeId(object? value, ref uint pipeWireNodeId)
    {
        value = Unwrap(value);
        if (value is null)
            return false;

        if (value is ITuple tuple && tuple.Length > 0)
            return TryConvertToUInt32(Unwrap(tuple[0]), out pipeWireNodeId);

        if (TryConvertToUInt32(value, out uint scalarNodeId))
        {
            pipeWireNodeId = scalarNodeId;
            return true;
        }

        bool found = false;
        if (value is Array array)
        {
            for (int index = 0; index < array.Length; index++)
            {
                uint candidate = pipeWireNodeId;
                if (!TryExtractPipeWireNodeId(array.GetValue(index), ref candidate))
                    continue;
                pipeWireNodeId = candidate;
                found = true;
            }
            return found;
        }

        if (value is IEnumerable enumerable and not string and not IDictionary)
        {
            foreach (object? item in enumerable)
            {
                uint candidate = pipeWireNodeId;
                if (!TryExtractPipeWireNodeId(item, ref candidate))
                    continue;
                pipeWireNodeId = candidate;
                found = true;
            }
            return found;
        }

        return false;
    }

    private static bool TryConvertToUInt32(object? value, out uint converted)
    {
        value = Unwrap(value);
        switch (value)
        {
            case byte number:
                converted = number;
                return true;
            case ushort number:
                converted = number;
                return true;
            case uint number:
                converted = number;
                return true;
            case int number when number >= 0:
                converted = (uint)number;
                return true;
            case long number when number is >= 0 and <= uint.MaxValue:
                converted = (uint)number;
                return true;
            case ulong number when number <= uint.MaxValue:
                converted = (uint)number;
                return true;
            default:
                converted = 0;
                return false;
        }
    }

    private static object? Unwrap(object? value)
    {
        for (int depth = 0; depth < 3 && value is not null; depth++)
        {
            Type type = value.GetType();
            if (!type.Name.Contains("Variant", StringComparison.OrdinalIgnoreCase))
                break;
            value = type.GetProperty("Value")?.GetValue(value);
        }
        return value;
    }

    public void Dispose()
    {
        Connection? connection;
        lock (_syncRoot)
        {
            if (_disposed)
                return;
            _disposed = true;
            connection = _connection;
            _connection = null;
            _localName = null;
        }
        connection?.Dispose();
    }

    private readonly record struct PortalResponse(uint Code, IDictionary<string, object> Results);
}
