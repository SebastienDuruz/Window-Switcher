using System.Diagnostics;
using System.Buffers;
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
using WindowSwitcherLib.Data.Platform.Commands.Abstractions;
using WindowSwitcherLib.Data.Platform.Commands.Dependencies;
using WindowSwitcherLib.Data.Platform.Commands.Wrappers;
using WindowSwitcherLib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcherLib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcherLib.Data.Platform.WindowAccess.PreviewFrames.Pipewire.Abstractions;
using WindowSwitcherLib.Models;

namespace WindowSwitcherLib.Data.Platform.WindowAccess.PreviewFrames.Pipewire;

public sealed class PipeWireFrameProvider : IPreviewFrameProvider, IStreamingPreviewFrameProvider
{
    private const int PipeWireReconnectDelayMs = 300;
    private const int PipeWireNodePollIntervalMs = 300;
    private const int PipeWireNodeDiscoveryTimeoutMs = 20_000;
    private const int CaptureCreationTimeoutMs = 30_000;
    private const int PipeWireNodeCacheTtlMs = 500;
    private const int PipeWireForcedNodeRefreshCooldownMs = 1_000;
    private const int MaxConcurrentWaylandPortalSessionCreations = 1;
    private const int PipeWireReaderFrameIntervalMs = 33;
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
    private readonly Dictionary<string, HashSet<string>> _excludedNodesByWindow = new(
        StringComparer.Ordinal
    );
    private IReadOnlyList<NodeCandidate> _cachedNodeCandidates = Array.Empty<NodeCandidate>();
    private DateTime _nodeCandidatesCachedAtUtc = DateTime.MinValue;
    private bool _nodeCandidatesRefreshInProgress;
    private readonly bool _isWaylandSession;
    private readonly bool _isKdeDesktopSession;
    private Connection? _sessionBusConnection;
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

