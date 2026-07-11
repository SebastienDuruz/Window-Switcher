using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Tmds.DBus;
using WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Commands.Dependencies;
using WindowSwitcher.Lib.Data.Platform.Commands.Wrappers;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Pipewire.Abstractions;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Pipewire;

public sealed partial class PipeWireFrameProvider
    : IPreviewFrameProvider,
        INativeBgraStreamingPreviewFrameProvider
{
    private const int PipeWireReconnectDelayMs = 300;
    private const int PipeWireNodePollIntervalMs = 300;
    private const int PipeWireNodeDiscoveryTimeoutMs = 20_000;
    private const int CaptureCreationTimeoutMs = 30_000;
    private const int PipeWireNodeCacheTtlMs = 500;
    private const int PipeWireForcedNodeRefreshCooldownMs = 1_000;
    private const int MaxConcurrentWaylandPortalSessionCreations = 1;
    private const int PipeWireStreamingPullTimeoutMs = 75;
    private const int PipeWireStreamingMinFrameIntervalMs = 50;
    private const string PortalDesktopDestination = "org.freedesktop.portal.Desktop";
    private const string KdePortalBackendDestination = "org.freedesktop.impl.portal.desktop.kde";
    private const string KdePortalScreenCastInterface = "org.freedesktop.impl.portal.ScreenCast";
    private const string KdePortalAppId = "windowswitcher";

    private readonly WinAccessorBase _accessorBase;
    private readonly IPwDumpWrapper _pwDump;
    private readonly IGdbusWrapper _gdbus;
    private readonly IGstLaunchWrapper _gstLaunch;
    private readonly Lock _capturesSync = new();
    private readonly Lock _nodeCacheSync = new();
    private readonly Lock _dbusSync = new();
    private readonly SemaphoreSlim _waylandPortalSessionGate = new(
        initialCount: MaxConcurrentWaylandPortalSessionCreations,
        maxCount: MaxConcurrentWaylandPortalSessionCreations
    );
    private readonly Dictionary<string, WindowCaptureContext> _captures = new(
        StringComparer.Ordinal
    );
    private readonly Dictionary<string, Task<WindowCaptureContext?>> _captureCreationTasks = new(
        StringComparer.Ordinal
    );
    private readonly Dictionary<
        string,
        CancellationTokenSource
    > _captureCreationCancellationSources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _pendingNodeIdsByWindow = new(
        StringComparer.Ordinal
    );
    private readonly WaylandScreenCastMemoryCache _waylandScreenCastMemoryCache =
        WaylandScreenCastMemoryCache.Shared;
    private IReadOnlyList<NodeCandidate> _cachedNodeCandidates = Array.Empty<NodeCandidate>();
    private DateTime _nodeCandidatesCachedAtUtc = DateTime.MinValue;
    private bool _nodeCandidatesRefreshInProgress;
    private readonly bool _isWaylandSession;
    private readonly bool _isKdeDesktopSession;
    private Connection? _sessionBusConnection;
    private string? _sessionBusLocalName;
    private bool _disposed;

    public PipeWireFrameProvider(
        WinAccessorBase accessorBase,
        IPwDumpWrapper? pwDump = null,
        IGdbusWrapper? gdbus = null,
        IGstLaunchWrapper? gstLaunch = null
    )
    {
        ArgumentNullException.ThrowIfNull(accessorBase);

        _accessorBase = accessorBase;
        _pwDump = pwDump ?? new PwDumpWrapper();
        _gdbus = gdbus ?? new GdbusWrapper();
        _gstLaunch = gstLaunch ?? new GstLaunchWrapper();

        _isWaylandSession = IsWaylandSession();
        _isKdeDesktopSession = IsKdeDesktopSession() || IsKdePortalBackendAvailable();
    }

    public async Task<Bitmap?> RequestAsync(
        string windowId,
        ScreenshotRequest request,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            if (_disposed)
                return null;

            if (
                string.IsNullOrWhiteSpace(windowId)
                || !RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            )
                return null;

            WindowCaptureContext? capture = await EnsureCaptureAsync(
                    windowId,
                    request,
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (capture is null)
                return null;

            return await TryRequestCaptureFrameAsync(capture, request, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<NativeBgraPreviewFrame> StreamNativeBgraAsync(
        string windowId,
        ScreenshotRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        if (_disposed)
            yield break;

        if (
            string.IsNullOrWhiteSpace(windowId)
            || !RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
        )
            yield break;

        while (!cancellationToken.IsCancellationRequested && !_disposed)
        {
            WindowCaptureContext? capture = await EnsureCaptureAsync(
                    windowId,
                    request,
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (capture is null)
            {
                try
                {
                    await Task.Delay(PipeWireNodePollIntervalMs, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    yield break;
                }
                continue;
            }

            long nextPullAtMs = 0;
            while (!cancellationToken.IsCancellationRequested && !capture.IsDisposed)
            {
                long delayMs = nextPullAtMs - Environment.TickCount64;
                if (delayMs > 0)
                {
                    try
                    {
                        await Task.Delay(
                                delayMs > int.MaxValue ? int.MaxValue : (int)delayMs,
                                cancellationToken
                            )
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        yield break;
                    }
                }

                NativeBgraPreviewFrame? frame = capture.Stream.PullNativeFrame(
                    Math.Min(request.TimeoutMs, PipeWireStreamingPullTimeoutMs),
                    cancellationToken
                );

                if (frame is not null)
                {
                    capture.ConsecutiveFailures = 0;
                    capture.ConsecutiveNoFrameTimeouts = 0;
                    yield return frame;
                    nextPullAtMs = Environment.TickCount64 + PipeWireStreamingMinFrameIntervalMs;
                    continue;
                }

                bool keepStreaming = await TryRecoverCaptureAsync(
                        capture,
                        request.TimeoutMs,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                if (!keepStreaming)
                    break;
            }
        }
    }

    public void ForgetWindow(string windowId)
    {
        if (string.IsNullOrWhiteSpace(windowId))
            return;

        WindowCaptureContext? capture = null;
        CancellationTokenSource? captureCreationCancellation = null;
        lock (_capturesSync)
        {
            _pendingNodeIdsByWindow.Remove(windowId);
            if (
                _captureCreationCancellationSources.TryGetValue(
                    windowId,
                    out captureCreationCancellation
                )
            )
                captureCreationCancellation.Cancel();
            if (_captures.TryGetValue(windowId, out capture))
                _captures.Remove(windowId);
        }

        capture?.Dispose(ClosePortalSession);
    }

    public void SuspendWindow(string windowId)
    {
        if (string.IsNullOrWhiteSpace(windowId))
            return;

        WindowCaptureContext? capture = null;
        lock (_capturesSync)
            _captures.TryGetValue(windowId, out capture);

        capture?.Suspend();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        List<WindowCaptureContext> captures;
        List<Task<WindowCaptureContext?>> captureCreationTasks;
        List<CancellationTokenSource> captureCreationCancellations;
        lock (_capturesSync)
        {
            captures = _captures.Values.ToList();
            captureCreationTasks = _captureCreationTasks.Values.ToList();
            captureCreationCancellations = _captureCreationCancellationSources.Values.ToList();
            _captures.Clear();
            _captureCreationTasks.Clear();
            _captureCreationCancellationSources.Clear();
            _pendingNodeIdsByWindow.Clear();
        }

        foreach (CancellationTokenSource cancellation in captureCreationCancellations)
            cancellation.Cancel();

        WaitForCaptureCreationTasksToComplete(captureCreationTasks);

        foreach (WindowCaptureContext capture in captures)
            capture.Dispose(ClosePortalSessionSynchronously);
        foreach (CancellationTokenSource cancellation in captureCreationCancellations)
            cancellation.Dispose();

        Connection? sessionBusConnection;
        lock (_dbusSync)
        {
            sessionBusConnection = _sessionBusConnection;
            _sessionBusConnection = null;
            _sessionBusLocalName = null;
        }

        DisposeSessionBusConnection(sessionBusConnection);
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private async Task<PortalCaptureBootstrap?> TryStartWaylandPortalWindowScreencastAsync(
        string windowId,
        CancellationToken cancellationToken
    )
    {
        await _waylandPortalSessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        Connection? connection = await EnsureSessionBusConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        if (connection is null)
        {
            _waylandPortalSessionGate.Release();
            return null;
        }

        string? sessionPath = null;
        CloseSafeHandle? pipeWireRemoteHandle = null;
        try
        {
            IPipeWirePortalScreenCast screenCast =
                connection.CreateProxy<IPipeWirePortalScreenCast>(
                    PortalDesktopDestination,
                    new ObjectPath("/org/freedesktop/portal/desktop")
                );

            string token = Guid.NewGuid().ToString("N");
            string sessionToken = $"ws_session_{token}";
            string? restoreToken = GetStoredWaylandScreenCastRestoreToken(windowId);

            cancellationToken.ThrowIfCancellationRequested();
            string createHandleToken = $"ws_create_{token}";
            ObjectPath createRequestPath = BuildPortalRequestPath(connection, createHandleToken);
            PortalRequestResponse? createResponse = await InvokePortalRequestAsync(
                    connection,
                    createRequestPath,
                    () =>
                        screenCast.CreateSessionAsync(
                            new Dictionary<string, object>
                            {
                                ["handle_token"] = createHandleToken,
                                ["session_handle_token"] = sessionToken,
                            }
                        ),
                    TimeSpan.FromMinutes(2),
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (createResponse is null || createResponse.Value.ResponseCode != 0)
                return null;

            sessionPath =
                ExtractSessionPath(createResponse.Value.Results)
                ?? BuildSessionPathFromRequest(createRequestPath, sessionToken);
            if (string.IsNullOrWhiteSpace(sessionPath))
                return null;

            var selectOptions = new Dictionary<string, object>
            {
                ["handle_token"] = $"ws_select_{token}",
                ["types"] = (uint)2,
                ["multiple"] = false,
                ["cursor_mode"] = (uint)2,
                ["persist_mode"] = (uint)2,
            };
            if (!string.IsNullOrWhiteSpace(restoreToken))
            {
                selectOptions["restore_token"] = restoreToken;
            }

            var sessionObjectPath = new ObjectPath(sessionPath);
            cancellationToken.ThrowIfCancellationRequested();
            ObjectPath selectRequestPath = BuildPortalRequestPath(
                connection,
                $"ws_select_{token}"
            );
            PortalRequestResponse? selectResponse = await InvokePortalRequestAsync(
                    connection,
                    selectRequestPath,
                    () => screenCast.SelectSourcesAsync(sessionObjectPath, selectOptions),
                    TimeSpan.FromMinutes(5),
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (selectResponse is null || selectResponse.Value.ResponseCode != 0)
            {
                await ClosePortalSessionAsync(sessionPath, CancellationToken.None)
                    .ConfigureAwait(false);
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            string startHandleToken = $"ws_start_{token}";
            ObjectPath startRequestPath = BuildPortalRequestPath(connection, startHandleToken);
            PortalRequestResponse? startResponse = await InvokePortalRequestAsync(
                    connection,
                    startRequestPath,
                    () =>
                        screenCast.StartAsync(
                            sessionObjectPath,
                            string.Empty,
                            new Dictionary<string, object> { ["handle_token"] = startHandleToken }
                        ),
                    TimeSpan.FromMinutes(5),
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (startResponse is null || startResponse.Value.ResponseCode != 0)
            {
                await ClosePortalSessionAsync(sessionPath, CancellationToken.None)
                    .ConfigureAwait(false);
                return null;
            }

            string? newRestoreToken = ExtractPortalRestoreToken(startResponse.Value.Results);
            if (string.IsNullOrWhiteSpace(newRestoreToken)) { }
            else
            {
                SaveStoredWaylandScreenCastRestoreToken(windowId, newRestoreToken);
            }

            string? nodeId = SelectPortalStreamNodeId(
                windowId,
                startResponse.Value.Results,
                out string? selectedStreamStableId,
                out bool shouldPersistSelectedStreamStableId
            );
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                await ClosePortalSessionAsync(sessionPath, CancellationToken.None)
                    .ConfigureAwait(false);
                return null;
            }

            if (
                shouldPersistSelectedStreamStableId
                && !string.IsNullOrWhiteSpace(selectedStreamStableId)
            )
                SaveStoredWaylandScreenCastStreamId(windowId, selectedStreamStableId);

            cancellationToken.ThrowIfCancellationRequested();
            pipeWireRemoteHandle = await RunWithoutSynchronizationContext(() =>
                    screenCast.OpenPipeWireRemoteAsync(
                        sessionObjectPath,
                        new Dictionary<string, object>()
                    )
                )
                .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken)
                .ConfigureAwait(false);
            if (pipeWireRemoteHandle.IsInvalid || pipeWireRemoteHandle.IsClosed)
            {
                pipeWireRemoteHandle.Dispose();
                await ClosePortalSessionAsync(sessionPath, CancellationToken.None)
                    .ConfigureAwait(false);
                return null;
            }

            SetPendingNodeId(windowId, nodeId);
            return new PortalCaptureBootstrap(
                nodeId,
                sessionPath,
                PortalDesktopDestination,
                pipeWireRemoteHandle
            );
        }
        catch (TimeoutException)
        {
            ClearPendingNodeId(windowId);
            pipeWireRemoteHandle?.Dispose();
            if (!string.IsNullOrWhiteSpace(sessionPath))
                await ClosePortalSessionAsync(sessionPath, CancellationToken.None)
                    .ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException)
        {
            ClearPendingNodeId(windowId);
            pipeWireRemoteHandle?.Dispose();
            if (!string.IsNullOrWhiteSpace(sessionPath))
                await ClosePortalSessionAsync(sessionPath, CancellationToken.None)
                    .ConfigureAwait(false);
            return null;
        }
        catch (Exception)
        {
            ClearPendingNodeId(windowId);
            pipeWireRemoteHandle?.Dispose();
            if (!string.IsNullOrWhiteSpace(sessionPath))
                await ClosePortalSessionAsync(sessionPath, CancellationToken.None)
                    .ConfigureAwait(false);
            return null;
        }
        finally
        {
            _waylandPortalSessionGate.Release();
        }
    }

    private async Task<Connection?> EnsureSessionBusConnectionAsync(
        CancellationToken cancellationToken
    )
    {
        Connection connection;
        lock (_dbusSync)
        {
            _sessionBusConnection ??= CreateSessionBusConnection();
            connection = _sessionBusConnection;
        }

        try
        {
            ConnectionInfo connectionInfo = await RunWithoutSynchronizationContext(
                    () => connection.ConnectAsync()
                )
                .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken)
                .ConfigureAwait(false);

            lock (_dbusSync)
            {
                if (ReferenceEquals(_sessionBusConnection, connection))
                    _sessionBusLocalName = connectionInfo.LocalName;
            }
            return connection;
        }
        catch (Exception)
        {
            bool shouldDispose = false;
            lock (_dbusSync)
            {
                if (ReferenceEquals(_sessionBusConnection, connection))
                {
                    _sessionBusConnection = null;
                    _sessionBusLocalName = null;
                    shouldDispose = true;
                }
            }

            if (shouldDispose)
                DisposeSessionBusConnection(connection);

            return null;
        }
    }

    private static Connection CreateSessionBusConnection()
    {
        return RunWithoutSynchronizationContext(() =>
        {
            var connectionOptions = new ClientConnectionOptions(Address.Session)
            {
                // Avoid capturing AvaloniaSynchronizationContext and dispatching callbacks on shutdown.
                SynchronizationContext = null,
                AutoConnect = true,
                RunContinuationsAsynchronously = true,
            };
            return new Connection(connectionOptions);
        });
    }

    private static void DisposeSessionBusConnection(Connection? connection)
    {
        if (connection is null)
            return;

        try
        {
            connection.Dispose();
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private static async Task<PortalRequestResponse?> WaitForPortalRequestResponseAsync(
        Connection connection,
        ObjectPath requestPath,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        IPipeWirePortalRequest request = connection.CreateProxy<IPipeWirePortalRequest>(
            PortalDesktopDestination,
            requestPath
        );
        var completion = new TaskCompletionSource<PortalRequestResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        IDisposable? watcher = null;

        try
        {
            watcher = await RunWithoutSynchronizationContext(() =>
                    request.WatchResponseAsync(response =>
                    {
                        IDictionary<string, object> safeResults =
                            response.Results ?? new Dictionary<string, object>();
                        _ = completion.TrySetResult(
                            new PortalRequestResponse(response.Response, safeResults)
                        );
                    })
                )
                .ConfigureAwait(false);

            return await completion
                .Task.WaitAsync(timeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            watcher?.Dispose();
        }
    }

    private static string? ExtractSessionPath(IDictionary<string, object> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        return !results.TryGetValue("session_handle", out object? sessionHandle)
            ? null
            : TryExtractObjectPathString(sessionHandle);
    }

    private static string? ExtractPortalRestoreToken(IDictionary<string, object> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        if (!results.TryGetValue("restore_token", out object? value))
            return null;

        string? text = TryExtractScalarString(value);
        if (string.IsNullOrWhiteSpace(text))
            return null;

        return text.Trim();
    }

    private static PortalRestoreData? ExtractPortalRestoreData(IDictionary<string, object> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        if (!results.TryGetValue("restore_data", out object? value))
            return null;

        return TryDecodePortalRestoreData(value, out PortalRestoreData restoreData)
            ? restoreData
            : null;
    }

    private static bool TryDecodePortalRestoreData(object? value, out PortalRestoreData restoreData)
    {
        restoreData = default;
        value = UnwrapVariantLike(value);
        if (value is null)
            return false;

        if (value is ValueTuple<string, uint, byte[]> typedTuple)
        {
            if (string.IsNullOrWhiteSpace(typedTuple.Item1) || typedTuple.Item3.Length == 0)
                return false;
            restoreData = new PortalRestoreData(
                typedTuple.Item1.Trim(),
                typedTuple.Item2,
                typedTuple.Item3.ToArray()
            );
            return true;
        }

        if (
            value is ValueTuple<string, int, byte[]> signedTuple
            && signedTuple.Item2 >= 0
            && !string.IsNullOrWhiteSpace(signedTuple.Item1)
            && signedTuple.Item3.Length > 0
        )
        {
            restoreData = new PortalRestoreData(
                signedTuple.Item1.Trim(),
                (uint)signedTuple.Item2,
                signedTuple.Item3.ToArray()
            );
            return true;
        }

        if (value is not ITuple tuple || tuple.Length < 3)
            return false;

        string? provider = TryExtractScalarString(tuple[0]);
        if (string.IsNullOrWhiteSpace(provider))
            return false;
        if (!TryConvertToUInt32(tuple[1], out uint version))
            return false;
        if (!TryExtractByteArray(tuple[2], out byte[] bytes))
            return false;

        restoreData = new PortalRestoreData(provider.Trim(), version, bytes);
        return true;
    }

    private static string SerializePortalRestoreData(PortalRestoreData restoreData)
    {
        string base64Bytes = Convert.ToBase64String(restoreData.Bytes);
        return $"{restoreData.Provider}|{restoreData.Version.ToString(CultureInfo.InvariantCulture)}|{base64Bytes}";
    }

    private static bool TryDeserializePortalRestoreData(
        string serializedRestoreData,
        out PortalRestoreData restoreData
    )
    {
        restoreData = default;
        if (string.IsNullOrWhiteSpace(serializedRestoreData))
            return false;

        string[] parts = serializedRestoreData.Split('|', 3, StringSplitOptions.None);
        if (parts.Length != 3)
            return false;

        string provider = parts[0].Trim();
        if (string.IsNullOrWhiteSpace(provider))
            return false;
        if (
            !uint.TryParse(
                parts[1].Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out uint version
            )
        )
            return false;

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(parts[2].Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        if (bytes.Length == 0)
            return false;

        restoreData = new PortalRestoreData(provider, version, bytes);
        return true;
    }

    private static bool ArePortalRestoreDataEquivalent(
        PortalRestoreData left,
        PortalRestoreData right
    )
    {
        if (!string.Equals(left.Provider, right.Provider, StringComparison.Ordinal))
            return false;
        if (left.Version != right.Version)
            return false;
        return left.Bytes.AsSpan().SequenceEqual(right.Bytes);
    }

    private static string? BuildSessionPathFromRequest(ObjectPath requestPath, string sessionToken)
    {
        if (string.IsNullOrWhiteSpace(sessionToken))
            return null;

        string[] parts = requestPath.ToString().Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return null;

        string sender = parts[^2];
        return $"/org/freedesktop/portal/desktop/session/{sender}/{sessionToken}";
    }

    private ObjectPath BuildPortalRequestPath(Connection connection, string handleToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(handleToken);

        string? localName;
        lock (_dbusSync)
        {
            localName = ReferenceEquals(_sessionBusConnection, connection)
                ? _sessionBusLocalName
                : null;
        }
        if (string.IsNullOrWhiteSpace(localName))
            throw new InvalidOperationException("The session bus connection does not have a local name.");

        string senderPathSegment = localName.TrimStart(':').Replace('.', '_');
        return new ObjectPath(
            $"/org/freedesktop/portal/desktop/request/{senderPathSegment}/{handleToken}"
        );
    }

    private static async Task<PortalRequestResponse?> InvokePortalRequestAsync(
        Connection connection,
        ObjectPath expectedRequestPath,
        Func<Task<ObjectPath>> invokeRequestAsync,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(invokeRequestAsync);

        IPipeWirePortalRequest request = connection.CreateProxy<IPipeWirePortalRequest>(
            PortalDesktopDestination,
            expectedRequestPath
        );
        var completion = new TaskCompletionSource<PortalRequestResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        using IDisposable watcher = await RunWithoutSynchronizationContext(() =>
                request.WatchResponseAsync(response =>
                {
                    IDictionary<string, object> results =
                        response.Results ?? new Dictionary<string, object>();
                    _ = completion.TrySetResult(
                        new PortalRequestResponse(response.Response, results)
                    );
                })
            )
            .ConfigureAwait(false);

        ObjectPath actualRequestPath = await RunWithoutSynchronizationContext(invokeRequestAsync)
            .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(
                actualRequestPath.ToString(),
                expectedRequestPath.ToString(),
                StringComparison.Ordinal
            ))
        {
            return await WaitForPortalRequestResponseAsync(
                    connection,
                    actualRequestPath,
                    timeout,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        try
        {
            return await completion.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    private static string DescribePortalResultKeys(IDictionary<string, object> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        if (results.Count == 0)
            return "none";

        return string.Join(
            ",",
            results.Select(entry =>
            {
                object? value = UnwrapVariantLike(entry.Value);
                string typeName = value?.GetType().Name ?? "null";
                string preview = TryExtractScalarString(value) ?? value?.ToString() ?? string.Empty;
                if (preview.Length > 64)
                    preview = preview[..64];
                return $"{entry.Key}:{typeName}:{preview}";
            })
        );
    }

    private string? SelectPortalStreamNodeId(
        string windowId,
        IDictionary<string, object> results,
        out string? selectedStreamStableId,
        out bool shouldPersistSelectedStreamStableId
    )
    {
        selectedStreamStableId = null;
        shouldPersistSelectedStreamStableId = false;

        IReadOnlyList<PortalStreamDescriptor> streams = ExtractPortalStreams(results);
        if (streams.Count == 0)
            return null;

        HashSet<string> blockedNodeIds = SnapshotActiveNodeIds().ToHashSet(StringComparer.Ordinal);

        string? windowTitle = TryGetWindowTitleById(windowId);
        IReadOnlyCollection<string> windowPatterns = BuildWindowMatchPatterns(
            windowId,
            windowTitle
        );
        IReadOnlyCollection<string> windowTitlePatterns = BuildWindowTitlePatterns(windowTitle);
        string normalizedRequestedWindowTitle = NormalizeForSearch(windowTitle);
        PortalRestoreData? restoreData = ExtractPortalRestoreData(results);
        bool restoreDataMatchesRequestedWindowTitle =
            restoreData is PortalRestoreData typedRestoreData
            && MatchesRestoreDataWindowTitle(typedRestoreData, windowTitle);
        IReadOnlyList<NodeCandidate>? discoveredNodes = null;
        bool discoveredNodesForceRefreshed = false;
        IReadOnlyCollection<WindowConfig>? windowsSnapshot = null;

        NodeCandidate? FindNodeCandidate(string nodeId)
        {
            discoveredNodes ??= GetPipeWireNodeCandidates(forceRefresh: false);
            for (int index = 0; index < discoveredNodes.Count; index++)
            {
                NodeCandidate candidate = discoveredNodes[index];
                if (string.Equals(candidate.Id, nodeId, StringComparison.Ordinal))
                    return candidate;
            }

            if (discoveredNodesForceRefreshed)
                return null;

            discoveredNodes = GetPipeWireNodeCandidates(forceRefresh: true);
            discoveredNodesForceRefreshed = true;
            for (int index = 0; index < discoveredNodes.Count; index++)
            {
                NodeCandidate candidate = discoveredNodes[index];
                if (string.Equals(candidate.Id, nodeId, StringComparison.Ordinal))
                    return candidate;
            }

            return null;
        }

        bool MatchesExactRequestedWindowTitle(PortalStreamDescriptor stream)
        {
            if (string.IsNullOrWhiteSpace(normalizedRequestedWindowTitle))
                return false;
            if (string.IsNullOrWhiteSpace(stream.NormalizedSearchText))
                return false;

            return stream.NormalizedSearchText.Contains(
                normalizedRequestedWindowTitle,
                StringComparison.Ordinal
            );
        }

        bool MatchesExactWindowTitle(PortalStreamDescriptor stream, string? title)
        {
            string normalizedTitle = NormalizeForSearch(title);
            if (string.IsNullOrWhiteSpace(normalizedTitle))
                return false;
            if (string.IsNullOrWhiteSpace(stream.NormalizedSearchText))
                return false;

            return stream.NormalizedSearchText.Contains(normalizedTitle, StringComparison.Ordinal);
        }

        int ComputeWindowPatternScore(
            PortalStreamDescriptor stream,
            IReadOnlyCollection<string> patterns
        )
        {
            if (patterns.Count == 0)
                return 0;

            int score = ComputePatternScore(stream.NormalizedSearchText, patterns);
            NodeCandidate? candidate = FindNodeCandidate(stream.NodeId);
            if (candidate is NodeCandidate typedCandidate)
                score = Math.Max(
                    score,
                    ComputePatternScore(typedCandidate.NormalizedSearchText, patterns)
                );

            return score;
        }

        bool IsLikelyAssignedToAnotherWindow(PortalStreamDescriptor stream)
        {
            if (windowPatterns.Count == 0)
                return false;

            bool requestedExactTitleMatch = MatchesExactRequestedWindowTitle(stream);

            int requestedScore = ComputeWindowPatternScore(stream, windowPatterns);

            try
            {
                windowsSnapshot ??= _accessorBase.GetWindows();
            }
            catch
            {
                return false;
            }

            int bestOtherScore = 0;
            string? bestOtherWindowId = null;
            foreach (WindowConfig candidateWindow in windowsSnapshot)
            {
                if (AreWindowIdsEquivalent(candidateWindow.WindowId, windowId))
                    continue;

                if (MatchesExactWindowTitle(stream, candidateWindow.WindowTitle))
                {
                    if (!requestedExactTitleMatch)
                        return true;
                }

                IReadOnlyCollection<string> candidatePatterns = BuildWindowMatchPatterns(
                    candidateWindow.WindowId,
                    candidateWindow.WindowTitle
                );
                int score = ComputeWindowPatternScore(stream, candidatePatterns);
                if (score <= bestOtherScore)
                    continue;

                bestOtherScore = score;
                bestOtherWindowId = candidateWindow.WindowId;
            }

            if (bestOtherScore <= 0 || string.IsNullOrWhiteSpace(bestOtherWindowId))
                return false;

            return requestedScore <= 0 || bestOtherScore > requestedScore;
        }

        if (!string.IsNullOrWhiteSpace(normalizedRequestedWindowTitle))
        {
            PortalStreamDescriptor[] exactTitleMatches = streams
                .Where(stream =>
                    !blockedNodeIds.Contains(stream.NodeId)
                    && MatchesExactRequestedWindowTitle(stream)
                )
                .OrderBy(stream => stream.NodeId, Comparer<string>.Create(CompareNodeId))
                .ToArray();

            if (exactTitleMatches.Length == 1)
            {
                PortalStreamDescriptor matched = exactTitleMatches[0];
                selectedStreamStableId = matched.StableId;
                shouldPersistSelectedStreamStableId = true;
                return matched.NodeId;
            }
        }

        MatchState EvaluateWindowTitleMatchState(PortalStreamDescriptor stream)
        {
            if (string.IsNullOrWhiteSpace(normalizedRequestedWindowTitle))
                return MatchState.Unknown;

            if (MatchesExactRequestedWindowTitle(stream))
                return MatchState.Match;

            NodeCandidate? candidate = FindNodeCandidate(stream.NodeId);
            if (candidate is null)
                return MatchState.Unknown;

            return candidate.Value.NormalizedSearchText.Contains(
                normalizedRequestedWindowTitle,
                StringComparison.Ordinal
            )
                ? MatchState.Match
                : MatchState.Mismatch;
        }

        bool clearedStoredArtifacts = false;
        void ClearStoredArtifactsForCurrentAttempt()
        {
            ClearStoredWaylandScreenCastStreamId(windowId);
            clearedStoredArtifacts = true;
        }

        string? expectedStreamStableId = GetStoredWaylandScreenCastStreamId(windowId);
        bool ShouldPersistSelectedStableId(string? stableId)
        {
            if (string.IsNullOrWhiteSpace(stableId))
                return false;

            if (
                clearedStoredArtifacts
                || string.IsNullOrWhiteSpace(expectedStreamStableId)
                || !string.Equals(stableId, expectedStreamStableId, StringComparison.Ordinal)
            )
            {
                return true;
            }

            return false;
        }

        if (!string.IsNullOrWhiteSpace(expectedStreamStableId))
        {
            for (int index = 0; index < streams.Count; index++)
            {
                PortalStreamDescriptor stream = streams[index];
                if (
                    !string.Equals(
                        stream.StableId,
                        expectedStreamStableId,
                        StringComparison.Ordinal
                    )
                )
                    continue;
                if (blockedNodeIds.Contains(stream.NodeId))
                    continue;
                MatchState storedStreamMatchState = EvaluateWindowTitleMatchState(stream);
                if (windowTitlePatterns.Count > 0 && storedStreamMatchState is MatchState.Mismatch)
                {
                    bool allowSingleMismatchByRestoreData =
                        streams.Count == 1
                        && restoreDataMatchesRequestedWindowTitle
                        && !IsLikelyAssignedToAnotherWindow(stream);
                    if (allowSingleMismatchByRestoreData) { }
                    else
                    {
                        ClearStoredArtifactsForCurrentAttempt();
                        continue;
                    }
                }

                if (windowTitlePatterns.Count > 0 && storedStreamMatchState is MatchState.Unknown)
                { }

                if (IsLikelyAssignedToAnotherWindow(stream))
                {
                    ClearStoredArtifactsForCurrentAttempt();
                    continue;
                }

                selectedStreamStableId = stream.StableId;
                if (ShouldPersistSelectedStableId(stream.StableId))
                    shouldPersistSelectedStreamStableId = true;
                return stream.NodeId;
            }
        }

        if (windowPatterns.Count > 0)
        {
            IReadOnlyList<PortalStreamDescriptor> portalMetadataCandidates =
                string.IsNullOrWhiteSpace(normalizedRequestedWindowTitle)
                    ? streams
                    : streams
                        .Where(stream => EvaluateWindowTitleMatchState(stream) is MatchState.Match)
                        .ToArray();
            PortalStreamDescriptor? matchedByPortalMetadata = FindBestMatchingPortalStream(
                portalMetadataCandidates,
                blockedNodeIds,
                windowPatterns
            );
            if (matchedByPortalMetadata is not null)
            {
                selectedStreamStableId = matchedByPortalMetadata.Value.StableId;
                if (ShouldPersistSelectedStableId(matchedByPortalMetadata.Value.StableId))
                    shouldPersistSelectedStreamStableId = true;
                return matchedByPortalMetadata.Value.NodeId;
            }
        }

        HashSet<string> candidateIds = streams
            .Select(stream => stream.NodeId)
            .ToHashSet(StringComparer.Ordinal);
        if (windowPatterns.Count > 0)
        {
            IReadOnlyList<NodeCandidate> nodes = discoveredNodes ??= GetPipeWireNodeCandidates(
                forceRefresh: false
            );
            if (nodes.Count == 0 && !discoveredNodesForceRefreshed)
            {
                nodes = GetPipeWireNodeCandidates(forceRefresh: true);
                discoveredNodes = nodes;
                discoveredNodesForceRefreshed = true;
            }
            HashSet<string>? titleMatchedNodeIds = string.IsNullOrWhiteSpace(
                normalizedRequestedWindowTitle
            )
                ? null
                : streams
                    .Where(stream => EvaluateWindowTitleMatchState(stream) is MatchState.Match)
                    .Select(stream => stream.NodeId)
                    .ToHashSet(StringComparer.Ordinal);
            var matchingCandidates = new List<NodeCandidate>(capacity: streams.Count);
            for (int index = 0; index < nodes.Count; index++)
            {
                NodeCandidate node = nodes[index];
                if (!candidateIds.Contains(node.Id))
                    continue;
                if (blockedNodeIds.Contains(node.Id))
                    continue;
                if (titleMatchedNodeIds is not null && !titleMatchedNodeIds.Contains(node.Id))
                    continue;
                matchingCandidates.Add(node);
            }

            NodeCandidate? matchedNode = FindBestMatchingNode(matchingCandidates, windowPatterns);
            if (matchedNode is not null)
            {
                string? matchedStableId = streams
                    .FirstOrDefault(stream =>
                        string.Equals(stream.NodeId, matchedNode.Value.Id, StringComparison.Ordinal)
                    )
                    .StableId;
                selectedStreamStableId = matchedStableId;
                if (ShouldPersistSelectedStableId(matchedStableId))
                    shouldPersistSelectedStreamStableId = true;
                return matchedNode.Value.Id;
            }
        }

        IReadOnlyList<PortalStreamDescriptor> orderedFallbackStreams =
            OrderPortalStreamsDeterministically(streams);
        for (int index = 0; index < orderedFallbackStreams.Count; index++)
        {
            PortalStreamDescriptor stream = orderedFallbackStreams[index];
            if (blockedNodeIds.Contains(stream.NodeId))
                continue;

            MatchState titleMatchState = EvaluateWindowTitleMatchState(stream);
            if (windowTitlePatterns.Count > 0 && titleMatchState is not MatchState.Match)
            {
                bool allowByStableId =
                    titleMatchState is MatchState.Unknown
                    && !string.IsNullOrWhiteSpace(expectedStreamStableId)
                    && string.Equals(
                        stream.StableId,
                        expectedStreamStableId,
                        StringComparison.Ordinal
                    );
                bool allowInteractiveSelectionBootstrap =
                    titleMatchState is MatchState.Unknown
                    && string.IsNullOrWhiteSpace(expectedStreamStableId)
                    && !restoreDataMatchesRequestedWindowTitle;
                bool allowSingleStreamFallback =
                    streams.Count == 1
                    && !IsLikelyAssignedToAnotherWindow(stream)
                    && (
                        restoreDataMatchesRequestedWindowTitle
                        || allowByStableId
                        || allowInteractiveSelectionBootstrap
                    );
                if (allowSingleStreamFallback)
                {
                    string acceptanceReason =
                        restoreDataMatchesRequestedWindowTitle ? "restore_data title match"
                        : allowByStableId ? "stored stream id"
                        : "interactive selection without title metadata";
                }
                else
                {
                    string rejectionReason =
                        titleMatchState is MatchState.Mismatch
                            ? "strict title mismatch"
                            : "missing strict title verification";
                    ClearStoredArtifactsForCurrentAttempt();
                    continue;
                }
            }

            selectedStreamStableId = stream.StableId;
            if (ShouldPersistSelectedStableId(stream.StableId))
                shouldPersistSelectedStreamStableId = true;
            return stream.NodeId;
        }

        if (windowTitlePatterns.Count > 0) { }

        return null;
    }

    private static IReadOnlyList<PortalStreamDescriptor> ExtractPortalStreams(
        IDictionary<string, object> results
    )
    {
        ArgumentNullException.ThrowIfNull(results);

        if (!results.TryGetValue("streams", out object? streams))
            return Array.Empty<PortalStreamDescriptor>();

        var descriptors = new List<PortalStreamDescriptor>(capacity: 4);
        CollectPortalStreams(streams, descriptors);
        return descriptors;
    }

    private static void CollectPortalStreams(object? value, IList<PortalStreamDescriptor> streams)
    {
        if (value is null)
            return;

        if (TryExtractPortalStreamDescriptor(value, out PortalStreamDescriptor descriptor))
        {
            bool exists = false;
            for (int index = 0; index < streams.Count; index++)
            {
                PortalStreamDescriptor existing = streams[index];
                if (!string.Equals(existing.NodeId, descriptor.NodeId, StringComparison.Ordinal))
                    continue;
                if (string.Equals(existing.StableId, descriptor.StableId, StringComparison.Ordinal))
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
                streams.Add(descriptor);
            return;
        }

        if (value is Array array)
        {
            for (int index = 0; index < array.Length; index++)
                CollectPortalStreams(array.GetValue(index), streams);
            return;
        }

        if (value is System.Collections.IEnumerable enumerable)
        {
            foreach (object? item in enumerable)
                CollectPortalStreams(item, streams);
        }
    }

    private static bool TryExtractPortalStreamDescriptor(
        object value,
        out PortalStreamDescriptor descriptor
    )
    {
        ArgumentNullException.ThrowIfNull(value);

        descriptor = default;

        if (value is ValueTuple<uint, IDictionary<string, object>> typedTuple)
        {
            descriptor = new PortalStreamDescriptor(
                typedTuple.Item1.ToString(CultureInfo.InvariantCulture),
                TryExtractPortalStreamStableId(typedTuple.Item2),
                BuildPortalStreamSearchText(typedTuple.Item2)
            );
            return true;
        }

        if (
            value is ITuple tuple
            && tuple.Length > 0
            && TryConvertToUInt32(tuple[0], out uint tupleId)
        )
        {
            string? stableId =
                tuple.Length > 1 && tuple[1] is IDictionary<string, object> tupleProperties
                    ? TryExtractPortalStreamStableId(tupleProperties)
                    : null;
            string normalizedSearchText =
                tuple.Length > 1 && tuple[1] is IDictionary<string, object> tupleSearchProperties
                    ? BuildPortalStreamSearchText(tupleSearchProperties)
                    : string.Empty;
            descriptor = new PortalStreamDescriptor(
                tupleId.ToString(CultureInfo.InvariantCulture),
                stableId,
                normalizedSearchText
            );
            return true;
        }

        if (value is IDictionary<string, object> dictionary)
        {
            if (TryExtractStreamNodeIdFromDictionary(dictionary, out string nodeId))
            {
                descriptor = new PortalStreamDescriptor(
                    nodeId,
                    TryExtractPortalStreamStableId(dictionary),
                    BuildPortalStreamSearchText(dictionary)
                );
                return true;
            }
        }

        if (TryConvertToUInt32(value, out uint scalarId))
        {
            descriptor = new PortalStreamDescriptor(
                scalarId.ToString(CultureInfo.InvariantCulture),
                null,
                string.Empty
            );
            return true;
        }

        return false;
    }

    private static bool TryExtractStreamNodeIdFromDictionary(
        IDictionary<string, object> dictionary,
        out string streamNodeId
    )
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        streamNodeId = string.Empty;
        string[] candidateKeys = ["node_id", "node", "id", "stream_id"];
        for (int index = 0; index < candidateKeys.Length; index++)
        {
            string key = candidateKeys[index];
            if (!dictionary.TryGetValue(key, out object? candidateValue))
                continue;

            if (!TryConvertToUInt32(candidateValue, out uint parsedId))
                continue;

            streamNodeId = parsedId.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        return false;
    }

    private static string? TryExtractPortalStreamStableId(IDictionary<string, object> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        string[] candidateKeys = ["id", "mapping_id", "stream_id"];
        for (int index = 0; index < candidateKeys.Length; index++)
        {
            string key = candidateKeys[index];
            if (!properties.TryGetValue(key, out object? value))
                continue;

            string? scalarValue = TryExtractScalarString(value);
            if (!string.IsNullOrWhiteSpace(scalarValue))
                return scalarValue.Trim();

            if (TryConvertToUInt32(value, out uint numericValue))
                return numericValue.ToString(CultureInfo.InvariantCulture);
        }

        return null;
    }

    private static string BuildPortalStreamSearchText(IDictionary<string, object> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        var tokens = new List<string>(capacity: 24);
        foreach ((string key, object value) in properties)
        {
            if (!string.IsNullOrWhiteSpace(key))
                tokens.Add(key);
            CollectPortalStreamTokens(value, tokens, depth: 0);
        }

        if (tokens.Count == 0)
            return string.Empty;

        return NormalizeForSearch(string.Join(' ', tokens));
    }

    private static void CollectPortalStreamTokens(object? value, IList<string> tokens, int depth)
    {
        if (value is null || depth > 4)
            return;

        value = UnwrapVariantLike(value);
        if (value is null)
            return;

        if (value is byte[] || value is sbyte[])
            return;

        if (value is IDictionary<string, object> dictionary)
        {
            foreach ((string key, object nested) in dictionary)
            {
                if (!string.IsNullOrWhiteSpace(key))
                    tokens.Add(key);
                CollectPortalStreamTokens(nested, tokens, depth + 1);
            }

            return;
        }

        if (value is ITuple tuple)
        {
            for (int index = 0; index < tuple.Length; index++)
                CollectPortalStreamTokens(tuple[index], tokens, depth + 1);
            return;
        }

        if (value is System.Collections.IEnumerable enumerable && value is not string)
        {
            foreach (object? item in enumerable)
                CollectPortalStreamTokens(item, tokens, depth + 1);
            return;
        }

        if (TryConvertToUInt32(value, out uint numericValue))
        {
            tokens.Add(numericValue.ToString(CultureInfo.InvariantCulture));
            return;
        }

        string? scalar = TryExtractScalarString(value);
        if (!string.IsNullOrWhiteSpace(scalar))
            tokens.Add(scalar);
    }

    private static bool TryConvertToUInt32(object? value, out uint result)
    {
        value = UnwrapVariantLike(value);
        result = 0;
        switch (value)
        {
            case null:
                return false;
            case uint unsigned:
                result = unsigned;
                return true;
            case int signed when signed >= 0:
                result = (uint)signed;
                return true;
            case long longValue when longValue >= 0 && longValue <= uint.MaxValue:
                result = (uint)longValue;
                return true;
            case ulong ulongValue when ulongValue <= uint.MaxValue:
                result = (uint)ulongValue;
                return true;
            case string text:
                return uint.TryParse(
                    text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out result
                );
            case JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.Number:
                return jsonElement.TryGetUInt32(out result);
            default:
                return false;
        }
    }

    private static bool TryExtractByteArray(object? value, out byte[] bytes)
    {
        value = UnwrapVariantLike(value);
        bytes = Array.Empty<byte>();
        switch (value)
        {
            case null:
                return false;
            case byte[] typedBytes:
                bytes = typedBytes.ToArray();
                return bytes.Length > 0;
            case string base64Text:
                try
                {
                    bytes = Convert.FromBase64String(base64Text);
                    return bytes.Length > 0;
                }
                catch (FormatException)
                {
                    return false;
                }
            case JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.Array:
            {
                var buffer = new byte[jsonElement.GetArrayLength()];
                int index = 0;
                foreach (JsonElement element in jsonElement.EnumerateArray())
                {
                    if (!element.TryGetByte(out byte item))
                        return false;
                    buffer[index++] = item;
                }

                bytes = buffer;
                return bytes.Length > 0;
            }
            case Array array:
            {
                var buffer = new byte[array.Length];
                for (int index = 0; index < array.Length; index++)
                {
                    if (
                        !TryConvertToUInt32(array.GetValue(index), out uint parsed)
                        || parsed > byte.MaxValue
                    )
                        return false;
                    buffer[index] = (byte)parsed;
                }

                bytes = buffer;
                return bytes.Length > 0;
            }
            case System.Collections.IEnumerable enumerable:
            {
                var buffer = new List<byte>();
                foreach (object? item in enumerable)
                {
                    if (!TryConvertToUInt32(item, out uint parsed) || parsed > byte.MaxValue)
                        return false;
                    buffer.Add((byte)parsed);
                }

                bytes = buffer.ToArray();
                return bytes.Length > 0;
            }
            default:
                return false;
        }
    }

    private static object? UnwrapVariantLike(object? value)
    {
        object? current = value;
        int guard = 0;
        while (current is not null && guard++ < 8)
        {
            Type type = current.GetType();
            PropertyInfo? valueProperty = type.GetProperty(
                "Value",
                BindingFlags.Public | BindingFlags.Instance
            );
            if (valueProperty is null || valueProperty.GetIndexParameters().Length != 0)
                break;

            object? next;
            try
            {
                next = valueProperty.GetValue(current);
            }
            catch
            {
                break;
            }

            if (next is null || ReferenceEquals(next, current))
                break;

            current = next;
        }

        return current;
    }

    private static string? TryExtractObjectPathString(object? value)
    {
        value = UnwrapVariantLike(value);
        return value switch
        {
            null => null,
            ObjectPath objectPath => objectPath.ToString(),
            string text when text.StartsWith("/", StringComparison.Ordinal) => text,
            _ => null,
        };
    }

    private static string? TryExtractScalarString(object? value)
    {
        value = UnwrapVariantLike(value);
        return value switch
        {
            null => null,
            string text => text,
            ObjectPath objectPath => objectPath.ToString(),
            JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.String =>
                jsonElement.GetString(),
            _ => value.ToString(),
        };
    }

    private async Task<PortalCaptureBootstrap?> TryStartKdeBackendWindowScreencastAsync(
        string windowId,
        CancellationToken cancellationToken
    )
    {
        if (!LinuxDependencies.IsPwDumpAvailable)
            return null;

        await _waylandPortalSessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? sessionPath = null;
        try
        {
            Connection? connection = await EnsureSessionBusConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            if (connection is null)
                return null;

            IKdePortalScreenCast screenCast = connection.CreateProxy<IKdePortalScreenCast>(
                KdePortalBackendDestination,
                new ObjectPath("/org/freedesktop/portal/desktop")
            );

            KdePortalRequestPaths requestPaths = BuildKdePortalRequestPaths();
            sessionPath = requestPaths.SessionPath;

            IReadOnlyList<NodeCandidate> baseline = GetPipeWireNodeCandidates(forceRefresh: true);
            HashSet<string> baselineIds = baseline
                .Select(candidate => candidate.Id)
                .ToHashSet(StringComparer.Ordinal);

            if (
                !await TryCreateKdePortalSessionAsync(
                        screenCast,
                        requestPaths,
                        windowId,
                        cancellationToken
                    )
                    .ConfigureAwait(false)
            )
                return null;

            KdeStoredArtifacts storedArtifacts = LoadKdeStoredArtifactsForAttempt(windowId);

            Dictionary<string, object> selectOptions = BuildKdeSelectOptions(
                storedArtifacts.RestoreData,
                storedArtifacts.RestoreToken
            );
            if (
                !await TrySelectKdePortalSourcesAsync(
                        screenCast,
                        requestPaths,
                        selectOptions,
                        windowId,
                        cancellationToken
                    )
                    .ConfigureAwait(false)
            )
            {
                ClosePortalSession(sessionPath, KdePortalBackendDestination);
                return null;
            }

            KdePortalStartResult? startResult = await TryStartKdePortalSessionAsync(
                    screenCast,
                    requestPaths,
                    windowId,
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (startResult is null)
            {
                ClosePortalSession(sessionPath, KdePortalBackendDestination);
                return null;
            }

            string? nodeId = SelectPortalStreamNodeId(
                windowId,
                startResult.Value.Results,
                out string? selectedStreamStableId,
                out bool shouldPersistSelectedStreamStableId
            );
            IReadOnlyList<PortalStreamDescriptor> extractedStreams = ExtractPortalStreams(
                startResult.Value.Results
            );

            if (
                shouldPersistSelectedStreamStableId
                && !string.IsNullOrWhiteSpace(selectedStreamStableId)
            )
                SaveStoredWaylandScreenCastStreamId(windowId, selectedStreamStableId);

            if (!string.IsNullOrWhiteSpace(nodeId))
            {
                PersistUpdatedKdeRestoreArtifacts(
                    windowId,
                    storedArtifacts.RestoreData,
                    storedArtifacts.RestoreToken,
                    startResult.Value.RestoreData,
                    startResult.Value.RestoreToken
                );
                SetPendingNodeId(windowId, nodeId);
                return new PortalCaptureBootstrap(
                    nodeId,
                    sessionPath,
                    KdePortalBackendDestination,
                    PipeWireRemoteHandle: null
                );
            }

            if (extractedStreams.Count > 0)
            {
                PersistUpdatedKdeRestoreArtifacts(
                    windowId,
                    storedArtifacts.RestoreData,
                    storedArtifacts.RestoreToken,
                    startResult.Value.RestoreData,
                    startResult.Value.RestoreToken
                );
                ClosePortalSession(sessionPath, KdePortalBackendDestination);
                return null;
            }

            HashSet<string> blockedNodeIds = SnapshotActiveNodeIds()
                .ToHashSet(StringComparer.Ordinal);

            string? discoveredNodeId = await WaitForNewPipeWireNodeIdAsync(
                    baselineIds,
                    TimeSpan.FromMilliseconds(PipeWireNodeDiscoveryTimeoutMs),
                    windowId,
                    blockedNodeIds,
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(discoveredNodeId))
            {
                PersistUpdatedKdeRestoreArtifacts(
                    windowId,
                    storedArtifacts.RestoreData,
                    storedArtifacts.RestoreToken,
                    startResult.Value.RestoreData,
                    startResult.Value.RestoreToken
                );
                SetPendingNodeId(windowId, discoveredNodeId);
                return new PortalCaptureBootstrap(
                    discoveredNodeId,
                    sessionPath,
                    KdePortalBackendDestination,
                    PipeWireRemoteHandle: null
                );
            }

            ClosePortalSession(sessionPath, KdePortalBackendDestination);
            return null;
        }
        catch (TimeoutException)
        {
            if (!string.IsNullOrWhiteSpace(sessionPath))
                ClosePortalSession(sessionPath, KdePortalBackendDestination);
            return null;
        }
        catch (OperationCanceledException)
        {
            if (!string.IsNullOrWhiteSpace(sessionPath))
                ClosePortalSession(sessionPath, KdePortalBackendDestination);
            return null;
        }
        catch (Exception)
        {
            if (!string.IsNullOrWhiteSpace(sessionPath))
                ClosePortalSession(sessionPath, KdePortalBackendDestination);
            return null;
        }
        finally
        {
            _waylandPortalSessionGate.Release();
        }
    }

    private static KdePortalRequestPaths BuildKdePortalRequestPaths()
    {
        string requestToken = Guid.NewGuid().ToString("N");
        string senderPathSegment = $"1_{Environment.ProcessId}";
        return new KdePortalRequestPaths(
            CreateHandlePath: $"/org/freedesktop/portal/desktop/request/{senderPathSegment}/ws_create_{requestToken}",
            SelectHandlePath: $"/org/freedesktop/portal/desktop/request/{senderPathSegment}/ws_select_{requestToken}",
            StartHandlePath: $"/org/freedesktop/portal/desktop/request/{senderPathSegment}/ws_start_{requestToken}",
            SessionPath: $"/org/freedesktop/portal/desktop/session/{senderPathSegment}/ws_session_{requestToken}"
        );
    }

    private async Task<bool> TryCreateKdePortalSessionAsync(
        IKdePortalScreenCast screenCast,
        KdePortalRequestPaths requestPaths,
        string windowId,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        (uint createResponseCode, IDictionary<string, object> _) =
            await RunWithoutSynchronizationContext(() =>
                    screenCast.CreateSessionAsync(
                        new ObjectPath(requestPaths.CreateHandlePath),
                        new ObjectPath(requestPaths.SessionPath),
                        KdePortalAppId,
                        new Dictionary<string, object>()
                    )
                )
                .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken)
                .ConfigureAwait(false);
        return createResponseCode == 0;
    }

    private KdeStoredArtifacts LoadKdeStoredArtifactsForAttempt(string windowId)
    {
        PortalRestoreData? restoreData = null;
        string? storedRestoreDataValue = GetStoredWaylandScreenCastRestoreData(windowId);
        if (!string.IsNullOrWhiteSpace(storedRestoreDataValue))
        {
            if (
                TryDeserializePortalRestoreData(
                    storedRestoreDataValue,
                    out PortalRestoreData parsedRestoreData
                )
            )
            {
                restoreData = parsedRestoreData;
            }
            else { }
        }

        return new KdeStoredArtifacts(
            RestoreData: restoreData,
            RestoreToken: GetStoredWaylandScreenCastRestoreToken(windowId),
            StreamStableId: GetStoredWaylandScreenCastStreamId(windowId)
        );
    }

    private static Dictionary<string, object> BuildKdeSelectOptions(
        PortalRestoreData? restoreData,
        string? restoreToken
    )
    {
        var selectOptions = new Dictionary<string, object>
        {
            ["types"] = (uint)2,
            ["multiple"] = false,
            ["cursor_mode"] = (uint)2,
            ["persist_mode"] = (uint)2,
        };
        if (restoreData is PortalRestoreData typedRestoreData)
        {
            selectOptions["restore_data"] = ValueTuple.Create(
                typedRestoreData.Provider,
                typedRestoreData.Version,
                typedRestoreData.Bytes
            );
        }
        else if (!string.IsNullOrWhiteSpace(restoreToken))
        {
            selectOptions["restore_token"] = restoreToken.Trim();
        }

        return selectOptions;
    }

    private async Task<bool> TrySelectKdePortalSourcesAsync(
        IKdePortalScreenCast screenCast,
        KdePortalRequestPaths requestPaths,
        IDictionary<string, object> selectOptions,
        string windowId,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        (uint selectResponseCode, IDictionary<string, object> _) =
            await RunWithoutSynchronizationContext(() =>
                    screenCast.SelectSourcesAsync(
                        new ObjectPath(requestPaths.SelectHandlePath),
                        new ObjectPath(requestPaths.SessionPath),
                        KdePortalAppId,
                        selectOptions
                    )
                )
                .WaitAsync(TimeSpan.FromSeconds(45), cancellationToken)
                .ConfigureAwait(false);
        return selectResponseCode == 0;
    }

    private async Task<KdePortalStartResult?> TryStartKdePortalSessionAsync(
        IKdePortalScreenCast screenCast,
        KdePortalRequestPaths requestPaths,
        string windowId,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        (uint startResponseCode, IDictionary<string, object> startResults) =
            await RunWithoutSynchronizationContext(() =>
                    screenCast.StartAsync(
                        new ObjectPath(requestPaths.StartHandlePath),
                        new ObjectPath(requestPaths.SessionPath),
                        KdePortalAppId,
                        string.Empty,
                        new Dictionary<string, object>()
                    )
                )
                .WaitAsync(TimeSpan.FromMinutes(2), cancellationToken)
                .ConfigureAwait(false);
        if (startResponseCode != 0)
            return null;

        PortalRestoreData? restoreData = ExtractPortalRestoreData(startResults);

        string? restoreToken = ExtractPortalRestoreToken(startResults);
        if (restoreData is null && string.IsNullOrWhiteSpace(restoreToken)) { }

        return new KdePortalStartResult(startResults, restoreData, restoreToken);
    }

    private void PersistUpdatedKdeRestoreArtifacts(
        string windowId,
        PortalRestoreData? previousRestoreData,
        string? previousRestoreToken,
        PortalRestoreData? newRestoreData,
        string? newRestoreToken
    )
    {
        if (newRestoreData is PortalRestoreData typedNewRestoreData)
        {
            if (previousRestoreData is null)
            {
                SaveStoredWaylandScreenCastRestoreData(
                    windowId,
                    SerializePortalRestoreData(typedNewRestoreData)
                );
            }
            else if (
                !ArePortalRestoreDataEquivalent(previousRestoreData.Value, typedNewRestoreData)
            )
            {
                SaveStoredWaylandScreenCastRestoreData(
                    windowId,
                    SerializePortalRestoreData(typedNewRestoreData)
                );
            }
        }

        if (string.IsNullOrWhiteSpace(newRestoreToken))
            return;

        if (string.IsNullOrWhiteSpace(previousRestoreToken))
        {
            SaveStoredWaylandScreenCastRestoreToken(windowId, newRestoreToken);
        }
        else if (!string.Equals(previousRestoreToken, newRestoreToken, StringComparison.Ordinal))
        {
            SaveStoredWaylandScreenCastRestoreToken(windowId, newRestoreToken);
        }
    }

    private string? ResolveNodeIdFromPwDump(string windowId, bool allowBestCandidate)
    {
        if (!LinuxDependencies.IsPwDumpAvailable)
            return null;

        IReadOnlyList<NodeCandidate> nodes = GetPipeWireNodeCandidates();
        if (nodes.Count == 0)
            return null;

        string? windowTitle = TryGetWindowTitleById(windowId);
        IReadOnlyCollection<string> normalizedWindowIds = BuildWindowMatchPatterns(
            windowId,
            windowTitle
        );
        NodeCandidate? matching = FindBestMatchingNode(nodes, normalizedWindowIds);
        if (matching is null)
            return allowBestCandidate ? FindBestNode(nodes)?.Id : null;

        return matching.Value.Id;
    }

    private async Task<string?> WaitForNewPipeWireNodeIdAsync(
        HashSet<string> baselineIds,
        TimeSpan timeout,
        string windowId,
        IReadOnlyCollection<string> blockedNodeIds,
        CancellationToken cancellationToken = default
    )
    {
        string? windowTitle = TryGetWindowTitleById(windowId);
        IReadOnlyCollection<string> normalizedWindowIds = BuildWindowMatchPatterns(
            windowId,
            windowTitle
        );
        DateTime deadline = DateTime.UtcNow + timeout;
        NodeCandidate? bestNewCandidate = null;

        while (DateTime.UtcNow < deadline)
        {
            IReadOnlyList<NodeCandidate> current = GetPipeWireNodeCandidates(forceRefresh: true);
            NodeCandidate? matchingNew = FindBestNewMatchingNode(
                current,
                baselineIds,
                normalizedWindowIds,
                blockedNodeIds
            );
            if (matchingNew is not null)
                return matchingNew.Value.Id;

            NodeCandidate? bestThisRound = FindBestNewNode(current, baselineIds, blockedNodeIds);
            if (
                bestThisRound is not null
                && (
                    bestNewCandidate is null
                    || bestThisRound.Value.Score > bestNewCandidate.Value.Score
                )
            )
                bestNewCandidate = bestThisRound;

            try
            {
                await Task.Delay(PipeWireNodePollIntervalMs, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        return null;
    }

    private static bool MatchesWindowId(
        NodeCandidate candidate,
        IReadOnlyCollection<string> normalizedWindowIds
    )
    {
        if (normalizedWindowIds.Count == 0)
            return false;

        foreach (string normalizedWindowId in normalizedWindowIds)
        {
            if (
                candidate.NormalizedSearchText.Contains(
                    normalizedWindowId,
                    StringComparison.Ordinal
                )
            )
                return true;
        }

        return false;
    }

    private static int ComputePatternScore(
        string? normalizedSearchText,
        IReadOnlyCollection<string> patterns
    )
    {
        if (string.IsNullOrWhiteSpace(normalizedSearchText) || patterns.Count == 0)
            return 0;

        int score = 0;
        foreach (string pattern in patterns)
        {
            if (!normalizedSearchText.Contains(pattern, StringComparison.Ordinal))
                continue;

            score += Math.Max(1, pattern.Length);
        }

        return score;
    }

    private static bool MatchesRestoreDataWindowTitle(
        PortalRestoreData restoreData,
        string? requestedWindowTitle
    )
    {
        string normalizedRequestedWindowTitle = NormalizeForSearch(requestedWindowTitle);
        if (
            string.IsNullOrWhiteSpace(normalizedRequestedWindowTitle)
            || restoreData.Bytes.Length == 0
        )
            return false;

        string normalizedSearchText = BuildRestoreDataSearchText(restoreData);
        return normalizedSearchText.Contains(
            normalizedRequestedWindowTitle,
            StringComparison.Ordinal
        );
    }

    private static bool SerializedRestoreDataMatchesTitle(
        string serializedRestoreData,
        string? requestedWindowTitle
    )
    {
        if (
            !TryDeserializePortalRestoreData(
                serializedRestoreData,
                out PortalRestoreData parsedRestoreData
            )
        )
            return false;

        return MatchesRestoreDataWindowTitle(parsedRestoreData, requestedWindowTitle);
    }

    private static string BuildRestoreDataSearchText(PortalRestoreData restoreData)
    {
        if (restoreData.Bytes.Length == 0)
            return string.Empty;

        var builder = new StringBuilder(capacity: restoreData.Bytes.Length * 2);
        builder.Append(restoreData.Provider);
        AppendDecodedRestoreData(builder, restoreData.Bytes, Encoding.UTF8);
        AppendDecodedRestoreData(builder, restoreData.Bytes, Encoding.Unicode);
        AppendDecodedRestoreData(builder, restoreData.Bytes, Encoding.BigEndianUnicode);
        AppendDecodedRestoreData(builder, restoreData.Bytes, Encoding.Latin1);
        AppendAsciiAlphaNumericBytes(builder, restoreData.Bytes);
        return NormalizeForSearch(builder.ToString());
    }

    private static void AppendDecodedRestoreData(
        StringBuilder builder,
        byte[] bytes,
        Encoding encoding
    )
    {
        try
        {
            string decoded = encoding.GetString(bytes);
            if (string.IsNullOrWhiteSpace(decoded))
                return;

            builder.Append(' ');
            builder.Append(decoded);
        }
        catch (DecoderFallbackException)
        {
            // Best-effort decoding.
        }
        catch (ArgumentException)
        {
            // Best-effort decoding.
        }
    }

    private static void AppendAsciiAlphaNumericBytes(StringBuilder builder, byte[] bytes)
    {
        if (bytes.Length == 0)
            return;

        builder.Append(' ');
        for (int index = 0; index < bytes.Length; index++)
        {
            byte value = bytes[index];
            if (value is >= (byte)'A' and <= (byte)'Z')
            {
                builder.Append((char)(value + 32));
                continue;
            }

            if (
                (value is >= (byte)'a' and <= (byte)'z') || (value is >= (byte)'0' and <= (byte)'9')
            )
                builder.Append((char)value);
        }
    }

    private static PortalStreamDescriptor? FindBestMatchingPortalStream(
        IReadOnlyList<PortalStreamDescriptor> streams,
        IReadOnlyCollection<string> blockedNodeIds,
        IReadOnlyCollection<string> normalizedWindowIds
    )
    {
        if (streams.Count == 0 || normalizedWindowIds.Count == 0)
            return null;

        bool hasBest = false;
        PortalStreamDescriptor best = default;
        int bestScore = 0;

        for (int index = 0; index < streams.Count; index++)
        {
            PortalStreamDescriptor stream = streams[index];
            if (blockedNodeIds.Contains(stream.NodeId))
                continue;
            if (string.IsNullOrWhiteSpace(stream.NormalizedSearchText))
                continue;

            int score = ComputePatternScore(stream.NormalizedSearchText, normalizedWindowIds);
            if (score <= 0)
                continue;

            if (
                !hasBest
                || score > bestScore
                || (score == bestScore && CompareNodeId(stream.NodeId, best.NodeId) < 0)
            )
            {
                hasBest = true;
                best = stream;
                bestScore = score;
            }
        }

        return hasBest ? best : null;
    }

    private static IReadOnlyList<PortalStreamDescriptor> OrderPortalStreamsDeterministically(
        IReadOnlyList<PortalStreamDescriptor> streams
    )
    {
        if (streams.Count <= 1)
            return streams;

        return streams
            .OrderBy(stream => stream.NodeId, Comparer<string>.Create(CompareNodeId))
            .ToArray();
    }

    private static int CompareNodeId(string? leftNodeId, string? rightNodeId)
    {
        string left = leftNodeId?.Trim() ?? string.Empty;
        string right = rightNodeId?.Trim() ?? string.Empty;

        bool leftParsed = TryParseWindowIdAsUInt64(left, out ulong leftValue);
        bool rightParsed = TryParseWindowIdAsUInt64(right, out ulong rightValue);

        if (leftParsed && rightParsed)
            return leftValue.CompareTo(rightValue);
        if (leftParsed)
            return -1;
        if (rightParsed)
            return 1;

        return string.Compare(left, right, StringComparison.Ordinal);
    }

    private static string NormalizeForSearch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            char normalized = char.ToLowerInvariant(character);
            bool isAsciiAlphaNumeric =
                (normalized >= 'a' && normalized <= 'z')
                || (normalized >= '0' && normalized <= '9');
            if (isAsciiAlphaNumeric || normalized == 'x')
                builder.Append(normalized);
        }

        return builder.ToString();
    }

    private static IReadOnlyCollection<string> BuildWindowTitlePatterns(string? windowTitle)
    {
        var patterns = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(windowTitle))
            return patterns;

        AddNormalizedPattern(patterns, windowTitle);
        foreach (string token in ExtractWindowTitleTokens(windowTitle))
            AddNormalizedPattern(patterns, token);

        return patterns;
    }

    private static IReadOnlyCollection<string> BuildWindowMatchPatterns(
        string? windowId,
        string? windowTitle
    )
    {
        HashSet<string> patterns = BuildWindowIdPatterns(windowId)
            .ToHashSet(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(windowTitle))
            return patterns;

        AddNormalizedPattern(patterns, windowTitle);
        foreach (string token in ExtractWindowTitleTokens(windowTitle))
            AddNormalizedPattern(patterns, token);

        return patterns;
    }

    private bool IsWindowStillAvailable(string windowId)
    {
        if (string.IsNullOrWhiteSpace(windowId))
            return false;

        try
        {
            IReadOnlyCollection<WindowConfig> windows = _accessorBase.GetWindows();
            foreach (WindowConfig window in windows)
            {
                if (AreWindowIdsEquivalent(window.WindowId, windowId))
                    return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private string? TryGetWindowTitleById(string windowId)
    {
        if (string.IsNullOrWhiteSpace(windowId))
            return null;

        try
        {
            IReadOnlyCollection<WindowConfig> windows = _accessorBase.GetWindows();
            foreach (WindowConfig window in windows)
            {
                if (!AreWindowIdsEquivalent(window.WindowId, windowId))
                    continue;
                if (string.IsNullOrWhiteSpace(window.WindowTitle))
                    return null;
                return window.WindowTitle.Trim();
            }
        }
        catch
        {
            // Best-effort enrichment for window-title matching only.
        }

        return null;
    }

    private static bool AreWindowIdsEquivalent(string? leftWindowId, string? rightWindowId)
    {
        if (string.IsNullOrWhiteSpace(leftWindowId) || string.IsNullOrWhiteSpace(rightWindowId))
            return false;

        string left = leftWindowId.Trim();
        string right = rightWindowId.Trim();
        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!TryParseWindowIdAsUInt64(left, out ulong leftValue))
            return false;
        if (!TryParseWindowIdAsUInt64(right, out ulong rightValue))
            return false;

        return leftValue == rightValue;
    }

    private static bool TryParseWindowIdAsUInt64(string windowId, out ulong value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(windowId))
            return false;

        string normalized = windowId.Trim();
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return ulong.TryParse(
                normalized[2..],
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out value
            );
        }

        return ulong.TryParse(
            normalized,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value
        );
    }

    private static IReadOnlyList<string> ExtractWindowTitleTokens(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return Array.Empty<string>();

        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = new StringBuilder(capacity: 16);

        static void FlushCurrentToken(StringBuilder buffer, HashSet<string> destination)
        {
            if (buffer.Length < 4)
            {
                buffer.Clear();
                return;
            }

            destination.Add(buffer.ToString());
            buffer.Clear();
        }

        foreach (char character in title)
        {
            if (char.IsLetterOrDigit(character))
            {
                current.Append(character);
            }
            else
            {
                FlushCurrentToken(current, tokens);
            }
        }

        FlushCurrentToken(current, tokens);

        return tokens.OrderByDescending(token => token.Length).Take(4).ToArray();
    }

    private static IReadOnlyCollection<string> BuildWindowIdPatterns(string? windowId)
    {
        var patterns = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(windowId))
            return patterns;

        string trimmed = windowId.Trim();
        AddNormalizedPattern(patterns, trimmed);

        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (
                ulong.TryParse(
                    trimmed[2..],
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out ulong parsedHex
                )
            )
            {
                AddNormalizedPattern(patterns, $"0x{parsedHex:x}");
                AddNormalizedPattern(patterns, parsedHex.ToString(CultureInfo.InvariantCulture));
            }
        }
        else if (
            ulong.TryParse(
                trimmed,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out ulong parsedDec
            )
        )
        {
            AddNormalizedPattern(patterns, parsedDec.ToString(CultureInfo.InvariantCulture));
            AddNormalizedPattern(patterns, $"0x{parsedDec:x}");
        }

        return patterns;
    }

    private static void AddNormalizedPattern(HashSet<string> patterns, string value)
    {
        string normalized = NormalizeForSearch(value);
        if (!string.IsNullOrWhiteSpace(normalized))
            patterns.Add(normalized);
    }

    private static Bitmap? CreateBitmap(NativeBgraPreviewFrame frame)
    {
        if (frame.Data == IntPtr.Zero || frame.WidthPx <= 0 || frame.HeightPx <= 0)
            return null;

        try
        {
            var bitmap = new WriteableBitmap(
                new PixelSize(frame.WidthPx, frame.HeightPx),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Opaque
            );
            using ILockedFramebuffer framebuffer = bitmap.Lock();
            if (framebuffer.Address == IntPtr.Zero)
            {
                bitmap.Dispose();
                return null;
            }

            if (!frame.TryCopyTo(framebuffer.Address, framebuffer.RowBytes))
            {
                bitmap.Dispose();
                return null;
            }

            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static NodeCandidate? FindBestMatchingNode(
        IReadOnlyList<NodeCandidate> candidates,
        IReadOnlyCollection<string> normalizedWindowIds
    )
    {
        if (normalizedWindowIds.Count == 0)
            return null;

        NodeCandidate? bestMatch = null;
        for (int index = 0; index < candidates.Count; index++)
        {
            NodeCandidate candidate = candidates[index];
            if (!MatchesWindowId(candidate, normalizedWindowIds))
                continue;

            if (bestMatch is null || candidate.Score > bestMatch.Value.Score)
                bestMatch = candidate;
        }

        return bestMatch;
    }

    private static NodeCandidate? FindBestNode(IReadOnlyList<NodeCandidate> candidates)
    {
        NodeCandidate? best = null;
        for (int index = 0; index < candidates.Count; index++)
        {
            NodeCandidate candidate = candidates[index];
            if (best is null || candidate.Score > best.Value.Score)
                best = candidate;
        }

        return best;
    }

    private static NodeCandidate? FindBestNewNode(
        IReadOnlyList<NodeCandidate> candidates,
        HashSet<string> baselineIds,
        IReadOnlyCollection<string> blockedNodeIds
    )
    {
        NodeCandidate? best = null;
        for (int index = 0; index < candidates.Count; index++)
        {
            NodeCandidate candidate = candidates[index];
            if (baselineIds.Contains(candidate.Id))
                continue;
            if (blockedNodeIds.Contains(candidate.Id))
                continue;
            if (best is null || candidate.Score > best.Value.Score)
                best = candidate;
        }

        return best;
    }

    private static NodeCandidate? FindBestNewMatchingNode(
        IReadOnlyList<NodeCandidate> candidates,
        HashSet<string> baselineIds,
        IReadOnlyCollection<string> normalizedWindowIds,
        IReadOnlyCollection<string> blockedNodeIds
    )
    {
        NodeCandidate? best = null;
        for (int index = 0; index < candidates.Count; index++)
        {
            NodeCandidate candidate = candidates[index];
            if (baselineIds.Contains(candidate.Id))
                continue;
            if (blockedNodeIds.Contains(candidate.Id))
                continue;
            if (!MatchesWindowId(candidate, normalizedWindowIds))
                continue;

            if (best is null || candidate.Score > best.Value.Score)
                best = candidate;
        }

        return best;
    }

    private IReadOnlyList<NodeCandidate> GetPipeWireNodeCandidates(bool forceRefresh = false)
    {
        DateTime nowUtc = DateTime.UtcNow;
        lock (_nodeCacheSync)
        {
            bool hasCache = _cachedNodeCandidates.Count > 0;
            if (hasCache)
            {
                TimeSpan cacheAge = nowUtc - _nodeCandidatesCachedAtUtc;
                if (!forceRefresh && cacheAge < TimeSpan.FromMilliseconds(PipeWireNodeCacheTtlMs))
                    return _cachedNodeCandidates;

                if (
                    forceRefresh
                    && cacheAge < TimeSpan.FromMilliseconds(PipeWireForcedNodeRefreshCooldownMs)
                )
                    return _cachedNodeCandidates;
            }

            // If a refresh is already in progress, reuse the latest snapshot to avoid parallel pw-dump calls.
            if (_nodeCandidatesRefreshInProgress)
                return _cachedNodeCandidates;

            _nodeCandidatesRefreshInProgress = true;
        }

        IReadOnlyList<NodeCandidate> freshCandidates = Array.Empty<NodeCandidate>();
        try
        {
            freshCandidates = LoadPipeWireNodeCandidates();
        }
        catch
        {
            // Fallback to an empty snapshot on loader failures.
            freshCandidates = Array.Empty<NodeCandidate>();
        }

        lock (_nodeCacheSync)
        {
            _nodeCandidatesRefreshInProgress = false;
            _cachedNodeCandidates = freshCandidates;
            _nodeCandidatesCachedAtUtc = DateTime.UtcNow;
            return _cachedNodeCandidates;
        }
    }

    private IReadOnlyList<NodeCandidate> LoadPipeWireNodeCandidates()
    {
        string output = _pwDump.Execute(timeoutMs: 2_500);
        if (string.IsNullOrWhiteSpace(output))
            return Array.Empty<NodeCandidate>();

        var candidates = new List<NodeCandidate>(capacity: 16);
        try
        {
            using JsonDocument document = JsonDocument.Parse(output);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return candidates;

            foreach (JsonElement element in document.RootElement.EnumerateArray())
            {
                if (!element.TryGetProperty("type", out JsonElement typeElement))
                    continue;
                if (
                    !string.Equals(
                        typeElement.GetString(),
                        "PipeWire:Interface:Node",
                        StringComparison.Ordinal
                    )
                )
                    continue;
                if (!element.TryGetProperty("id", out JsonElement idElement))
                    continue;

                string nodeId = idElement.ToString();
                if (string.IsNullOrWhiteSpace(nodeId))
                    continue;

                int score = 0;
                string searchText = nodeId;
                if (
                    element.TryGetProperty("info", out JsonElement infoElement)
                    && infoElement.TryGetProperty("props", out JsonElement propsElement)
                )
                {
                    string mediaClass = GetPropertyValue(propsElement, "media.class");
                    string nodeName = GetPropertyValue(propsElement, "node.name");
                    string nodeDescription = GetPropertyValue(propsElement, "node.description");

                    if (mediaClass.StartsWith("Video/Source", StringComparison.OrdinalIgnoreCase))
                        score += 6;
                    if (
                        mediaClass.StartsWith(
                            "Stream/Output/Video",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                        score += 5;
                    if (mediaClass.Contains("video", StringComparison.OrdinalIgnoreCase))
                        score += 2;
                    if (
                        nodeName.Contains("portal", StringComparison.OrdinalIgnoreCase)
                        || nodeName.Contains("screencast", StringComparison.OrdinalIgnoreCase)
                        || nodeName.Contains("screen", StringComparison.OrdinalIgnoreCase)
                        || nodeName.Contains("monitor", StringComparison.OrdinalIgnoreCase)
                    )
                        score += 4;
                    if (nodeName.Contains("kwin", StringComparison.OrdinalIgnoreCase))
                        score += 2;
                    if (
                        nodeDescription.Contains("screencast", StringComparison.OrdinalIgnoreCase)
                        || nodeDescription.Contains("window", StringComparison.OrdinalIgnoreCase)
                        || nodeDescription.Contains("screen", StringComparison.OrdinalIgnoreCase)
                        || nodeDescription.Contains("monitor", StringComparison.OrdinalIgnoreCase)
                    )
                        score += 3;

                    searchText = BuildSearchText(propsElement, nodeId, nodeName, nodeDescription);
                }

                if (score > 0)
                {
                    string normalizedSearchText = NormalizeForSearch(searchText);
                    if (!string.IsNullOrWhiteSpace(normalizedSearchText))
                        candidates.Add(new NodeCandidate(nodeId, score, normalizedSearchText));
                }
            }
        }
        catch
        {
            return Array.Empty<NodeCandidate>();
        }

        return candidates;
    }

    private IReadOnlyCollection<string> SnapshotActiveNodeIds()
    {
        lock (_capturesSync)
        {
            if (_captures.Count == 0 && _pendingNodeIdsByWindow.Count == 0)
                return Array.Empty<string>();

            return _captures
                .Values.Select(capture => capture.NodeId)
                .Concat(_pendingNodeIdsByWindow.Values)
                .Where(nodeId => !string.IsNullOrWhiteSpace(nodeId))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
    }

    private void SetPendingNodeId(string windowId, string nodeId)
    {
        if (string.IsNullOrWhiteSpace(windowId) || string.IsNullOrWhiteSpace(nodeId))
            return;

        lock (_capturesSync)
            _pendingNodeIdsByWindow[windowId] = nodeId;
    }

    private void ClearPendingNodeId(string windowId)
    {
        if (string.IsNullOrWhiteSpace(windowId))
            return;

        lock (_capturesSync)
            _pendingNodeIdsByWindow.Remove(windowId);
    }

    private static string BuildSearchText(
        JsonElement propsElement,
        string nodeId,
        string nodeName,
        string nodeDescription
    )
    {
        var parts = new List<string> { nodeId, nodeName, nodeDescription };
        foreach (JsonProperty property in propsElement.EnumerateObject())
        {
            string value = GetJsonScalarString(property.Value);

            if (!string.IsNullOrWhiteSpace(value))
                parts.Add(value);
        }

        return string.Join('|', parts);
    }

    private static string GetPropertyValue(JsonElement propsElement, string propertyName)
    {
        if (!propsElement.TryGetProperty(propertyName, out JsonElement value))
            return string.Empty;

        return GetJsonScalarString(value);
    }

    private void ClosePortalSession(string sessionPath, string? sessionDestination = null)
    {
        if (string.IsNullOrWhiteSpace(sessionPath))
            return;

        _ = ClosePortalSessionBestEffortAsync(sessionPath, sessionDestination);
    }

    private void ClosePortalSessionSynchronously(
        string sessionPath,
        string? sessionDestination = null
    )
    {
        if (string.IsNullOrWhiteSpace(sessionPath))
            return;

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            RunWithoutSynchronizationContext(() =>
            {
                ClosePortalSessionAsync(sessionPath, sessionDestination, timeoutCts.Token)
                    .GetAwaiter()
                    .GetResult();
                return 0;
            });
        }
        catch
        {
            // Best effort close.
        }
    }

    private async Task ClosePortalSessionBestEffortAsync(
        string sessionPath,
        string? sessionDestination
    )
    {
        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await ClosePortalSessionAsync(sessionPath, sessionDestination, timeoutCts.Token)
                .ConfigureAwait(false);
        }
        catch
        {
            // Best effort close.
        }
    }

    private static void WaitForCaptureCreationTasksToComplete(
        IReadOnlyCollection<Task<WindowCaptureContext?>> captureCreationTasks
    )
    {
        if (captureCreationTasks.Count == 0)
            return;

        Task[] taskArray = captureCreationTasks.Select(static task => (Task)task).ToArray();
        try
        {
            _ = Task.WaitAll(taskArray, millisecondsTimeout: 2_000);
        }
        catch (AggregateException)
        {
            // Best effort shutdown.
        }

        for (int index = 0; index < taskArray.Length; index++)
        {
            Task task = taskArray[index];
            if (task.IsFaulted)
                _ = task.Exception;
        }
    }

    private static T RunWithoutSynchronizationContext<T>(Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        SynchronizationContext? previous = SynchronizationContext.Current;
        if (previous is null)
            return action();

        try
        {
            SynchronizationContext.SetSynchronizationContext(null);
            return action();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private async Task ClosePortalSessionAsync(
        string sessionPath,
        CancellationToken cancellationToken
    )
    {
        await ClosePortalSessionAsync(sessionPath, PortalDesktopDestination, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task ClosePortalSessionAsync(
        string sessionPath,
        string? sessionDestination,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(sessionPath))
            return;

        string destination = string.IsNullOrWhiteSpace(sessionDestination)
            ? PortalDesktopDestination
            : sessionDestination;

        Connection? connection = await EnsureSessionBusConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        if (connection is null)
            return;

        try
        {
            if (!string.Equals(destination, PortalDesktopDestination, StringComparison.Ordinal))
            {
                IKdePortalSession kdeSession = connection.CreateProxy<IKdePortalSession>(
                    destination,
                    new ObjectPath(sessionPath)
                );

                await RunWithoutSynchronizationContext(() => kdeSession.CloseAsync())
                    .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            IPipeWirePortalSession session = connection.CreateProxy<IPipeWirePortalSession>(
                PortalDesktopDestination,
                new ObjectPath(sessionPath)
            );

            await RunWithoutSynchronizationContext(() => session.CloseAsync())
                .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // Best effort close.
        }
    }

    private static bool IsWaylandSession()
    {
        string? sessionType = LinuxSessionDetector.GetSessionType();
        return string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsKdeDesktopSession()
    {
        string?[] candidates =
        [
            Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"),
            Environment.GetEnvironmentVariable("XDG_SESSION_DESKTOP"),
            Environment.GetEnvironmentVariable("DESKTOP_SESSION"),
        ];

        for (int index = 0; index < candidates.Length; index++)
        {
            string candidate = candidates[index] ?? string.Empty;
            if (
                candidate.Contains("kde", StringComparison.OrdinalIgnoreCase)
                || candidate.Contains("plasma", StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }
        }

        string kdeFullSession =
            Environment.GetEnvironmentVariable("KDE_FULL_SESSION") ?? string.Empty;
        return !string.IsNullOrWhiteSpace(kdeFullSession);
    }

    private bool IsKdePortalBackendAvailable()
    {
        if (!LinuxDependencies.IsGdbusAvailable)
            return false;

        try
        {
            string output = _gdbus.Execute(
                [
                    "introspect",
                    "--session",
                    "--dest",
                    KdePortalBackendDestination,
                    "--object-path",
                    "/org/freedesktop/portal/desktop",
                ],
                timeoutMs: 1_500
            );
            return output.Contains(
                "org.freedesktop.impl.portal.ScreenCast",
                StringComparison.Ordinal
            );
        }
        catch
        {
            return false;
        }
    }

    private static string GetJsonScalarString(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty,
        };
    }

    private readonly record struct KdePortalRequestPaths(
        string CreateHandlePath,
        string SelectHandlePath,
        string StartHandlePath,
        string SessionPath
    );

    private readonly record struct KdePortalStartResult(
        IDictionary<string, object> Results,
        PortalRestoreData? RestoreData,
        string? RestoreToken
    );

    private readonly record struct KdeStoredArtifacts(
        PortalRestoreData? RestoreData,
        string? RestoreToken,
        string? StreamStableId
    )
    {
        public bool HasAny =>
            RestoreData is not null
            || !string.IsNullOrWhiteSpace(RestoreToken)
            || !string.IsNullOrWhiteSpace(StreamStableId);
    }

    private readonly record struct PortalCaptureBootstrap(
        string NodeId,
        string SessionPath,
        string SessionDestination,
        CloseSafeHandle? PipeWireRemoteHandle
    );

    private readonly record struct PortalRequestResponse(
        uint ResponseCode,
        IDictionary<string, object> Results
    );

    private readonly record struct PortalRestoreData(string Provider, uint Version, byte[] Bytes);

    private readonly record struct WindowIdentity(
        string WindowTitle,
        string ProcessName,
        int TitleOrdinal,
        int ProcessTitleOrdinal,
        int TitleCount,
        int ProcessTitleCount
    );

    private readonly record struct PortalStreamDescriptor(
        string NodeId,
        string? StableId,
        string NormalizedSearchText
    );

    private readonly record struct NodeCandidate(string Id, int Score, string NormalizedSearchText);

    private enum MatchState
    {
        Unknown = 0,
        Match = 1,
        Mismatch = 2,
    }
}