    public async IAsyncEnumerable<Bitmap> StreamAsync(
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

            long latestSequence = 0;
            while (
                !cancellationToken.IsCancellationRequested
                && !capture.IsDisposed
            )
            {
                capture.Stream.EnsureRunning();

                PipeWireWindowStream.FrameSnapshot? snapshot = await capture
                    .Stream.WaitForNextFrameAsync(latestSequence, request.TimeoutMs, cancellationToken)
                    .ConfigureAwait(false);

                if (snapshot is not null)
                {
                    latestSequence = snapshot.Value.Sequence;
                    Bitmap? bitmap;
                    try
                    {
                        bitmap = CreateBitmap(snapshot.Value);
                    }
                    finally
                    {
                        capture.Stream.ReleaseSnapshot(snapshot.Value);
                    }
                    if (bitmap is not null)
                    {
                        capture.ConsecutiveFailures = 0;
                        capture.ConsecutiveNoFrameTimeouts = 0;
                        ClearExcludedNodes(capture.WindowId);
                        yield return bitmap;
                        continue;
                    }
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
            _excludedNodesByWindow.Remove(windowId);
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

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        List<WindowCaptureContext> captures;
        List<CancellationTokenSource> captureCreationCancellations;
        lock (_capturesSync)
        {
            captures = _captures.Values.ToList();
            captureCreationCancellations = _captureCreationCancellationSources.Values.ToList();
            _captures.Clear();
            _captureCreationTasks.Clear();
            _captureCreationCancellationSources.Clear();
            _pendingNodeIdsByWindow.Clear();
            _excludedNodesByWindow.Clear();
        }

        foreach (WindowCaptureContext capture in captures)
            capture.Dispose(ClosePortalSession);
        foreach (CancellationTokenSource cancellation in captureCreationCancellations)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }
        lock (_dbusSync)
            _sessionBusConnection = null;
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private async Task<WindowCaptureContext?> EnsureCaptureAsync(
        string windowId,
        ScreenshotRequest request,
        CancellationToken cancellationToken
    )
    {
        WindowCaptureContext? staleCapture = null;
        Task<WindowCaptureContext?> createTask;
        lock (_capturesSync)
        {
            if (_captures.TryGetValue(windowId, out WindowCaptureContext? existing))
            {
                if (CaptureMatchesRequest(existing, request))
                    return existing;

                _captures.Remove(windowId);
                _pendingNodeIdsByWindow.Remove(windowId);
                staleCapture = existing;
            }

            if (!_captureCreationTasks.TryGetValue(windowId, out createTask!))
            {
                var createCancellationSource = new CancellationTokenSource();
                _captureCreationCancellationSources[windowId] = createCancellationSource;
                createTask = CreateAndRegisterCaptureAsync(
                    windowId,
                    request,
                    createCancellationSource.Token
                );
                _captureCreationTasks[windowId] = createTask;
            }
        }

        staleCapture?.Dispose(ClosePortalSession);

        try
        {
            return await createTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private async Task<WindowCaptureContext?> CreateAndRegisterCaptureAsync(
        string windowId,
        ScreenshotRequest request,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using var creationCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken
            );
            creationCts.CancelAfter(CaptureCreationTimeoutMs);
            WindowCaptureContext? created = await CreateCaptureAsync(
                    windowId,
                    request,
                    creationCts.Token
                )
                .ConfigureAwait(false);
            if (created is null)
            {
                ClearPendingNodeId(windowId);
                return null;
            }

            lock (_capturesSync)
            {
                if (_disposed)
                {
                    created.Dispose(ClosePortalSession);
                    return null;
                }

                if (_captures.TryGetValue(windowId, out WindowCaptureContext? existing))
                {
                    _pendingNodeIdsByWindow.Remove(windowId);
                    created.Dispose(ClosePortalSession);
                    return existing;
                }

                _pendingNodeIdsByWindow.Remove(windowId);
                _captures[windowId] = created;
                return created;
            }
        }
        catch (OperationCanceledException)
        {
            ClearPendingNodeId(windowId);
            return null;
        }
        catch (Exception)
        {
            ClearPendingNodeId(windowId);
            return null;
        }
        finally
        {
            CancellationTokenSource? captureCreationCancellation = null;
            lock (_capturesSync)
            {
                _captureCreationTasks.Remove(windowId);
                if (
                    _captureCreationCancellationSources.TryGetValue(
                        windowId,
                        out captureCreationCancellation
                    )
                )
                    _captureCreationCancellationSources.Remove(windowId);
            }

            captureCreationCancellation?.Dispose();
        }
    }

    private async Task<WindowCaptureContext?> CreateCaptureAsync(
        string windowId,
        ScreenshotRequest request,
        CancellationToken cancellationToken
    )
    {
        if (!IsWindowStillAvailable(windowId))
        {
            return null;
        }

        int? targetWidthPx = NormalizeTargetDimension(request.MaxWidthPx);
        int? targetHeightPx = NormalizeTargetDimension(request.MaxHeightPx);

        string? nodeId = null;
        string? portalSessionPath = null;
        string? portalSessionDestination = null;
        CloseSafeHandle? pipeWireRemoteHandle = null;
        IReadOnlyCollection<string> excludedNodeIds = SnapshotExcludedNodeIds(windowId);

        if (_isWaylandSession)
        {
            PortalCaptureBootstrap? started = null;

            if (_isKdeDesktopSession)
            {
                started = await TryStartKdeBackendWindowScreencastAsync(
                        windowId,
                        excludedNodeIds,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
            else
            {
                started = await TryStartWaylandPortalWindowScreencastAsync(
                        windowId,
                        excludedNodeIds,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }

            if (started is null)
            {
                return null;
            }

            nodeId = started.Value.NodeId;
            portalSessionPath = started.Value.SessionPath;
            portalSessionDestination = started.Value.SessionDestination;
            pipeWireRemoteHandle = started.Value.PipeWireRemoteHandle;
        }
        else
        {
            nodeId = ResolveNodeIdFromPwDump(windowId, excludedNodeIds, allowBestCandidate: false);
        }

        if (string.IsNullOrWhiteSpace(nodeId))
            return null;

        var stream = new PipeWireWindowStream(
            nodeId,
            _gstLaunch,
            pipeWireRemoteHandle,
            PipeWireReaderFrameIntervalMs,
            targetWidthPx,
            targetHeightPx
        );
        return new WindowCaptureContext(
            windowId,
            nodeId,
            portalSessionPath,
            portalSessionDestination,
            stream,
            targetWidthPx,
            targetHeightPx
        );
    }

    private string? GetStoredWaylandScreenCastRestoreToken(string windowId)
    {
        IReadOnlyList<string> restoreKeys = BuildWaylandRestoreKeys(windowId);
        if (restoreKeys.Count == 0)
            return null;

        (string? mappedToken, string? legacyToken, bool hasPerWindowMappings) = ConfigFileAccessor
            .GetInstance()
            .ReadConfig(config =>
            {
                for (int index = 0; index < restoreKeys.Count; index++)
                {
                    string key = restoreKeys[index];
                    if (
                        !config.LinuxWaylandScreenCastRestoreTokensByWindowId.TryGetValue(
                            key,
                            out string? existing
                        )
                    )
                        continue;
                    if (string.IsNullOrWhiteSpace(existing))
                        continue;
                    return (existing, config.LinuxWaylandScreenCastRestoreToken, true);
                }

                return (
                    (string?)null,
                    config.LinuxWaylandScreenCastRestoreToken,
                    config.LinuxWaylandScreenCastRestoreTokensByWindowId.Count > 0
                );
            });

        if (!string.IsNullOrWhiteSpace(mappedToken))
            return mappedToken.Trim();

        // Legacy single-token fallback is only safe when no per-window mappings exist yet.
        if (hasPerWindowMappings)
            return null;

        if (string.IsNullOrWhiteSpace(legacyToken))
            return null;

        return legacyToken.Trim();
    }

    private void SaveStoredWaylandScreenCastRestoreToken(string windowId, string restoreToken)
    {
        IReadOnlyList<string> restoreKeys = BuildWaylandRestoreKeys(windowId);
        if (restoreKeys.Count == 0)
            return;

        ArgumentException.ThrowIfNullOrWhiteSpace(restoreToken);

        string normalizedToken = restoreToken.Trim();
        ConfigFileAccessor configAccessor = ConfigFileAccessor.GetInstance();
        bool needsUpdate = configAccessor.ReadConfig(config =>
        {
            for (int index = 0; index < restoreKeys.Count; index++)
            {
                string key = restoreKeys[index];
                if (
                    !config.LinuxWaylandScreenCastRestoreTokensByWindowId.TryGetValue(
                        key,
                        out string? existing
                    ) || !string.Equals(existing, normalizedToken, StringComparison.Ordinal)
                )
                {
                    return true;
                }
            }

            return false;
        });
        if (!needsUpdate)
            return;

        configAccessor.UpdateConfig(config =>
        {
            for (int index = 0; index < restoreKeys.Count; index++)
                config.LinuxWaylandScreenCastRestoreTokensByWindowId[restoreKeys[index]] =
                    normalizedToken;
        });
        configAccessor.WriteUserSettings();
    }

    private string? GetStoredWaylandScreenCastRestoreData(string windowId)
    {
        IReadOnlyList<string> restoreKeys = BuildWaylandRestoreKeys(windowId);
        if (restoreKeys.Count == 0)
            return null;

        string? windowTitle = TryGetWindowTitleById(windowId);
        IReadOnlyCollection<string> windowTitlePatterns = BuildWindowTitlePatterns(windowTitle);
        string? configuredValue = ConfigFileAccessor
            .GetInstance()
            .ReadConfig(config =>
            {
                var preferredValues = new List<string>(capacity: restoreKeys.Count);
                for (int index = 0; index < restoreKeys.Count; index++)
                {
                    string key = restoreKeys[index];
                    if (
                        !config.LinuxWaylandScreenCastRestoreDataByWindowId.TryGetValue(
                            key,
                            out string? value
                        )
                    )
                        continue;
                    if (string.IsNullOrWhiteSpace(value))
                        continue;
                    preferredValues.Add(value.Trim());
                }

                if (preferredValues.Count == 0)
                    return null;

                if (windowTitlePatterns.Count == 0)
                    return preferredValues[0];

                for (int index = 0; index < preferredValues.Count; index++)
                {
                    string candidate = preferredValues[index];
                    if (SerializedRestoreDataMatchesTitle(candidate, windowTitlePatterns))
                        return candidate;
                }

                return null;
            });

        if (string.IsNullOrWhiteSpace(configuredValue))
            return null;

        return configuredValue.Trim();
    }

    private void SaveStoredWaylandScreenCastRestoreData(
        string windowId,
        string serializedRestoreData
    )
    {
        IReadOnlyList<string> restoreKeys = BuildWaylandRestoreKeys(windowId);
        if (restoreKeys.Count == 0 || string.IsNullOrWhiteSpace(serializedRestoreData))
            return;

        string normalizedValue = serializedRestoreData.Trim();
        ConfigFileAccessor configAccessor = ConfigFileAccessor.GetInstance();
        bool needsUpdate = configAccessor.ReadConfig(config =>
        {
            for (int index = 0; index < restoreKeys.Count; index++)
            {
                string key = restoreKeys[index];
                if (
                    !config.LinuxWaylandScreenCastRestoreDataByWindowId.TryGetValue(
                        key,
                        out string? existing
                    ) || !string.Equals(existing, normalizedValue, StringComparison.Ordinal)
                )
                {
                    return true;
                }
            }

            return false;
        });
        if (!needsUpdate)
            return;

        configAccessor.UpdateConfig(config =>
        {
            for (int index = 0; index < restoreKeys.Count; index++)
                config.LinuxWaylandScreenCastRestoreDataByWindowId[restoreKeys[index]] =
                    normalizedValue;
        });
        configAccessor.WriteUserSettings();
    }

    private string? GetStoredWaylandScreenCastStreamId(string windowId)
    {
        IReadOnlyList<string> restoreKeys = BuildWaylandRestoreKeys(windowId);
        if (restoreKeys.Count == 0)
            return null;

        string? configuredId = ConfigFileAccessor
            .GetInstance()
            .ReadConfig(config =>
            {
                for (int index = 0; index < restoreKeys.Count; index++)
                {
                    string key = restoreKeys[index];
                    if (
                        !config.LinuxWaylandScreenCastStreamIdsByWindowId.TryGetValue(
                            key,
                            out string? value
                        )
                    )
                        continue;
                    if (string.IsNullOrWhiteSpace(value))
                        continue;
                    return value;
                }

                return null;
            });

        if (string.IsNullOrWhiteSpace(configuredId))
            return null;

        return configuredId.Trim();
    }

    private void SaveStoredWaylandScreenCastStreamId(string windowId, string streamId)
    {
        IReadOnlyList<string> restoreKeys = BuildWaylandRestoreKeys(windowId);
        if (restoreKeys.Count == 0 || string.IsNullOrWhiteSpace(streamId))
            return;

        string normalizedStreamId = streamId.Trim();
        ConfigFileAccessor configAccessor = ConfigFileAccessor.GetInstance();
        bool needsUpdate = configAccessor.ReadConfig(config =>
        {
            for (int index = 0; index < restoreKeys.Count; index++)
            {
                string key = restoreKeys[index];
                if (
                    !config.LinuxWaylandScreenCastStreamIdsByWindowId.TryGetValue(
                        key,
                        out string? existing
                    ) || !string.Equals(existing, normalizedStreamId, StringComparison.Ordinal)
                )
                {
                    return true;
                }
            }

            return false;
        });
        if (!needsUpdate)
            return;

        configAccessor.UpdateConfig(config =>
        {
            for (int index = 0; index < restoreKeys.Count; index++)
                config.LinuxWaylandScreenCastStreamIdsByWindowId[restoreKeys[index]] =
                    normalizedStreamId;
        });
        configAccessor.WriteUserSettings();
    }

    private void ClearStoredWaylandScreenCastArtifacts(string windowId)
    {
        IReadOnlyList<string> restoreKeys = BuildWaylandRestoreKeys(windowId);
        if (restoreKeys.Count == 0)
            return;

        ConfigFileAccessor configAccessor = ConfigFileAccessor.GetInstance();
        bool changed = false;
        configAccessor.UpdateConfig(config =>
        {
            for (int index = 0; index < restoreKeys.Count; index++)
            {
                string key = restoreKeys[index];
                changed |= config.LinuxWaylandScreenCastRestoreTokensByWindowId.Remove(key);
                changed |= config.LinuxWaylandScreenCastRestoreDataByWindowId.Remove(key);
                changed |= config.LinuxWaylandScreenCastStreamIdsByWindowId.Remove(key);
            }

            if (
                !string.IsNullOrWhiteSpace(config.LinuxWaylandScreenCastRestoreToken)
                && config.LinuxWaylandScreenCastRestoreTokensByWindowId.Count == 0
            )
            {
                config.LinuxWaylandScreenCastRestoreToken = string.Empty;
                changed = true;
            }
        });

        if (changed)
            configAccessor.WriteUserSettings();
    }

    private IReadOnlyList<string> BuildWaylandRestoreKeys(string windowId)
    {
        var keys = new List<string>(capacity: 8);
        var uniqueKeys = new HashSet<string>(StringComparer.Ordinal);

        void AddRestoreKey(string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            string normalized = key.Trim().ToLowerInvariant();
            if (uniqueKeys.Add(normalized))
                keys.Add(normalized);
        }

        if (TryGetWindowIdentity(windowId, out WindowIdentity identity))
        {
            string normalizedTitle = NormalizeIdentityPart(identity.WindowTitle);
            string normalizedProcess = NormalizeIdentityPart(identity.ProcessName);

            if (
                !string.IsNullOrWhiteSpace(normalizedProcess)
                && !string.IsNullOrWhiteSpace(normalizedTitle)
            )
            {
                if (identity.ProcessTitleOrdinal > 0)
                {
                    AddRestoreKey(
                        $"process_title:{normalizedProcess}|{normalizedTitle}#{identity.ProcessTitleOrdinal.ToString(CultureInfo.InvariantCulture)}"
                    );
                }

                if (identity.ProcessTitleCount <= 1)
                {
                    AddRestoreKey($"process_title:{normalizedProcess}|{normalizedTitle}");
                }
            }

            if (!string.IsNullOrWhiteSpace(normalizedTitle))
            {
                if (identity.TitleOrdinal > 0)
                    AddRestoreKey(
                        $"title:{normalizedTitle}#{identity.TitleOrdinal.ToString(CultureInfo.InvariantCulture)}"
                    );

                if (identity.TitleCount <= 1)
                    AddRestoreKey($"title:{normalizedTitle}");
            }
        }

        string normalizedWindowId = NormalizeWindowId(windowId);
        AddRestoreKey(normalizedWindowId);

        return keys;
    }

    private bool TryGetWindowIdentity(string windowId, out WindowIdentity identity)
    {
        identity = default;
        if (string.IsNullOrWhiteSpace(windowId))
            return false;

        IReadOnlyCollection<WindowConfig> windows;
        try
        {
            windows = _accessorBase.GetWindows();
        }
        catch
        {
            return false;
        }

        if (windows.Count == 0)
            return false;

        WindowConfig? currentWindow = windows.FirstOrDefault(window =>
            AreWindowIdsEquivalent(window.WindowId, windowId)
        );
        if (currentWindow is null)
            return false;

        string title = currentWindow.WindowTitle?.Trim() ?? string.Empty;
        string processName = currentWindow.ProcessName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(processName))
            return false;

        int titleOrdinal = 0;
        int titleCount = 0;
        if (!string.IsNullOrWhiteSpace(title))
        {
            string normalizedTitle = NormalizeIdentityPart(title);
            List<WindowConfig> sameTitleWindows = windows
                .Where(window =>
                    string.Equals(
                        NormalizeIdentityPart(window.WindowTitle),
                        normalizedTitle,
                        StringComparison.Ordinal
                    )
                )
                .OrderBy(window => NormalizeWindowId(window.WindowId))
                .ToList();
            titleCount = sameTitleWindows.Count;

            titleOrdinal =
                sameTitleWindows
                    .Select((window, index) => new { window, ordinal = index + 1 })
                    .FirstOrDefault(item => AreWindowIdsEquivalent(item.window.WindowId, windowId))
                    ?.ordinal
                ?? 0;
        }

        int processTitleOrdinal = 0;
        int processTitleCount = 0;
        if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(processName))
        {
            string normalizedTitle = NormalizeIdentityPart(title);
            string normalizedProcess = NormalizeIdentityPart(processName);
            List<WindowConfig> sameProcessTitleWindows = windows
                .Where(window =>
                    string.Equals(
                        NormalizeIdentityPart(window.WindowTitle),
                        normalizedTitle,
                        StringComparison.Ordinal
                    )
                    && string.Equals(
                        NormalizeIdentityPart(window.ProcessName),
                        normalizedProcess,
                        StringComparison.Ordinal
                    )
                )
                .OrderBy(window => NormalizeWindowId(window.WindowId))
                .ToList();
            processTitleCount = sameProcessTitleWindows.Count;

            processTitleOrdinal =
                sameProcessTitleWindows
                    .Select((window, index) => new { window, ordinal = index + 1 })
                    .FirstOrDefault(item => AreWindowIdsEquivalent(item.window.WindowId, windowId))
                    ?.ordinal
                ?? 0;
        }

        identity = new WindowIdentity(
            title,
            processName,
            titleOrdinal,
            processTitleOrdinal,
            titleCount,
            processTitleCount
        );
        return true;
    }

    private static string NormalizeIdentityPart(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
    }

    private static string NormalizeWindowId(string windowId)
    {
        return string.IsNullOrWhiteSpace(windowId)
            ? string.Empty
            : windowId.Trim().ToLowerInvariant();
    }

    private static int? NormalizeTargetDimension(int? value)
    {
        if (!value.HasValue || value.Value <= 0)
            return null;

        return value.Value;
    }

    private static bool CaptureMatchesRequest(
        WindowCaptureContext capture,
        ScreenshotRequest request
    )
    {
        ArgumentNullException.ThrowIfNull(capture);

        int? requestWidth = NormalizeTargetDimension(request.MaxWidthPx);
        int? requestHeight = NormalizeTargetDimension(request.MaxHeightPx);
        return capture.TargetWidthPx == requestWidth && capture.TargetHeightPx == requestHeight;
    }

    private async Task<Bitmap?> TryRequestCaptureFrameAsync(
        WindowCaptureContext capture,
        ScreenshotRequest request,
        CancellationToken cancellationToken
    )
    {
        if (capture.IsDisposed)
            return null;

        capture.Stream.EnsureRunning();

        int timeoutMs = request.TimeoutMs;
        Bitmap? frame = await capture
            .Stream.GetFrameAsync(timeoutMs, cancellationToken)
            .ConfigureAwait(false);
        if (frame is not null)
        {
            capture.ConsecutiveFailures = 0;
            capture.ConsecutiveNoFrameTimeouts = 0;
            ClearExcludedNodes(capture.WindowId);
            return frame;
        }

        bool needsRestart = capture.Stream.NeedsRestart();
        if (!needsRestart)
        {
            capture.ConsecutiveNoFrameTimeouts++;
            bool waitingForFirstWaylandFrame =
                _isWaylandSession && !capture.Stream.HasReceivedFrame();
            if (capture.ConsecutiveNoFrameTimeouts < 2 && !waitingForFirstWaylandFrame)
                return null;
        }
        else
        {
            capture.ConsecutiveNoFrameTimeouts = 0;
        }

        try
        {
            await Task.Delay(Math.Min(PipeWireReconnectDelayMs, timeoutMs), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        capture.Stream.Restart();
        frame = await capture
            .Stream.GetFrameAsync(timeoutMs, cancellationToken)
            .ConfigureAwait(false);
        if (frame is not null)
        {
            capture.ConsecutiveFailures = 0;
            capture.ConsecutiveNoFrameTimeouts = 0;
            ClearExcludedNodes(capture.WindowId);
            return frame;
        }

        if (_isWaylandSession && !capture.Stream.HasReceivedFrame())
        {
            int bootstrapTimeoutMs = Math.Max(timeoutMs, 1_200);
            frame = await capture
                .Stream.GetFrameAsync(bootstrapTimeoutMs, cancellationToken)
                .ConfigureAwait(false);
            if (frame is not null)
            {
                capture.ConsecutiveFailures = 0;
                capture.ConsecutiveNoFrameTimeouts = 0;
                ClearExcludedNodes(capture.WindowId);
                return frame;
            }

            capture.ConsecutiveFailures++;
            return null;
        }

        capture.ConsecutiveFailures++;
        if (capture.ConsecutiveFailures < 3)
            return null;

        capture.Stream.Restart();
        capture.ConsecutiveFailures = 0;
        capture.ConsecutiveNoFrameTimeouts = 0;
        return null;
    }

    private async Task<bool> TryRecoverCaptureAsync(
        WindowCaptureContext capture,
        int timeoutMs,
        CancellationToken cancellationToken
    )
    {
        bool needsRestart = capture.Stream.NeedsRestart();
        if (!needsRestart)
        {
            capture.ConsecutiveNoFrameTimeouts++;
            if (capture.ConsecutiveNoFrameTimeouts < 3)
                return true;
        }
        else
        {
            capture.ConsecutiveNoFrameTimeouts = 0;
        }

        try
        {
            await Task.Delay(Math.Min(PipeWireReconnectDelayMs, timeoutMs), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        capture.Stream.Restart();
        if (!capture.Stream.NeedsRestart())
        {
            if (_isWaylandSession && !capture.Stream.HasReceivedFrame())
            {
                capture.ConsecutiveFailures++;
                if (capture.ConsecutiveFailures >= 3)
                {
                    capture.Stream.Restart();
                    capture.ConsecutiveFailures = 0;
                    capture.ConsecutiveNoFrameTimeouts = 0;
                }

                return true;
            }

            capture.ConsecutiveFailures = 0;
            capture.ConsecutiveNoFrameTimeouts = 0;
            return true;
        }

        capture.ConsecutiveFailures++;
        if (capture.ConsecutiveFailures >= 3)
        {
            capture.Stream.Restart();
            capture.ConsecutiveFailures = 0;
            capture.ConsecutiveNoFrameTimeouts = 0;
        }

        return true;
    }

    private async Task<PortalCaptureBootstrap?> TryStartWaylandPortalWindowScreencastAsync(
        string windowId,
        IReadOnlyCollection<string> excludedNodeIds,
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
            ObjectPath createRequestPath = await screenCast
                .CreateSessionAsync(
                    new Dictionary<string, object>
                    {
                        ["handle_token"] = $"ws_create_{token}",
                        ["session_handle_token"] = sessionToken,
                    }
                )
                .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken)
                .ConfigureAwait(false);

            PortalRequestResponse? createResponse = await WaitForPortalRequestResponseAsync(
                    connection,
                    createRequestPath,
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
            ObjectPath selectRequestPath = await screenCast
                .SelectSourcesAsync(sessionObjectPath, selectOptions)
                .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken)
                .ConfigureAwait(false);

            PortalRequestResponse? selectResponse = await WaitForPortalRequestResponseAsync(
                    connection,
                    selectRequestPath,
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
            ObjectPath startRequestPath = await screenCast
                .StartAsync(
                    sessionObjectPath,
                    string.Empty,
                    new Dictionary<string, object> { ["handle_token"] = $"ws_start_{token}" }
                )
                .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken)
                .ConfigureAwait(false);

            PortalRequestResponse? startResponse = await WaitForPortalRequestResponseAsync(
                    connection,
                    startRequestPath,
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
            if (string.IsNullOrWhiteSpace(newRestoreToken))
            {
            }
            else
            {
                SaveStoredWaylandScreenCastRestoreToken(windowId, newRestoreToken);
            }

            string? nodeId = SelectPortalStreamNodeId(
                windowId,
                startResponse.Value.Results,
                excludedNodeIds,
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
            pipeWireRemoteHandle = await screenCast
                .OpenPipeWireRemoteAsync(sessionObjectPath, new Dictionary<string, object>())
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
            _sessionBusConnection ??= Connection.Session;
            connection = _sessionBusConnection;
        }

        try
        {
            await connection
                .ConnectAsync()
                .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken)
                .ConfigureAwait(false);
            return connection;
        }
        catch (Exception)
        {
            return null;
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
            watcher = await request
                .WatchResponseAsync(response =>
                {
                    IDictionary<string, object> safeResults =
                        response.Results ?? new Dictionary<string, object>();
                    _ = completion.TrySetResult(
                        new PortalRequestResponse(response.Response, safeResults)
                    );
                })
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
        IReadOnlyCollection<string> excludedNodeIds,
        out string? selectedStreamStableId,
        out bool shouldPersistSelectedStreamStableId
    )
    {
        selectedStreamStableId = null;
        shouldPersistSelectedStreamStableId = false;

        IReadOnlyList<PortalStreamDescriptor> streams = ExtractPortalStreams(results);
        if (streams.Count == 0)
            return null;

        var allExcludedNodeIds = new HashSet<string>(excludedNodeIds, StringComparer.Ordinal);
        foreach (string activeNodeId in SnapshotActiveNodeIds())
            _ = allExcludedNodeIds.Add(activeNodeId);

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
            && MatchesRestoreDataWindowTitle(typedRestoreData, windowTitlePatterns);
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
                    !allExcludedNodeIds.Contains(stream.NodeId)
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
            if (windowTitlePatterns.Count == 0)
                return MatchState.Unknown;

            if (ComputePatternScore(stream.NormalizedSearchText, windowTitlePatterns) > 0)
                return MatchState.Match;

            NodeCandidate? candidate = FindNodeCandidate(stream.NodeId);
            if (candidate is null)
                return MatchState.Unknown;

            return MatchesWindowId(candidate.Value, windowTitlePatterns)
                ? MatchState.Match
                : MatchState.Mismatch;
        }

        bool clearedStoredArtifacts = false;
        void ClearStoredArtifactsForCurrentAttempt()
        {
            ClearStoredWaylandScreenCastArtifacts(windowId);
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
                if (allExcludedNodeIds.Contains(stream.NodeId))
                    continue;
                MatchState storedStreamMatchState = EvaluateWindowTitleMatchState(stream);
                if (windowTitlePatterns.Count > 0 && storedStreamMatchState is MatchState.Mismatch)
                {
                    bool allowSingleMismatchByRestoreData =
                        streams.Count == 1
                        && restoreDataMatchesRequestedWindowTitle
                        && !IsLikelyAssignedToAnotherWindow(stream);
                    if (allowSingleMismatchByRestoreData)
                    {
                    }
                    else
                    {
                        ClearStoredArtifactsForCurrentAttempt();
                        continue;
                    }
                }

                if (windowTitlePatterns.Count > 0 && storedStreamMatchState is MatchState.Unknown)
                {
                }

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
            PortalStreamDescriptor? matchedByPortalMetadata = FindBestMatchingPortalStream(
                streams,
                allExcludedNodeIds,
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
            var matchingCandidates = new List<NodeCandidate>(capacity: streams.Count);
            for (int index = 0; index < nodes.Count; index++)
            {
                NodeCandidate node = nodes[index];
                if (!candidateIds.Contains(node.Id))
                    continue;
                if (allExcludedNodeIds.Contains(node.Id))
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
            if (allExcludedNodeIds.Contains(stream.NodeId))
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

        if (windowTitlePatterns.Count > 0)
        {
        }

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
        IReadOnlyCollection<string> excludedNodeIds,
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
                excludedNodeIds,
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

            var allExcludedNodeIds = new HashSet<string>(
                excludedNodeIds,
                StringComparer.Ordinal
            );
            foreach (string activeNodeId in SnapshotActiveNodeIds())
                _ = allExcludedNodeIds.Add(activeNodeId);

            string? discoveredNodeId = await WaitForNewPipeWireNodeIdAsync(
                baselineIds,
                TimeSpan.FromMilliseconds(PipeWireNodeDiscoveryTimeoutMs),
                windowId,
                allExcludedNodeIds,
                cancellationToken
            ).ConfigureAwait(false);
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
        (uint createResponseCode, IDictionary<string, object> _) = await screenCast
            .CreateSessionAsync(
                new ObjectPath(requestPaths.CreateHandlePath),
                new ObjectPath(requestPaths.SessionPath),
                KdePortalAppId,
                new Dictionary<string, object>()
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
            else
            {
            }
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
        (uint selectResponseCode, IDictionary<string, object> _) = await screenCast
            .SelectSourcesAsync(
                new ObjectPath(requestPaths.SelectHandlePath),
                new ObjectPath(requestPaths.SessionPath),
                KdePortalAppId,
                selectOptions
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
        (uint startResponseCode, IDictionary<string, object> startResults) = await screenCast
            .StartAsync(
                new ObjectPath(requestPaths.StartHandlePath),
                new ObjectPath(requestPaths.SessionPath),
                KdePortalAppId,
                string.Empty,
                new Dictionary<string, object>()
            )
            .WaitAsync(TimeSpan.FromMinutes(2), cancellationToken)
            .ConfigureAwait(false);
        if (startResponseCode != 0)
            return null;

        PortalRestoreData? restoreData = ExtractPortalRestoreData(startResults);

        string? restoreToken = ExtractPortalRestoreToken(startResults);
        if (restoreData is null && string.IsNullOrWhiteSpace(restoreToken))
        {
        }

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

    private string? ResolveNodeIdFromPwDump(
        string windowId,
        IReadOnlyCollection<string> excludedNodeIds,
        bool allowBestCandidate
    )
    {
        if (!LinuxDependencies.IsPwDumpAvailable)
            return null;

        IReadOnlyList<NodeCandidate> nodes = GetPipeWireNodeCandidates();
        IReadOnlyList<NodeCandidate> availableNodes = FilterExcludedNodes(nodes, excludedNodeIds);
        if (availableNodes.Count == 0)
            return null;

        string? windowTitle = TryGetWindowTitleById(windowId);
        IReadOnlyCollection<string> normalizedWindowIds = BuildWindowMatchPatterns(
            windowId,
            windowTitle
        );
        NodeCandidate? matching = FindBestMatchingNode(availableNodes, normalizedWindowIds);
        if (matching is null)
            return allowBestCandidate ? FindBestNode(availableNodes)?.Id : null;

        return matching.Value.Id;
    }

    private async Task<string?> WaitForNewPipeWireNodeIdAsync(
        HashSet<string> baselineIds,
        TimeSpan timeout,
        string windowId,
        IReadOnlyCollection<string> excludedNodeIds,
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
                excludedNodeIds
            );
            if (matchingNew is not null)
                return matchingNew.Value.Id;

            NodeCandidate? bestThisRound = FindBestNewNode(current, baselineIds, excludedNodeIds);
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
        IReadOnlyCollection<string> requestedWindowTitlePatterns
    )
    {
        if (requestedWindowTitlePatterns.Count == 0 || restoreData.Bytes.Length == 0)
            return false;

        string normalizedSearchText = BuildRestoreDataSearchText(restoreData);
        return ComputePatternScore(normalizedSearchText, requestedWindowTitlePatterns) > 0;
    }

    private static bool SerializedRestoreDataMatchesTitle(
        string serializedRestoreData,
        IReadOnlyCollection<string> requestedWindowTitlePatterns
    )
    {
        if (
            !TryDeserializePortalRestoreData(
                serializedRestoreData,
                out PortalRestoreData parsedRestoreData
            )
        )
            return false;

        return MatchesRestoreDataWindowTitle(parsedRestoreData, requestedWindowTitlePatterns);
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
        IReadOnlyCollection<string> excludedNodeIds,
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
            if (excludedNodeIds.Contains(stream.NodeId))
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

    private static Bitmap? CreateBitmap(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap? CreateBitmap(PipeWireWindowStream.FrameSnapshot snapshot)
    {
        if (snapshot.Format == PipeWireWindowStream.FrameFormat.Bgra32)
        {
            return CreateBitmapFromBgra(snapshot.Bytes, snapshot.WidthPx, snapshot.HeightPx);
        }

        return CreateBitmap(snapshot.Bytes);
    }

    private static Bitmap? CreateBitmapFromBgra(byte[] bytes, int widthPx, int heightPx)
    {
        if (bytes.Length == 0 || widthPx <= 0 || heightPx <= 0)
            return null;

        int srcStride;
        int requiredBytes;
        try
        {
            srcStride = checked(widthPx * 4);
            requiredBytes = checked(srcStride * heightPx);
        }
        catch (OverflowException)
        {
            return null;
        }

        if (bytes.Length < requiredBytes)
            return null;

        try
        {
            var bitmap = new WriteableBitmap(
                new PixelSize(widthPx, heightPx),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Opaque
            );
            using ILockedFramebuffer framebuffer = bitmap.Lock();
            IntPtr destination = framebuffer.Address;
            if (destination == IntPtr.Zero)
            {
                bitmap.Dispose();
                return null;
            }

            int destinationStride = framebuffer.RowBytes;
            if (destinationStride == srcStride)
            {
                Marshal.Copy(bytes, 0, destination, requiredBytes);
            }
            else
            {
                for (int row = 0; row < heightPx; row++)
                {
                    Marshal.Copy(
                        bytes,
                        row * srcStride,
                        IntPtr.Add(destination, row * destinationStride),
                        srcStride
                    );
                }
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
        IReadOnlyCollection<string> excludedNodeIds
    )
    {
        NodeCandidate? best = null;
        for (int index = 0; index < candidates.Count; index++)
        {
            NodeCandidate candidate = candidates[index];
            if (baselineIds.Contains(candidate.Id))
                continue;
            if (excludedNodeIds.Contains(candidate.Id))
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
        IReadOnlyCollection<string> excludedNodeIds
    )
    {
        NodeCandidate? best = null;
        for (int index = 0; index < candidates.Count; index++)
        {
            NodeCandidate candidate = candidates[index];
            if (baselineIds.Contains(candidate.Id))
                continue;
            if (excludedNodeIds.Contains(candidate.Id))
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
                if (
                    !forceRefresh
                    && cacheAge < TimeSpan.FromMilliseconds(PipeWireNodeCacheTtlMs)
                )
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

    private static IReadOnlyList<NodeCandidate> FilterExcludedNodes(
        IReadOnlyList<NodeCandidate> candidates,
        IReadOnlyCollection<string> excludedNodeIds
    )
    {
        if (excludedNodeIds.Count == 0)
            return candidates;

        var filtered = new List<NodeCandidate>(capacity: candidates.Count);
        for (int index = 0; index < candidates.Count; index++)
        {
            NodeCandidate candidate = candidates[index];
            if (!excludedNodeIds.Contains(candidate.Id))
                filtered.Add(candidate);
        }

        return filtered;
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

    private IReadOnlyCollection<string> SnapshotExcludedNodeIds(string windowId)
    {
        if (string.IsNullOrWhiteSpace(windowId))
            return Array.Empty<string>();

        lock (_capturesSync)
        {
            if (
                !_excludedNodesByWindow.TryGetValue(windowId, out HashSet<string>? excludedNodeIds)
                || excludedNodeIds.Count == 0
            )
                return Array.Empty<string>();

            return excludedNodeIds.ToArray();
        }
    }

    private void ClearExcludedNodes(string windowId)
    {
        if (string.IsNullOrWhiteSpace(windowId))
            return;

        lock (_capturesSync)
        {
            _excludedNodesByWindow.Remove(windowId);
        }
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

                await kdeSession
                    .CloseAsync()
                    .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            IPipeWirePortalSession session = connection.CreateProxy<IPipeWirePortalSession>(
                PortalDesktopDestination,
                new ObjectPath(sessionPath)
            );

            await session
                .CloseAsync()
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

    private sealed class WindowCaptureContext(
        string windowId,
        string nodeId,
        string? portalSessionPath,
        string? portalSessionDestination,
        PipeWireWindowStream stream,
        int? targetWidthPx,
        int? targetHeightPx
    )
    {
        public string WindowId { get; } = windowId;
        public string NodeId { get; } = nodeId;
        public string? PortalSessionPath { get; } = portalSessionPath;
        public string? PortalSessionDestination { get; } = portalSessionDestination;
        public PipeWireWindowStream Stream { get; } = stream;
        public int? TargetWidthPx { get; } = targetWidthPx;
        public int? TargetHeightPx { get; } = targetHeightPx;
        public int ConsecutiveFailures { get; set; }
        public int ConsecutiveNoFrameTimeouts { get; set; }
        public bool IsDisposed { get; private set; }

        public void Dispose(Action<string, string?> closePortalSession)
        {
            if (IsDisposed)
                return;

            IsDisposed = true;
            Stream.Dispose();
            if (!string.IsNullOrWhiteSpace(PortalSessionPath))
                closePortalSession(PortalSessionPath, PortalSessionDestination);
        }
    }

    private sealed class PipeWireWindowStream : IDisposable
    {
        private const int MaxFrameBytes = 16 * 1024 * 1024;

        public enum FrameFormat
        {
            Jpeg = 0,
            Bgra32 = 1,
        }

        public readonly record struct FrameSnapshot(
            long Sequence,
            byte[] Bytes,
            FrameFormat Format,
            int WidthPx,
            int HeightPx,
            FrameBufferLease? Lease
        );

        public sealed class FrameBufferLease(ArrayPool<byte> pool, byte[] buffer)
        {
            private byte[]? _buffer = buffer;
            private int _refCount = 1;

            public byte[] Buffer => _buffer ?? Array.Empty<byte>();

            public void AddRef()
            {
                _ = Interlocked.Increment(ref _refCount);
            }

            public void Release()
            {
                if (Interlocked.Decrement(ref _refCount) != 0)
                    return;

                byte[]? released = Interlocked.Exchange(ref _buffer, null);
                if (released is null)
                    return;

                pool.Return(released);
            }
        }

        private readonly string _nodeId;
        private readonly IGstLaunchWrapper _gstLaunch;
        private readonly CloseSafeHandle? _pipeWireRemoteHandle;
        private readonly int _minFrameIntervalMs;
        private readonly object _syncRoot = new();
        private readonly SemaphoreSlim _frameReadySignal = new(initialCount: 0, maxCount: 1);
        private readonly ArrayPool<byte> _framePool = ArrayPool<byte>.Shared;

        private Process? _process;
        private CancellationTokenSource? _cts;
        private Task? _readerTask;
        private byte[]? _latestFrameBytes;
        private FrameBufferLease? _latestFrameLease;
        private FrameFormat _latestFrameFormat;
        private int _latestFrameWidthPx;
        private int _latestFrameHeightPx;
        private long _latestFrameSequence;
        private long _activeGeneration;
        private bool _hasReceivedFrame;
        private bool _disposed;
        private bool _faulted;
        private bool _restartInProgress;

        public PipeWireWindowStream(
            string nodeId,
            IGstLaunchWrapper gstLaunch,
            CloseSafeHandle? pipeWireRemoteHandle,
            int minFrameIntervalMs,
            int? maxWidthPx,
            int? maxHeightPx
        )
        {
            ArgumentNullException.ThrowIfNull(gstLaunch);
            _nodeId = nodeId;
            _gstLaunch = gstLaunch;
            _pipeWireRemoteHandle = pipeWireRemoteHandle;
            _minFrameIntervalMs = minFrameIntervalMs;
            int? normalizedMaxWidthPx = NormalizeTargetDimension(maxWidthPx);
            int? normalizedMaxHeightPx = NormalizeTargetDimension(maxHeightPx);
            _rawFrameWidthPx = normalizedMaxWidthPx ?? DefaultRawFrameWidthPx;
            _rawFrameHeightPx = normalizedMaxHeightPx ?? DefaultRawFrameHeightPx;
        }

        private readonly int _rawFrameWidthPx;
        private readonly int _rawFrameHeightPx;
        private const int DefaultRawFrameWidthPx = 640;
        private const int DefaultRawFrameHeightPx = 360;

        public void EnsureRunning()
        {
            lock (_syncRoot)
            {
                if (_disposed)
                    return;

                if (_process is not null && !_process.HasExited && !_faulted)
                    return;
            }

            Restart();
        }

        public bool NeedsRestart()
        {
            lock (_syncRoot)
            {
                if (_disposed)
                    return false;

                return _faulted || _process is null || _process.HasExited;
            }
        }

        public bool HasReceivedFrame()
        {
            lock (_syncRoot)
            {
                return _hasReceivedFrame;
            }
        }

        public void Restart()
        {
            lock (_syncRoot)
            {
                if (_disposed || _restartInProgress)
                    return;
                _restartInProgress = true;
            }

            Stop();

            int? remoteFd = GetPipeWireRemoteFd();
            Process? process = null;
            if (
                _rawFrameWidthPx > 0
                && _rawFrameHeightPx > 0
                && TryComputeRawFrameByteCount(_rawFrameWidthPx, _rawFrameHeightPx, out int rawBytes)
                && rawBytes <= MaxFrameBytes
            )
            {
                process = _gstLaunch.StartPipeWireRawBgraStream(
                    _nodeId,
                    _rawFrameWidthPx,
                    _rawFrameHeightPx,
                    remoteFd
                );
            }
            var cts = new CancellationTokenSource();
            if (process is null)
            {
                lock (_syncRoot)
                {
                    _faulted = true;
                    _restartInProgress = false;
                }

                cts.Dispose();
                return;
            }

            long generation;
            lock (_syncRoot)
            {
                _activeGeneration++;
                generation = _activeGeneration;
                _faulted = false;
                _hasReceivedFrame = false;
                _latestFrameFormat = FrameFormat.Bgra32;
                _latestFrameWidthPx = _rawFrameWidthPx;
                _latestFrameHeightPx = _rawFrameHeightPx;
                _process = process;
                _cts = cts;
                _readerTask = Task.Run(
                    () =>
                        ReadRawLoop(
                            process,
                            cts.Token,
                            generation,
                            _rawFrameWidthPx,
                            _rawFrameHeightPx
                        )
                );
                _ = Task.Run(() => DrainErrors(process, cts.Token));
                _restartInProgress = false;
            }


            DrainFrameSignal();
        }

        public async Task<Bitmap?> GetFrameAsync(int timeoutMs, CancellationToken cancellationToken)
        {
            bool hasFiniteTimeout = timeoutMs >= 0;
            long deadlineMs = hasFiniteTimeout ? Environment.TickCount64 + timeoutMs : 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                FrameSnapshot? snapshot = GetFrameSnapshotAfter(-1);
                if (snapshot is not null)
                {
                    try
                    {
                        return CreateBitmap(snapshot.Value);
                    }
                    finally
                    {
                        ReleaseSnapshot(snapshot.Value);
                    }
                }

                if (IsFaulted())
                    return null;

                try
                {
                    if (hasFiniteTimeout)
                    {
                        long remainingMs = deadlineMs - Environment.TickCount64;
                        if (remainingMs <= 0)
                            return null;

                        bool signaled = await _frameReadySignal
                            .WaitAsync(
                                millisecondsTimeout: remainingMs > int.MaxValue
                                    ? int.MaxValue
                                    : (int)remainingMs,
                                cancellationToken
                            )
                            .ConfigureAwait(false);
                        if (!signaled)
                            return null;
                    }
                    else
                    {
                        await _frameReadySignal.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
                catch (ObjectDisposedException)
                {
                    return null;
                }
            }

            return null;
        }

        public void ReleaseSnapshot(FrameSnapshot snapshot)
        {
            snapshot.Lease?.Release();
        }

        public async Task<FrameSnapshot?> WaitForNextFrameAsync(
            long afterSequence,
            int timeoutMs,
            CancellationToken cancellationToken
        )
        {
            bool hasFiniteTimeout = timeoutMs >= 0;
            long deadlineMs = hasFiniteTimeout ? Environment.TickCount64 + timeoutMs : 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                FrameSnapshot? snapshot = GetFrameSnapshotAfter(afterSequence);
                if (snapshot is not null)
                    return snapshot;

                if (IsFaulted())
                    return null;

                try
                {
                    if (hasFiniteTimeout)
                    {
                        long remainingMs = deadlineMs - Environment.TickCount64;
                        if (remainingMs <= 0)
                            return null;

                        bool signaled = await _frameReadySignal
                            .WaitAsync(
                                millisecondsTimeout: remainingMs > int.MaxValue
                                    ? int.MaxValue
                                    : (int)remainingMs,
                                cancellationToken
                            )
                            .ConfigureAwait(false);
                        if (!signaled)
                            return null;
                    }
                    else
                    {
                        await _frameReadySignal.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
                catch (ObjectDisposedException)
                {
                    return null;
                }
            }

            return null;
        }

        private FrameSnapshot? GetFrameSnapshotAfter(long afterSequence)
        {
            lock (_syncRoot)
            {
                if (_latestFrameBytes is null || _latestFrameSequence <= afterSequence)
                    return null;

                FrameBufferLease? lease = _latestFrameLease;
                lease?.AddRef();

                return new FrameSnapshot(
                    _latestFrameSequence,
                    _latestFrameBytes,
                    _latestFrameFormat,
                    _latestFrameWidthPx,
                    _latestFrameHeightPx,
                    lease
                );
            }
        }

        private static bool TryComputeRawFrameByteCount(
            int frameWidthPx,
            int frameHeightPx,
            out int frameByteCount
        )
        {
            frameByteCount = 0;
            if (frameWidthPx <= 0 || frameHeightPx <= 0)
                return false;

            try
            {
                int stride = checked(frameWidthPx * 4);
                frameByteCount = checked(stride * frameHeightPx);
                return frameByteCount > 0;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private static bool TryReadExact(Stream stream, byte[] buffer, int length)
        {
            int offset = 0;
            while (offset < length)
            {
                int read = stream.Read(buffer, offset, length - offset);
                if (read <= 0)
                    return false;

                offset += read;
            }

            return true;
        }

        private bool IsFaulted()
        {
            lock (_syncRoot)
            {
                return _faulted;
            }
        }

        private async Task DrainErrors(Process process, CancellationToken cancellationToken)
        {
            try
            {
                string stderr = await process
                    .StandardError.ReadToEndAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    string reduced = stderr.Length > 4_000 ? stderr[..4_000] : stderr;
                }
            }
            catch { }
        }

        private void ReadRawLoop(
            Process process,
            CancellationToken cancellationToken,
            long generation,
            int frameWidthPx,
            int frameHeightPx
        )
        {
            try
            {
                if (
                    !TryComputeRawFrameByteCount(
                        frameWidthPx,
                        frameHeightPx,
                        out int frameByteCount
                    )
                    || frameByteCount > MaxFrameBytes
                )
                {
                    return;
                }

                byte[] readBuffer = new byte[frameByteCount];
                Stream output = process.StandardOutput.BaseStream;
                long nextAcceptedFrameAtMs = 0;

                while (!cancellationToken.IsCancellationRequested)
                {
                    if (!TryReadExact(output, readBuffer, frameByteCount))
                        break;

                    long now = Environment.TickCount64;
                    if (_minFrameIntervalMs > 0 && now < nextAcceptedFrameAtMs)
                        continue;

                    byte[] frame = _framePool.Rent(frameByteCount);
                    try
                    {
                        Buffer.BlockCopy(readBuffer, 0, frame, 0, frameByteCount);
                        var lease = new FrameBufferLease(_framePool, frame);
                        FrameBufferLease? previousLease;

                        lock (_syncRoot)
                        {
                            previousLease = _latestFrameLease;
                            _latestFrameLease = lease;
                            _latestFrameBytes = frame;
                            _latestFrameFormat = FrameFormat.Bgra32;
                            _latestFrameWidthPx = frameWidthPx;
                            _latestFrameHeightPx = frameHeightPx;
                            _latestFrameSequence++;
                            _hasReceivedFrame = true;
                        }

                        previousLease?.Release();
                    }
                    catch
                    {
                        _framePool.Return(frame);
                        throw;
                    }

                    SignalFrameReady();

                    if (_minFrameIntervalMs > 0)
                        nextAcceptedFrameAtMs = now + _minFrameIntervalMs;
                }
            }
            catch (Exception) { }
            finally
            {
                lock (_syncRoot)
                {
                    // Ignore stale readers from older generations after a restart.
                    if (_activeGeneration == generation)
                        _faulted = true;
                }
                SignalFrameReady();
            }
        }

        private void ReadJpegLoop(Process process, CancellationToken cancellationToken, long generation)
        {
            try
            {
                var frameBuffer = new List<byte>(256 * 1024);
                byte[] readBuffer = new byte[16 * 1024];
                Stream output = process.StandardOutput.BaseStream;

                bool inFrame = false;
                bool dropCurrentFrame = false;
                byte previous = 0;
                long nextAcceptedFrameAtMs = 0;

                while (!cancellationToken.IsCancellationRequested)
                {
                    int read = output.Read(readBuffer, 0, readBuffer.Length);
                    if (read <= 0)
                        break;

                    for (int i = 0; i < read; i++)
                    {
                        byte current = readBuffer[i];

                        if (!inFrame)
                        {
                            if (previous == 0xFF && current == 0xD8)
                            {
                                inFrame = true;
                                long now = Environment.TickCount64;
                                dropCurrentFrame =
                                    _minFrameIntervalMs > 0 && now < nextAcceptedFrameAtMs;
                                frameBuffer.Clear();
                                if (!dropCurrentFrame)
                                {
                                    frameBuffer.Add(0xFF);
                                    frameBuffer.Add(0xD8);
                                }
                            }

                            previous = current;
                            continue;
                        }

                        if (!dropCurrentFrame)
                            frameBuffer.Add(current);

                        if (!dropCurrentFrame && frameBuffer.Count > MaxFrameBytes)
                        {
                            inFrame = false;
                            dropCurrentFrame = false;
                            frameBuffer.Clear();
                            previous = current;
                            continue;
                        }

                        if (previous == 0xFF && current == 0xD9)
                        {
                            if (!dropCurrentFrame)
                            {
                                byte[] frame = frameBuffer.ToArray();
                                FrameBufferLease? previousLease;
                                lock (_syncRoot)
                                {
                                    previousLease = _latestFrameLease;
                                    _latestFrameLease = null;
                                    _latestFrameBytes = frame;
                                    _latestFrameFormat = FrameFormat.Jpeg;
                                    _latestFrameWidthPx = 0;
                                    _latestFrameHeightPx = 0;
                                    _latestFrameSequence++;
                                    _hasReceivedFrame = true;
                                }
                                previousLease?.Release();
                                SignalFrameReady();

                                if (_minFrameIntervalMs > 0)
                                    nextAcceptedFrameAtMs =
                                        Environment.TickCount64 + _minFrameIntervalMs;
                            }

                            inFrame = false;
                            dropCurrentFrame = false;
                            frameBuffer.Clear();
                        }

                        previous = current;
                    }
                }
            }
            catch (Exception) { }
            finally
            {
                lock (_syncRoot)
                {
                    // Ignore stale readers from older generations after a restart.
                    if (_activeGeneration == generation)
                        _faulted = true;
                }
                SignalFrameReady();
            }
        }

        private void Stop()
        {
            Process? process;
            CancellationTokenSource? cts;
            FrameBufferLease? latestLease;

            lock (_syncRoot)
            {
                _activeGeneration++;
                process = _process;
                cts = _cts;
                latestLease = _latestFrameLease;
                _process = null;
                _cts = null;
                _readerTask = null;
                _latestFrameBytes = null;
                _latestFrameLease = null;
                _latestFrameFormat = FrameFormat.Jpeg;
                _latestFrameWidthPx = 0;
                _latestFrameHeightPx = 0;
                _hasReceivedFrame = false;
                _faulted = true;
            }

            latestLease?.Release();

            SignalFrameReady();
            DrainFrameSignal();

            if (cts is not null)
            {
                try
                {
                    cts.Cancel();
                }
                catch { }
                cts.Dispose();
            }

            if (process is not null)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch { }
                process.Dispose();
            }

        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Stop();
            _pipeWireRemoteHandle?.Dispose();
            _frameReadySignal.Dispose();
        }

        private int? GetPipeWireRemoteFd()
        {
            if (
                _pipeWireRemoteHandle is null
                || _pipeWireRemoteHandle.IsClosed
                || _pipeWireRemoteHandle.IsInvalid
            )
                return null;

            long value = _pipeWireRemoteHandle.DangerousGetHandle().ToInt64();
            return value is >= 0 and <= int.MaxValue ? (int)value : null;
        }

        private void SignalFrameReady()
        {
            try
            {
                if (_frameReadySignal.CurrentCount == 0)
                    _frameReadySignal.Release();
            }
            catch
            {
                // Dispose/shutdown path.
            }
        }

        private void DrainFrameSignal()
        {
            try
            {
                while (_frameReadySignal.Wait(0)) { }
            }
            catch
            {
                // Dispose/shutdown path.
            }
        }
    }
}
