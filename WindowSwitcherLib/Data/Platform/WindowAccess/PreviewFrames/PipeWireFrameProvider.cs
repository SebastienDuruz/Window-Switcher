using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Media.Imaging;
using Tmds.DBus;
using WindowSwitcherLib.Data;
using WindowSwitcherLib.Data.Platform.Commands.Abstractions;
using WindowSwitcherLib.Data.Platform.Commands.Dependencies;
using WindowSwitcherLib.Data.Platform.Commands.Wrappers;
using WindowSwitcherLib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcherLib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcherLib.Models;

namespace WindowSwitcherLib.Data.Platform.WindowAccess.PreviewFrames;

public sealed class PipeWireFrameProvider : IPreviewFrameProvider, IStreamingPreviewFrameProvider
{
    private const int PipeWireReconnectDelayMs = 300;
    private const int PipeWireNodePollIntervalMs = 300;
    private const int PipeWireNodeDiscoveryTimeoutMs = 20_000;
    private const int CaptureCreationTimeoutMs = 30_000;
    private const int PipeWireNodeCacheTtlMs = 500;
    private const bool EnablePortalFallback = true;
    private const string PortalDesktopDestination = "org.freedesktop.portal.Desktop";
    private const string KdePortalBackendDestination = "org.freedesktop.impl.portal.desktop.kde";
    private const string KdePortalScreenCastInterface = "org.freedesktop.impl.portal.ScreenCast";
    private const string KdePortalAppId = "windowswitcher";
    private static readonly Regex ObjectPathRegex = new(@"'(/org/[^']+)'", RegexOptions.Compiled);
    private static readonly Regex PortalCallReplyCodeRegex =
        new(@"\(\s*uint32\s+(?<code>\d+)\s*,", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex PortalResponseRegex =
        new(@"Response\s*\(\s*uint32\s+(?<code>\d+)\s*,\s*(?<payload>.*)\)\s*$", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex PortalSessionHandleRegex =
        new(@"'session_handle'\s*:\s*<'(?<path>/org/[^']+)'>", RegexOptions.Compiled);
    private static readonly Regex PortalStreamsRegex =
        new(@"'streams'\s*:\s*<\[(?<streams>.*)\]>", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex PortalStreamNodeIdRegex =
        new(@"\(\s*uint32\s+(?<id>\d+)\s*,", RegexOptions.Compiled);

    private readonly ScreenshotPreviewFrameProvider _fallbackProvider;
    private readonly WinAccessorBase _accessorBase;
    private readonly IPwDumpWrapper _pwDump;
    private readonly IGdbusWrapper _gdbus;
    private readonly IGstLaunchWrapper _gstLaunch;
    private readonly object _capturesSync = new();
    private readonly object _nodeCacheSync = new();
    private readonly object _dbusSync = new();
    private readonly SemaphoreSlim _waylandPortalSessionGate = new(initialCount: 1, maxCount: 1);
    private readonly Dictionary<string, WindowCaptureContext> _captures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<WindowCaptureContext?>> _captureCreationTasks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _pendingNodeIdsByWindow = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _excludedNodesByWindow = new(StringComparer.Ordinal);
    private readonly HashSet<string> _failedWindows = new(StringComparer.Ordinal);
    private IReadOnlyList<NodeCandidate> _cachedNodeCandidates = Array.Empty<NodeCandidate>();
    private DateTime _nodeCandidatesCachedAtUtc = DateTime.MinValue;
    private readonly bool _isWaylandSession;
    private readonly bool _isKdeDesktopSession;
    private Connection? _sessionBusConnection;
    private bool _disposed;

    public PipeWireFrameProvider(
        WinAccessorBase accessorBase,
        IPwDumpWrapper? pwDump = null,
        IGdbusWrapper? gdbus = null,
        IGstLaunchWrapper? gstLaunch = null)
    {
        ArgumentNullException.ThrowIfNull(accessorBase);

        _accessorBase = accessorBase;
        _fallbackProvider = new ScreenshotPreviewFrameProvider(accessorBase);
        _pwDump = pwDump ?? new PwDumpWrapper();
        _gdbus = gdbus ?? new GdbusWrapper();
        _gstLaunch = gstLaunch ?? new GstLaunchWrapper();

        _isWaylandSession = IsWaylandSession();
        _isKdeDesktopSession = IsKdeDesktopSession() || IsKdePortalBackendAvailable();
    }

    public async Task<Bitmap?> RequestAsync(string windowId, ScreenshotRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_disposed)
                return null;

            if (string.IsNullOrWhiteSpace(windowId) || !RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return await RequestFallbackAsync(windowId, request, cancellationToken).ConfigureAwait(false);

            WindowCaptureContext? capture = await EnsureCaptureAsync(windowId, cancellationToken).ConfigureAwait(false);
            if (capture is null || capture.ForceFallback)
                return _isWaylandSession
                    ? null
                    : await RequestFallbackAsync(windowId, request, cancellationToken).ConfigureAwait(false);

            Bitmap? frame = await TryRequestCaptureFrameAsync(capture, request, cancellationToken).ConfigureAwait(false);
            if (frame is not null)
                return frame;

            return _isWaylandSession
                ? null
                : await RequestFallbackAsync(windowId, request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception)
        {
            return _isWaylandSession
                ? null
                : await RequestFallbackAsync(windowId, request, cancellationToken).ConfigureAwait(false);
        }
    }

    public async IAsyncEnumerable<Bitmap> StreamAsync(
        string windowId,
        ScreenshotRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_disposed)
            yield break;

        if (string.IsNullOrWhiteSpace(windowId) || !RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            await foreach (Bitmap fallbackFrame in StreamFallbackAsync(windowId, request, cancellationToken))
                yield return fallbackFrame;
            yield break;
        }

        int timeoutMs = Math.Clamp(request.TimeoutMs, 100, 30_000);
        while (!cancellationToken.IsCancellationRequested && !_disposed)
        {
            WindowCaptureContext? capture = await EnsureCaptureAsync(windowId, cancellationToken).ConfigureAwait(false);
            if (capture is null || capture.ForceFallback)
            {
                if (_isWaylandSession)
                {
                    try
                    {
                        await Task.Delay(PipeWireNodePollIntervalMs, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        yield break;
                    }
                    continue;
                }

                await foreach (Bitmap fallbackFrame in StreamFallbackAsync(windowId, request, cancellationToken))
                    yield return fallbackFrame;
                yield break;
            }

            long latestSequence = 0;
            while (!cancellationToken.IsCancellationRequested && !capture.IsDisposed && !capture.ForceFallback)
            {
                capture.Stream.EnsureRunning();

                PipeWireWindowStream.FrameSnapshot? snapshot = await capture.Stream
                    .WaitForNextFrameAsync(latestSequence, timeoutMs, cancellationToken)
                    .ConfigureAwait(false);

                if (snapshot is not null)
                {
                    latestSequence = snapshot.Value.Sequence;
                    Bitmap? bitmap = CreateBitmap(snapshot.Value.Bytes);
                    if (bitmap is not null)
                    {
                        if (!capture.FirstDeliveredFrameLogged)
                        {
                            string fingerprint = ComputeFrameFingerprint(snapshot.Value.Bytes);
                            PipeWireTrace.Write(
                                $"WaylandPreview frame delivered windowId={capture.WindowId} nodeId={capture.NodeId} seq={snapshot.Value.Sequence} sha256={fingerprint}");
                            capture.FirstDeliveredFrameLogged = true;
                        }

                        capture.ConsecutiveFailures = 0;
                        capture.ConsecutiveNoFrameTimeouts = 0;
                        ClearExcludedNodes(capture.WindowId);
                        yield return bitmap;
                        continue;
                    }
                }

                bool keepStreaming = await TryRecoverCaptureAsync(capture, timeoutMs, cancellationToken).ConfigureAwait(false);
                if (!keepStreaming)
                    break;
            }

            if (!_isWaylandSession)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    await foreach (Bitmap fallbackFrame in StreamFallbackAsync(windowId, request, cancellationToken))
                        yield return fallbackFrame;
                }

                yield break;
            }
        }
    }

    public void ForgetWindow(string windowId)
    {
        if (string.IsNullOrWhiteSpace(windowId))
            return;

        _fallbackProvider.ForgetWindow(windowId);

        WindowCaptureContext? capture = null;
        lock (_capturesSync)
        {
            _failedWindows.Remove(windowId);
            _excludedNodesByWindow.Remove(windowId);
            _pendingNodeIdsByWindow.Remove(windowId);
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
        lock (_capturesSync)
        {
            captures = _captures.Values.ToList();
            _captures.Clear();
            _captureCreationTasks.Clear();
            _pendingNodeIdsByWindow.Clear();
            _failedWindows.Clear();
            _excludedNodesByWindow.Clear();
        }

        foreach (WindowCaptureContext capture in captures)
            capture.Dispose(ClosePortalSession);

        _fallbackProvider.Dispose();
        lock (_dbusSync)
            _sessionBusConnection = null;
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        await _fallbackProvider.DisposeAsync().ConfigureAwait(false);
    }

    private async Task<WindowCaptureContext?> EnsureCaptureAsync(string windowId, CancellationToken cancellationToken)
    {
        Task<WindowCaptureContext?> createTask;
        lock (_capturesSync)
        {
            if (!_isWaylandSession && _failedWindows.Contains(windowId))
                return null;

            if (_captures.TryGetValue(windowId, out WindowCaptureContext? existing))
                return existing;

            if (!_captureCreationTasks.TryGetValue(windowId, out createTask!))
            {
                createTask = CreateAndRegisterCaptureAsync(windowId);
                _captureCreationTasks[windowId] = createTask;
            }
        }

        try
        {
            return await createTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private async Task<WindowCaptureContext?> CreateAndRegisterCaptureAsync(string windowId)
    {
        try
        {
            using var creationCts = new CancellationTokenSource(CaptureCreationTimeoutMs);
            PipeWireTrace.Write($"EnsureCaptureAsync create requested windowId={windowId} wayland={_isWaylandSession}");
            WindowCaptureContext? created = await CreateCaptureAsync(windowId, creationCts.Token).ConfigureAwait(false);
            if (created is null)
            {
                ClearPendingNodeId(windowId);
                PipeWireTrace.Write($"EnsureCaptureAsync create failed windowId={windowId}");
                if (!_isWaylandSession)
                {
                    lock (_capturesSync)
                        _failedWindows.Add(windowId);
                }

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

                _failedWindows.Remove(windowId);
                _pendingNodeIdsByWindow.Remove(windowId);
                _captures[windowId] = created;
                PipeWireTrace.Write($"EnsureCaptureAsync create success windowId={windowId} nodeId={created.NodeId}");
                return created;
            }
        }
        catch (OperationCanceledException)
        {
            ClearPendingNodeId(windowId);
            PipeWireTrace.Write($"EnsureCaptureAsync create cancelled windowId={windowId}");
            return null;
        }
        catch (Exception exception)
        {
            ClearPendingNodeId(windowId);
            PipeWireTrace.Write($"EnsureCaptureAsync create exception windowId={windowId} type={exception.GetType().Name} message={exception.Message}");
            return null;
        }
        finally
        {
            lock (_capturesSync)
                _captureCreationTasks.Remove(windowId);
        }
    }

    private async Task<WindowCaptureContext?> CreateCaptureAsync(string windowId, CancellationToken cancellationToken)
    {
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
                    cancellationToken).ConfigureAwait(false);
            }

            if (started is null && excludedNodeIds.Count > 0)
            {
                ClearExcludedNodes(windowId);
                if (_isKdeDesktopSession)
                {
                    started = await TryStartKdeBackendWindowScreencastAsync(
                        windowId,
                        Array.Empty<string>(),
                        cancellationToken).ConfigureAwait(false);
                }
            }

            if (!_isKdeDesktopSession)
            {
                started ??= await TryStartWaylandPortalWindowScreencastAsync(
                        windowId,
                        excludedNodeIds,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (started is null && excludedNodeIds.Count > 0)
                {
                    ClearExcludedNodes(windowId);
                    started = await TryStartWaylandPortalWindowScreencastAsync(
                            windowId,
                            Array.Empty<string>(),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            if (started is null)
            {
                if (_isKdeDesktopSession)
                    PipeWireTrace.Write($"WaylandKdeBackend no capture available windowId={windowId} (no portal-desktop fallback)");
                return null;
            }

            nodeId = started.Value.NodeId;
            portalSessionPath = started.Value.SessionPath;
            portalSessionDestination = started.Value.SessionDestination;
            pipeWireRemoteHandle = started.Value.PipeWireRemoteHandle;
            PipeWireTrace.Write(
                $"CreateCaptureAsync wayland bootstrap ok windowId={windowId} nodeId={nodeId} sessionPath={portalSessionPath} destination={portalSessionDestination ?? "null"}");
        }
        else
        {
            nodeId = ResolveNodeIdFromPwDump(windowId, excludedNodeIds, allowBestCandidate: false);
            if (string.IsNullOrWhiteSpace(nodeId) && EnablePortalFallback)
            {
                nodeId = TryStartPortalWindowScreencast(windowId, excludedNodeIds, out portalSessionPath);
                if (!string.IsNullOrWhiteSpace(portalSessionPath))
                    portalSessionDestination = PortalDesktopDestination;
            }

            if (string.IsNullOrWhiteSpace(nodeId) && excludedNodeIds.Count > 0)
            {
                ClearExcludedNodes(windowId);
                nodeId = ResolveNodeIdFromPwDump(windowId, Array.Empty<string>(), allowBestCandidate: false);
                if (string.IsNullOrWhiteSpace(nodeId) && EnablePortalFallback)
                {
                    nodeId = TryStartPortalWindowScreencast(windowId, Array.Empty<string>(), out portalSessionPath);
                    if (!string.IsNullOrWhiteSpace(portalSessionPath))
                        portalSessionDestination = PortalDesktopDestination;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(nodeId))
            return null;

        double configuredFps = GetConfiguredLinuxPreviewRefreshRateFps();
        PreviewRefreshRateSettings.ToFraction(configuredFps, out int fpsNumerator, out int fpsDenominator);
        var stream = new PipeWireWindowStream(nodeId, fpsNumerator, fpsDenominator, _gstLaunch, pipeWireRemoteHandle);
        return new WindowCaptureContext(windowId, nodeId, portalSessionPath, portalSessionDestination, stream);
    }

    private static double GetConfiguredLinuxPreviewRefreshRateFps()
    {
        double configuredFps = ConfigFileAccessor.GetInstance().ReadConfig(config => config.LinuxPreviewRefreshRateFps);
        return PreviewRefreshRateSettings.Clamp(configuredFps);
    }

    private string? GetStoredWaylandScreenCastRestoreToken(string windowId)
    {
        IReadOnlyList<string> restoreKeys = BuildWaylandRestoreKeys(windowId);
        if (restoreKeys.Count == 0)
            return null;

        (string? mappedToken, string? legacyToken, bool hasPerWindowMappings) = ConfigFileAccessor.GetInstance()
            .ReadConfig(config =>
            {
                for (int index = 0; index < restoreKeys.Count; index++)
                {
                    string key = restoreKeys[index];
                    if (!config.LinuxWaylandScreenCastRestoreTokensByWindowId.TryGetValue(key, out string? existing))
                        continue;
                    if (string.IsNullOrWhiteSpace(existing))
                        continue;
                    return (existing, config.LinuxWaylandScreenCastRestoreToken, true);
                }

                return (
                    (string?)null,
                    config.LinuxWaylandScreenCastRestoreToken,
                    config.LinuxWaylandScreenCastRestoreTokensByWindowId.Count > 0);
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
                if (!config.LinuxWaylandScreenCastRestoreTokensByWindowId.TryGetValue(key, out string? existing) ||
                    !string.Equals(existing, normalizedToken, StringComparison.Ordinal))
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
                config.LinuxWaylandScreenCastRestoreTokensByWindowId[restoreKeys[index]] = normalizedToken;
        });
        configAccessor.WriteUserSettings();
    }

    private string? GetStoredWaylandScreenCastRestoreData(string windowId)
    {
        IReadOnlyList<string> restoreKeys = BuildWaylandRestoreKeys(windowId);
        if (restoreKeys.Count == 0)
            return null;

        string? configuredValue = ConfigFileAccessor.GetInstance()
            .ReadConfig(config =>
            {
                for (int index = 0; index < restoreKeys.Count; index++)
                {
                    string key = restoreKeys[index];
                    if (!config.LinuxWaylandScreenCastRestoreDataByWindowId.TryGetValue(key, out string? value))
                        continue;
                    if (string.IsNullOrWhiteSpace(value))
                        continue;
                    return value;
                }

                return null;
            });

        if (string.IsNullOrWhiteSpace(configuredValue))
            return null;

        return configuredValue.Trim();
    }

    private void SaveStoredWaylandScreenCastRestoreData(string windowId, string serializedRestoreData)
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
                if (!config.LinuxWaylandScreenCastRestoreDataByWindowId.TryGetValue(key, out string? existing) ||
                    !string.Equals(existing, normalizedValue, StringComparison.Ordinal))
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
                config.LinuxWaylandScreenCastRestoreDataByWindowId[restoreKeys[index]] = normalizedValue;
        });
        configAccessor.WriteUserSettings();
    }

    private string? GetStoredWaylandScreenCastStreamId(string windowId)
    {
        IReadOnlyList<string> restoreKeys = BuildWaylandRestoreKeys(windowId);
        if (restoreKeys.Count == 0)
            return null;

        string? configuredId = ConfigFileAccessor.GetInstance()
            .ReadConfig(config =>
            {
                for (int index = 0; index < restoreKeys.Count; index++)
                {
                    string key = restoreKeys[index];
                    if (!config.LinuxWaylandScreenCastStreamIdsByWindowId.TryGetValue(key, out string? value))
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
                if (!config.LinuxWaylandScreenCastStreamIdsByWindowId.TryGetValue(key, out string? existing) ||
                    !string.Equals(existing, normalizedStreamId, StringComparison.Ordinal))
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
                config.LinuxWaylandScreenCastStreamIdsByWindowId[restoreKeys[index]] = normalizedStreamId;
        });
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

            if (!string.IsNullOrWhiteSpace(normalizedProcess) && !string.IsNullOrWhiteSpace(normalizedTitle))
            {
                if (identity.ProcessTitleOrdinal > 0)
                {
                    AddRestoreKey(
                        $"process_title:{normalizedProcess}|{normalizedTitle}#{identity.ProcessTitleOrdinal.ToString(CultureInfo.InvariantCulture)}");
                }

                if (identity.ProcessTitleCount <= 1)
                {
                    AddRestoreKey($"process_title:{normalizedProcess}|{normalizedTitle}");
                }
            }

            if (!string.IsNullOrWhiteSpace(normalizedTitle))
            {
                if (identity.TitleOrdinal > 0)
                    AddRestoreKey($"title:{normalizedTitle}#{identity.TitleOrdinal.ToString(CultureInfo.InvariantCulture)}");

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

        WindowConfig? currentWindow = windows.FirstOrDefault(window => AreWindowIdsEquivalent(window.WindowId, windowId));
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
                .Where(window => string.Equals(NormalizeIdentityPart(window.WindowTitle), normalizedTitle, StringComparison.Ordinal))
                .OrderBy(window => NormalizeWindowId(window.WindowId))
                .ToList();
            titleCount = sameTitleWindows.Count;

            titleOrdinal = sameTitleWindows
                .Select((window, index) => new { window, ordinal = index + 1 })
                .FirstOrDefault(item => AreWindowIdsEquivalent(item.window.WindowId, windowId))
                ?.ordinal ?? 0;
        }

        int processTitleOrdinal = 0;
        int processTitleCount = 0;
        if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(processName))
        {
            string normalizedTitle = NormalizeIdentityPart(title);
            string normalizedProcess = NormalizeIdentityPart(processName);
            List<WindowConfig> sameProcessTitleWindows = windows
                .Where(window =>
                    string.Equals(NormalizeIdentityPart(window.WindowTitle), normalizedTitle, StringComparison.Ordinal) &&
                    string.Equals(NormalizeIdentityPart(window.ProcessName), normalizedProcess, StringComparison.Ordinal))
                .OrderBy(window => NormalizeWindowId(window.WindowId))
                .ToList();
            processTitleCount = sameProcessTitleWindows.Count;

            processTitleOrdinal = sameProcessTitleWindows
                .Select((window, index) => new { window, ordinal = index + 1 })
                .FirstOrDefault(item => AreWindowIdsEquivalent(item.window.WindowId, windowId))
                ?.ordinal ?? 0;
        }

        identity = new WindowIdentity(
            title,
            processName,
            titleOrdinal,
            processTitleOrdinal,
            titleCount,
            processTitleCount);
        return true;
    }

    private static string NormalizeIdentityPart(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant();
    }

    private static string NormalizeWindowId(string windowId)
    {
        return string.IsNullOrWhiteSpace(windowId)
            ? string.Empty
            : windowId.Trim().ToLowerInvariant();
    }

    private async Task<Bitmap?> TryRequestCaptureFrameAsync(
        WindowCaptureContext capture,
        ScreenshotRequest request,
        CancellationToken cancellationToken)
    {
        if (capture.IsDisposed || capture.ForceFallback)
            return null;

        capture.Stream.EnsureRunning();

        int timeoutMs = Math.Clamp(request.TimeoutMs, 100, 10_000);
        Bitmap? frame = await capture.Stream.GetFrameAsync(timeoutMs, cancellationToken).ConfigureAwait(false);
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
            if (capture.ConsecutiveNoFrameTimeouts < 2)
                return null;
        }
        else
        {
            capture.ConsecutiveNoFrameTimeouts = 0;
        }

        try
        {
            await Task.Delay(Math.Min(PipeWireReconnectDelayMs, timeoutMs), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        capture.Stream.Restart();
        frame = await capture.Stream.GetFrameAsync(timeoutMs, cancellationToken).ConfigureAwait(false);
        if (frame is not null)
        {
            capture.ConsecutiveFailures = 0;
            capture.ConsecutiveNoFrameTimeouts = 0;
            ClearExcludedNodes(capture.WindowId);
            return frame;
        }

        if (_isWaylandSession && !capture.Stream.HasReceivedFrame())
        {
            capture.ConsecutiveFailures++;
            if (capture.ConsecutiveFailures >= 2)
            {
                PipeWireTrace.Write($"Wayland no first frame after restart; recreating capture windowId={capture.WindowId} nodeId={capture.NodeId}");
                MarkNodeAsExcluded(capture.WindowId, capture.NodeId);
                ResetCapture(capture);
            }

            return null;
        }

        capture.ConsecutiveFailures++;
        if (capture.ConsecutiveFailures < 3)
            return null;

        if (_isWaylandSession)
        {
            MarkNodeAsExcluded(capture.WindowId, capture.NodeId);
            ResetCapture(capture);
            return null;
        }

        capture.ForceFallback = true;
        capture.Stream.Dispose();
        if (!string.IsNullOrWhiteSpace(capture.PortalSessionPath))
            ClosePortalSession(capture.PortalSessionPath);

        return null;
    }

    private async Task<bool> TryRecoverCaptureAsync(
        WindowCaptureContext capture,
        int timeoutMs,
        CancellationToken cancellationToken)
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
            await Task.Delay(Math.Min(PipeWireReconnectDelayMs, timeoutMs), cancellationToken).ConfigureAwait(false);
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
                if (capture.ConsecutiveFailures >= 2)
                {
                    PipeWireTrace.Write($"Wayland restart loop without frame; recreating capture windowId={capture.WindowId} nodeId={capture.NodeId}");
                    MarkNodeAsExcluded(capture.WindowId, capture.NodeId);
                    ResetCapture(capture);
                    return false;
                }

                return true;
            }

            capture.ConsecutiveFailures = 0;
            capture.ConsecutiveNoFrameTimeouts = 0;
            return true;
        }

        capture.ConsecutiveFailures++;
        if (capture.ConsecutiveFailures < 3)
            return true;

        if (_isWaylandSession)
        {
            MarkNodeAsExcluded(capture.WindowId, capture.NodeId);
            ResetCapture(capture);
            return false;
        }

        capture.ForceFallback = true;
        capture.Stream.Dispose();
        if (!string.IsNullOrWhiteSpace(capture.PortalSessionPath))
            ClosePortalSession(capture.PortalSessionPath);

        return false;
    }

    private void ResetCapture(WindowCaptureContext capture)
    {
        ArgumentNullException.ThrowIfNull(capture);

        lock (_capturesSync)
        {
            if (_captures.TryGetValue(capture.WindowId, out WindowCaptureContext? existing) && ReferenceEquals(existing, capture))
                _captures.Remove(capture.WindowId);
            _failedWindows.Remove(capture.WindowId);
        }

        capture.Dispose(ClosePortalSession);
    }

    private async Task<PortalCaptureBootstrap?> TryStartWaylandPortalWindowScreencastAsync(
        string windowId,
        IReadOnlyCollection<string> excludedNodeIds,
        CancellationToken cancellationToken)
    {
        await _waylandPortalSessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        Connection? connection = await EnsureSessionBusConnectionAsync(cancellationToken).ConfigureAwait(false);
        if (connection is null)
        {
            _waylandPortalSessionGate.Release();
            return null;
        }

        string? sessionPath = null;
        CloseSafeHandle? pipeWireRemoteHandle = null;
        PipeWireTrace.Write("WaylandPortal begin");
        try
        {
            IPipeWirePortalScreenCast screenCast = connection.CreateProxy<IPipeWirePortalScreenCast>(
                PortalDesktopDestination,
                new ObjectPath("/org/freedesktop/portal/desktop"));
            PipeWireTrace.Write("WaylandPortal proxy created");

            string token = Guid.NewGuid().ToString("N");
            string sessionToken = $"ws_session_{token}";
            string? restoreToken = GetStoredWaylandScreenCastRestoreToken(windowId);

            ObjectPath createRequestPath = await screenCast.CreateSessionAsync(new Dictionary<string, object>
            {
                ["handle_token"] = $"ws_create_{token}",
                ["session_handle_token"] = sessionToken
            }).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
            PipeWireTrace.Write($"WaylandPortal create requestPath={createRequestPath}");

            PortalRequestResponse? createResponse = await WaitForPortalRequestResponseAsync(
                    connection,
                    createRequestPath,
                    TimeSpan.FromMinutes(2),
                    cancellationToken)
                .ConfigureAwait(false);
            PipeWireTrace.Write($"WaylandPortal create response={(createResponse?.ResponseCode.ToString(CultureInfo.InvariantCulture) ?? "null")}");
            if (createResponse is null || createResponse.Value.ResponseCode != 0)
                return null;

            sessionPath = ExtractSessionPath(createResponse.Value.Results) ??
                          BuildSessionPathFromRequest(createRequestPath, sessionToken);
            if (string.IsNullOrWhiteSpace(sessionPath))
                return null;

            var selectOptions = new Dictionary<string, object>
            {
                ["handle_token"] = $"ws_select_{token}",
                ["types"] = (uint)2,
                ["multiple"] = false,
                ["cursor_mode"] = (uint)2,
                ["persist_mode"] = (uint)2
            };
            if (!string.IsNullOrWhiteSpace(restoreToken))
            {
                selectOptions["restore_token"] = restoreToken;
                PipeWireTrace.Write($"WaylandPortal using stored restore token windowId={windowId}");
            }

            var sessionObjectPath = new ObjectPath(sessionPath);
            ObjectPath selectRequestPath = await screenCast.SelectSourcesAsync(sessionObjectPath, selectOptions)
                .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken)
                .ConfigureAwait(false);
            PipeWireTrace.Write($"WaylandPortal select requestPath={selectRequestPath} session={sessionPath}");

            PortalRequestResponse? selectResponse = await WaitForPortalRequestResponseAsync(
                    connection,
                    selectRequestPath,
                    TimeSpan.FromMinutes(5),
                    cancellationToken)
                .ConfigureAwait(false);
            PipeWireTrace.Write($"WaylandPortal select response={(selectResponse?.ResponseCode.ToString(CultureInfo.InvariantCulture) ?? "null")}");
            if (selectResponse is null || selectResponse.Value.ResponseCode != 0)
            {
                await ClosePortalSessionAsync(sessionPath, CancellationToken.None).ConfigureAwait(false);
                return null;
            }

            ObjectPath startRequestPath = await screenCast.StartAsync(sessionObjectPath, string.Empty, new Dictionary<string, object>
            {
                ["handle_token"] = $"ws_start_{token}"
            }).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
            PipeWireTrace.Write($"WaylandPortal start requestPath={startRequestPath} session={sessionPath}");

            PortalRequestResponse? startResponse = await WaitForPortalRequestResponseAsync(
                    connection,
                    startRequestPath,
                    TimeSpan.FromMinutes(5),
                    cancellationToken)
                .ConfigureAwait(false);
            PipeWireTrace.Write($"WaylandPortal start response={(startResponse?.ResponseCode.ToString(CultureInfo.InvariantCulture) ?? "null")}");
            if (startResponse is null || startResponse.Value.ResponseCode != 0)
            {
                await ClosePortalSessionAsync(sessionPath, CancellationToken.None).ConfigureAwait(false);
                return null;
            }

            string? newRestoreToken = ExtractPortalRestoreToken(startResponse.Value.Results);
            if (string.IsNullOrWhiteSpace(newRestoreToken))
                PipeWireTrace.Write($"WaylandPortal no restore token in start response windowId={windowId}");

            string? nodeId = SelectPortalStreamNodeId(
                windowId,
                startResponse.Value.Results,
                excludedNodeIds,
                out string? selectedStreamStableId,
                out bool shouldPersistSelectedStreamStableId);
            IReadOnlyList<PortalStreamDescriptor> extractedStreams = ExtractPortalStreams(startResponse.Value.Results);
            string extractedSummary = string.Join(
                ',',
                extractedStreams.Select(stream =>
                    string.IsNullOrWhiteSpace(stream.StableId)
                        ? stream.NodeId
                        : $"{stream.NodeId}({stream.StableId})"));
            PipeWireTrace.Write(
                $"WaylandPortal streams extracted={extractedSummary} selected={nodeId ?? "null"} streamId={selectedStreamStableId ?? "null"}");
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                await ClosePortalSessionAsync(sessionPath, CancellationToken.None).ConfigureAwait(false);
                return null;
            }

            if (!string.IsNullOrWhiteSpace(newRestoreToken))
            {
                SaveStoredWaylandScreenCastRestoreToken(windowId, newRestoreToken);
                PipeWireTrace.Write($"WaylandPortal stored updated restore token windowId={windowId}");
            }

            if (shouldPersistSelectedStreamStableId && !string.IsNullOrWhiteSpace(selectedStreamStableId))
                SaveStoredWaylandScreenCastStreamId(windowId, selectedStreamStableId);

            pipeWireRemoteHandle = await screenCast.OpenPipeWireRemoteAsync(
                    sessionObjectPath,
                    new Dictionary<string, object>())
                .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken)
                .ConfigureAwait(false);
            if (pipeWireRemoteHandle.IsInvalid || pipeWireRemoteHandle.IsClosed)
            {
                pipeWireRemoteHandle.Dispose();
                await ClosePortalSessionAsync(sessionPath, CancellationToken.None).ConfigureAwait(false);
                return null;
            }

            SetPendingNodeId(windowId, nodeId);
            PipeWireTrace.Write($"WaylandPortal remote fd={pipeWireRemoteHandle.DangerousGetHandle().ToInt64()}");
            return new PortalCaptureBootstrap(nodeId, sessionPath, PortalDesktopDestination, pipeWireRemoteHandle);
        }
        catch (TimeoutException)
        {
            ClearPendingNodeId(windowId);
            PipeWireTrace.Write("WaylandPortal exception timeout");
            pipeWireRemoteHandle?.Dispose();
            if (!string.IsNullOrWhiteSpace(sessionPath))
                await ClosePortalSessionAsync(sessionPath, CancellationToken.None).ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException)
        {
            ClearPendingNodeId(windowId);
            PipeWireTrace.Write("WaylandPortal exception cancelled");
            pipeWireRemoteHandle?.Dispose();
            if (!string.IsNullOrWhiteSpace(sessionPath))
                await ClosePortalSessionAsync(sessionPath, CancellationToken.None).ConfigureAwait(false);
            return null;
        }
        catch (Exception exception)
        {
            ClearPendingNodeId(windowId);
            PipeWireTrace.Write($"WaylandPortal exception {exception.GetType().FullName}: {exception.Message}");
            pipeWireRemoteHandle?.Dispose();
            if (!string.IsNullOrWhiteSpace(sessionPath))
                await ClosePortalSessionAsync(sessionPath, CancellationToken.None).ConfigureAwait(false);
            return null;
        }
        finally
        {
            _waylandPortalSessionGate.Release();
        }
    }

    private async Task<Connection?> EnsureSessionBusConnectionAsync(CancellationToken cancellationToken)
    {
        Connection connection;
        lock (_dbusSync)
        {
            _sessionBusConnection ??= Connection.Session;
            connection = _sessionBusConnection;
        }

        try
        {
            await connection.ConnectAsync()
                .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken)
                .ConfigureAwait(false);
            PipeWireTrace.Write("EnsureSessionBusConnectionAsync connected");
            return connection;
        }
        catch (Exception exception)
        {
            PipeWireTrace.Write($"EnsureSessionBusConnectionAsync failed {exception.GetType().FullName}: {exception.Message}");
            return null;
        }
    }

    private static async Task<PortalRequestResponse?> WaitForPortalRequestResponseAsync(
        Connection connection,
        ObjectPath requestPath,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        IPipeWirePortalRequest request = connection.CreateProxy<IPipeWirePortalRequest>(PortalDesktopDestination, requestPath);
        var completion = new TaskCompletionSource<PortalRequestResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        IDisposable? watcher = null;

        try
        {
            watcher = await request.WatchResponseAsync(response =>
            {
                IDictionary<string, object> safeResults = response.Results ?? new Dictionary<string, object>();
                _ = completion.TrySetResult(new PortalRequestResponse(response.Response, safeResults));
            }).ConfigureAwait(false);

            return await completion.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            PipeWireTrace.Write($"WaylandPortal request timeout path={requestPath}");
            return null;
        }
        catch (OperationCanceledException)
        {
            PipeWireTrace.Write($"WaylandPortal request cancelled path={requestPath}");
            return null;
        }
        catch (Exception exception)
        {
            PipeWireTrace.Write($"WaylandPortal request exception path={requestPath} {exception.GetType().FullName}: {exception.Message}");
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
            restoreData = new PortalRestoreData(typedTuple.Item1.Trim(), typedTuple.Item2, typedTuple.Item3.ToArray());
            return true;
        }

        if (value is ValueTuple<string, int, byte[]> signedTuple &&
            signedTuple.Item2 >= 0 &&
            !string.IsNullOrWhiteSpace(signedTuple.Item1) &&
            signedTuple.Item3.Length > 0)
        {
            restoreData = new PortalRestoreData(signedTuple.Item1.Trim(), (uint)signedTuple.Item2, signedTuple.Item3.ToArray());
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

    private static bool TryDeserializePortalRestoreData(string serializedRestoreData, out PortalRestoreData restoreData)
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
        if (!uint.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint version))
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

    private static bool ArePortalRestoreDataEquivalent(PortalRestoreData left, PortalRestoreData right)
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
            }));
    }

    private string? SelectPortalStreamNodeId(
        string windowId,
        IDictionary<string, object> results,
        IReadOnlyCollection<string> excludedNodeIds,
        out string? selectedStreamStableId,
        out bool shouldPersistSelectedStreamStableId)
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
        IReadOnlyCollection<string> windowPatterns = BuildWindowMatchPatterns(windowId, windowTitle);
        IReadOnlyCollection<string> windowTitlePatterns = BuildWindowTitlePatterns(windowTitle);
        PortalRestoreData? restoreData = ExtractPortalRestoreData(results);
        bool restoreDataMatchesRequestedWindowTitle = restoreData is PortalRestoreData typedRestoreData &&
                                                      MatchesRestoreDataWindowTitle(typedRestoreData, windowTitlePatterns);
        IReadOnlyList<NodeCandidate>? discoveredNodes = null;

        NodeCandidate? FindNodeCandidate(string nodeId)
        {
            discoveredNodes ??= GetPipeWireNodeCandidates(forceRefresh: true);
            for (int index = 0; index < discoveredNodes.Count; index++)
            {
                NodeCandidate candidate = discoveredNodes[index];
                if (string.Equals(candidate.Id, nodeId, StringComparison.Ordinal))
                    return candidate;
            }

            return null;
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

        bool MatchesRequestedWindow(PortalStreamDescriptor stream)
        {
            return EvaluateWindowTitleMatchState(stream) is not MatchState.Mismatch;
        }

        string? expectedStreamStableId = GetStoredWaylandScreenCastStreamId(windowId);
        if (!string.IsNullOrWhiteSpace(expectedStreamStableId))
        {
            for (int index = 0; index < streams.Count; index++)
            {
                PortalStreamDescriptor stream = streams[index];
                if (!string.Equals(stream.StableId, expectedStreamStableId, StringComparison.Ordinal))
                    continue;
                if (allExcludedNodeIds.Contains(stream.NodeId))
                    continue;
                if (!MatchesRequestedWindow(stream))
                {
                    PipeWireTrace.Write(
                        $"WaylandPortal stored stream id candidate mismatched window title windowId={windowId} nodeId={stream.NodeId} streamId={stream.StableId ?? "null"}");
                    continue;
                }

                selectedStreamStableId = stream.StableId;
                shouldPersistSelectedStreamStableId = true;
                return stream.NodeId;
            }

            PipeWireTrace.Write(
                $"WaylandPortal stored stream id mismatch windowId={windowId} expected={expectedStreamStableId}");
        }

        if (windowPatterns.Count > 0)
        {
            PortalStreamDescriptor? matchedByPortalMetadata =
                FindBestMatchingPortalStream(streams, allExcludedNodeIds, windowPatterns);
            if (matchedByPortalMetadata is not null)
            {
                PipeWireTrace.Write(
                    $"WaylandPortal selected via stream metadata windowId={windowId} nodeId={matchedByPortalMetadata.Value.NodeId} streamId={matchedByPortalMetadata.Value.StableId ?? "null"}");
                selectedStreamStableId = matchedByPortalMetadata.Value.StableId;
                return matchedByPortalMetadata.Value.NodeId;
            }
        }

        HashSet<string> candidateIds = streams.Select(stream => stream.NodeId).ToHashSet(StringComparer.Ordinal);
        if (windowPatterns.Count > 0)
        {
            IReadOnlyList<NodeCandidate> nodes = discoveredNodes ??= GetPipeWireNodeCandidates(forceRefresh: true);
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
                PipeWireTrace.Write($"WaylandPortal selected via pw-dump match windowId={windowId} nodeId={matchedNode.Value.Id}");
                selectedStreamStableId = streams
                    .FirstOrDefault(stream => string.Equals(stream.NodeId, matchedNode.Value.Id, StringComparison.Ordinal))
                    .StableId;
                return matchedNode.Value.Id;
            }
        }

        IReadOnlyList<PortalStreamDescriptor> orderedFallbackStreams = OrderPortalStreamsDeterministically(streams);
        for (int index = 0; index < orderedFallbackStreams.Count; index++)
        {
            PortalStreamDescriptor stream = orderedFallbackStreams[index];
            if (allExcludedNodeIds.Contains(stream.NodeId))
                continue;

            MatchState titleMatchState = EvaluateWindowTitleMatchState(stream);
            if (titleMatchState is MatchState.Mismatch)
            {
                if (streams.Count == 1 && restoreDataMatchesRequestedWindowTitle)
                {
                    PipeWireTrace.Write(
                        $"WaylandPortal fallback candidate accepted via restore_data title match windowId={windowId} nodeId={stream.NodeId}");
                }
                else
                {
                    PipeWireTrace.Write(
                        $"WaylandPortal fallback candidate rejected by title windowId={windowId} nodeId={stream.NodeId}");
                    continue;
                }
            }

            if (windowTitlePatterns.Count > 0 && streams.Count == 1 && titleMatchState is not MatchState.Match &&
                restoreDataMatchesRequestedWindowTitle)
            {
                PipeWireTrace.Write(
                    $"WaylandPortal fallback candidate title verified by restore_data windowId={windowId} nodeId={stream.NodeId}");
            }

            if (windowTitlePatterns.Count > 0 && streams.Count > 1 && titleMatchState is not MatchState.Match)
            {
                PipeWireTrace.Write(
                    $"WaylandPortal fallback candidate unverified for ambiguous selection windowId={windowId} nodeId={stream.NodeId}");
                continue;
            }

            PipeWireTrace.Write($"WaylandPortal selected via deterministic fallback windowId={windowId} nodeId={stream.NodeId}");
            selectedStreamStableId = stream.StableId;
            shouldPersistSelectedStreamStableId = string.IsNullOrWhiteSpace(expectedStreamStableId);
            return stream.NodeId;
        }

        if (windowTitlePatterns.Count > 0 && streams.Count > 1)
        {
            PipeWireTrace.Write(
                $"WaylandPortal no verified stream candidate for ambiguous selection windowId={windowId}");
        }

        PipeWireTrace.Write(
            $"WaylandPortal all stream candidates excluded windowId={windowId} candidates={string.Join(',', streams.Select(stream => stream.NodeId))}");
        return null;
    }

    private static IReadOnlyList<PortalStreamDescriptor> ExtractPortalStreams(IDictionary<string, object> results)
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

    private static bool TryExtractPortalStreamDescriptor(object value, out PortalStreamDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(value);

        descriptor = default;

        if (value is ValueTuple<uint, IDictionary<string, object>> typedTuple)
        {
            descriptor = new PortalStreamDescriptor(
                typedTuple.Item1.ToString(CultureInfo.InvariantCulture),
                TryExtractPortalStreamStableId(typedTuple.Item2),
                BuildPortalStreamSearchText(typedTuple.Item2));
            return true;
        }

        if (value is ITuple tuple &&
            tuple.Length > 0 &&
            TryConvertToUInt32(tuple[0], out uint tupleId))
        {
            string? stableId = tuple.Length > 1 && tuple[1] is IDictionary<string, object> tupleProperties
                ? TryExtractPortalStreamStableId(tupleProperties)
                : null;
            string normalizedSearchText = tuple.Length > 1 && tuple[1] is IDictionary<string, object> tupleSearchProperties
                ? BuildPortalStreamSearchText(tupleSearchProperties)
                : string.Empty;
            descriptor = new PortalStreamDescriptor(
                tupleId.ToString(CultureInfo.InvariantCulture),
                stableId,
                normalizedSearchText);
            return true;
        }

        if (value is IDictionary<string, object> dictionary)
        {
            if (TryExtractStreamNodeIdFromDictionary(dictionary, out string nodeId))
            {
                descriptor = new PortalStreamDescriptor(
                    nodeId,
                    TryExtractPortalStreamStableId(dictionary),
                    BuildPortalStreamSearchText(dictionary));
                return true;
            }
        }

        if (TryConvertToUInt32(value, out uint scalarId))
        {
            descriptor = new PortalStreamDescriptor(
                scalarId.ToString(CultureInfo.InvariantCulture),
                null,
                string.Empty);
            return true;
        }

        return false;
    }

    private static bool TryExtractStreamNodeIdFromDictionary(
        IDictionary<string, object> dictionary,
        out string streamNodeId)
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
                return uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
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
                    if (!TryConvertToUInt32(array.GetValue(index), out uint parsed) || parsed > byte.MaxValue)
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
            PropertyInfo? valueProperty = type.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
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
            _ => null
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
            JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.String => jsonElement.GetString(),
            _ => value.ToString()
        };
    }

    private async Task<PortalCaptureBootstrap?> TryStartKdeBackendWindowScreencastAsync(
        string windowId,
        IReadOnlyCollection<string> excludedNodeIds,
        CancellationToken cancellationToken)
    {
        if (!LinuxDependencies.IsPwDumpAvailable)
            return null;

        await _waylandPortalSessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? sessionPath = null;
        try
        {
            Connection? connection = await EnsureSessionBusConnectionAsync(cancellationToken).ConfigureAwait(false);
            if (connection is null)
                return null;

            PipeWireTrace.Write($"WaylandKdeBackend begin windowId={windowId}");

            string requestToken = Guid.NewGuid().ToString("N");
            string senderPathSegment = $"1_{Environment.ProcessId}";
            string createHandlePath = $"/org/freedesktop/portal/desktop/request/{senderPathSegment}/ws_create_{requestToken}";
            string selectHandlePath = $"/org/freedesktop/portal/desktop/request/{senderPathSegment}/ws_select_{requestToken}";
            string startHandlePath = $"/org/freedesktop/portal/desktop/request/{senderPathSegment}/ws_start_{requestToken}";
            sessionPath = $"/org/freedesktop/portal/desktop/session/{senderPathSegment}/ws_session_{requestToken}";

            IReadOnlyList<NodeCandidate> baseline = GetPipeWireNodeCandidates(forceRefresh: true);
            HashSet<string> baselineIds = baseline.Select(candidate => candidate.Id).ToHashSet(StringComparer.Ordinal);

            IKdePortalScreenCast screenCast = connection.CreateProxy<IKdePortalScreenCast>(
                KdePortalBackendDestination,
                new ObjectPath("/org/freedesktop/portal/desktop"));

            (uint createResponseCode, IDictionary<string, object> _) = await screenCast.CreateSessionAsync(
                    new ObjectPath(createHandlePath),
                    new ObjectPath(sessionPath),
                    KdePortalAppId,
                    new Dictionary<string, object>())
                .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken)
                .ConfigureAwait(false);
            PipeWireTrace.Write($"WaylandKdeBackend create response={createResponseCode} windowId={windowId}");
            if (createResponseCode != 0)
                return null;

            PortalRestoreData? storedRestoreData = null;
            string? storedRestoreDataValue = GetStoredWaylandScreenCastRestoreData(windowId);
            if (!string.IsNullOrWhiteSpace(storedRestoreDataValue))
            {
                if (TryDeserializePortalRestoreData(storedRestoreDataValue, out PortalRestoreData parsedRestoreData))
                {
                    storedRestoreData = parsedRestoreData;
                    PipeWireTrace.Write(
                        $"WaylandKdeBackend using stored restore data windowId={windowId} provider={parsedRestoreData.Provider} version={parsedRestoreData.Version}");
                }
                else
                {
                    PipeWireTrace.Write($"WaylandKdeBackend stored restore data invalid format windowId={windowId}");
                }
            }

            string? restoreToken = GetStoredWaylandScreenCastRestoreToken(windowId);
            if (storedRestoreData is null && string.IsNullOrWhiteSpace(restoreToken))
            {
                PipeWireTrace.Write(
                    $"WaylandKdeBackend no stored restore artifacts windowId={windowId}; requesting interactive source selection");
            }
            else if (storedRestoreData is null)
            {
                PipeWireTrace.Write($"WaylandKdeBackend using stored restore token windowId={windowId}");
            }

            var selectOptions = new Dictionary<string, object>
            {
                ["types"] = (uint)2,
                ["multiple"] = false,
                ["cursor_mode"] = (uint)2,
                ["persist_mode"] = (uint)2
            };
            if (storedRestoreData is PortalRestoreData restoreData)
            {
                selectOptions["restore_data"] = ValueTuple.Create(
                    restoreData.Provider,
                    restoreData.Version,
                    restoreData.Bytes);
            }
            else if (!string.IsNullOrWhiteSpace(restoreToken))
            {
                selectOptions["restore_token"] = restoreToken.Trim();
            }

            (uint selectResponseCode, IDictionary<string, object> _) = await screenCast.SelectSourcesAsync(
                    new ObjectPath(selectHandlePath),
                    new ObjectPath(sessionPath),
                    KdePortalAppId,
                    selectOptions)
                .WaitAsync(TimeSpan.FromSeconds(45), cancellationToken)
                .ConfigureAwait(false);
            PipeWireTrace.Write($"WaylandKdeBackend select response={selectResponseCode} windowId={windowId}");
            if (selectResponseCode != 0)
            {
                ClosePortalSession(sessionPath, KdePortalBackendDestination);
                return null;
            }

            (uint startResponseCode, IDictionary<string, object> startResults) = await screenCast.StartAsync(
                    new ObjectPath(startHandlePath),
                    new ObjectPath(sessionPath),
                    KdePortalAppId,
                    string.Empty,
                    new Dictionary<string, object>())
                .WaitAsync(TimeSpan.FromMinutes(2), cancellationToken)
                .ConfigureAwait(false);
            PipeWireTrace.Write($"WaylandKdeBackend start response={startResponseCode} windowId={windowId}");
            if (startResponseCode != 0)
            {
                ClosePortalSession(sessionPath, KdePortalBackendDestination);
                return null;
            }

            PortalRestoreData? newRestoreData = ExtractPortalRestoreData(startResults);
            PipeWireTrace.Write(
                $"WaylandKdeBackend restore data from start={(newRestoreData is null ? "null" : "present")} windowId={windowId}");

            string? newRestoreToken = ExtractPortalRestoreToken(startResults);
            if (newRestoreData is null && string.IsNullOrWhiteSpace(newRestoreToken))
            {
                PipeWireTrace.Write(
                    $"WaylandKdeBackend start result keys windowId={windowId} keys={DescribePortalResultKeys(startResults)}");
            }

            void PersistUpdatedRestoreArtifacts()
            {
                if (newRestoreData is PortalRestoreData startRestoreData)
                {
                    if (storedRestoreData is null)
                    {
                        SaveStoredWaylandScreenCastRestoreData(windowId, SerializePortalRestoreData(startRestoreData));
                        PipeWireTrace.Write($"WaylandKdeBackend stored initial restore data windowId={windowId}");
                    }
                    else if (!ArePortalRestoreDataEquivalent(storedRestoreData.Value, startRestoreData))
                    {
                        PipeWireTrace.Write($"WaylandKdeBackend keep existing restore data windowId={windowId}");
                    }
                }

                if (!string.IsNullOrWhiteSpace(newRestoreToken))
                {
                    if (string.IsNullOrWhiteSpace(restoreToken))
                    {
                        SaveStoredWaylandScreenCastRestoreToken(windowId, newRestoreToken);
                        PipeWireTrace.Write($"WaylandKdeBackend stored initial restore token windowId={windowId}");
                    }
                    else if (!string.Equals(restoreToken, newRestoreToken, StringComparison.Ordinal))
                    {
                        PipeWireTrace.Write($"WaylandKdeBackend keep existing restore token windowId={windowId}");
                    }
                }
            }

            string? nodeId = SelectPortalStreamNodeId(
                windowId,
                startResults,
                excludedNodeIds,
                out string? selectedStreamStableId,
                out bool shouldPersistSelectedStreamStableId);
            IReadOnlyList<PortalStreamDescriptor> extractedStreams = ExtractPortalStreams(startResults);
            string extractedSummary = string.Join(
                ',',
                extractedStreams.Select(stream =>
                    string.IsNullOrWhiteSpace(stream.StableId)
                        ? stream.NodeId
                        : $"{stream.NodeId}({stream.StableId})"));
            PipeWireTrace.Write(
                $"WaylandKdeBackend streams extracted={extractedSummary} selected={nodeId ?? "null"} streamId={selectedStreamStableId ?? "null"}");

            if (shouldPersistSelectedStreamStableId && !string.IsNullOrWhiteSpace(selectedStreamStableId))
                SaveStoredWaylandScreenCastStreamId(windowId, selectedStreamStableId);

            if (!string.IsNullOrWhiteSpace(nodeId))
            {
                PersistUpdatedRestoreArtifacts();
                SetPendingNodeId(windowId, nodeId);
                return new PortalCaptureBootstrap(
                    nodeId,
                    sessionPath,
                    KdePortalBackendDestination,
                    PipeWireRemoteHandle: null);
            }

            var allExcludedNodeIds = new HashSet<string>(excludedNodeIds, StringComparer.Ordinal);
            foreach (string activeNodeId in SnapshotActiveNodeIds())
                _ = allExcludedNodeIds.Add(activeNodeId);

            string? discoveredNodeId = WaitForNewPipeWireNodeId(
                baselineIds,
                TimeSpan.FromMilliseconds(PipeWireNodeDiscoveryTimeoutMs),
                windowId,
                allExcludedNodeIds,
                allowUnmatchedFallback: false);
            if (!string.IsNullOrWhiteSpace(discoveredNodeId))
            {
                PersistUpdatedRestoreArtifacts();
                SetPendingNodeId(windowId, discoveredNodeId);
                PipeWireTrace.Write($"WaylandKdeBackend selected={discoveredNodeId} discovered=true windowId={windowId}");
                return new PortalCaptureBootstrap(
                    discoveredNodeId,
                    sessionPath,
                    KdePortalBackendDestination,
                    PipeWireRemoteHandle: null);
            }

            PipeWireTrace.Write(
                $"WaylandKdeBackend all stream candidates excluded windowId={windowId} candidates={extractedSummary}");
            ClosePortalSession(sessionPath, KdePortalBackendDestination);
            return null;
        }
        catch (TimeoutException)
        {
            PipeWireTrace.Write($"WaylandKdeBackend timeout windowId={windowId}");
            if (!string.IsNullOrWhiteSpace(sessionPath))
                ClosePortalSession(sessionPath, KdePortalBackendDestination);
            return null;
        }
        catch (OperationCanceledException)
        {
            PipeWireTrace.Write($"WaylandKdeBackend cancelled windowId={windowId}");
            if (!string.IsNullOrWhiteSpace(sessionPath))
                ClosePortalSession(sessionPath, KdePortalBackendDestination);
            return null;
        }
        catch (Exception exception)
        {
            PipeWireTrace.Write($"WaylandKdeBackend exception {exception.GetType().FullName}: {exception.Message}");
            if (!string.IsNullOrWhiteSpace(sessionPath))
                ClosePortalSession(sessionPath, KdePortalBackendDestination);
            return null;
        }
        finally
        {
            _waylandPortalSessionGate.Release();
        }
    }

    private string? TryStartPortalWindowScreencast(
        string windowId,
        IReadOnlyCollection<string> excludedNodeIds,
        out string? sessionPath)
    {
        sessionPath = null;

        if (!LinuxDependencies.IsGdbusAvailable || !LinuxDependencies.IsPwDumpAvailable)
            return null;

        IReadOnlyList<NodeCandidate> baseline = GetPipeWireNodeCandidates(forceRefresh: true);
        HashSet<string> baselineIds = baseline.Select(candidate => candidate.Id).ToHashSet(StringComparer.Ordinal);

        string sessionToken = $"ws_session_{Guid.NewGuid():N}";
        string createToken = $"ws_create_{Guid.NewGuid():N}";
        string selectToken = $"ws_select_{Guid.NewGuid():N}";
        string startToken = $"ws_start_{Guid.NewGuid():N}";

        string createOptions = $"{{'session_handle_token': <'{sessionToken}'>, 'handle_token': <'{createToken}'>}}";
        string createResult = RunPortalDesktopMethod(
            "org.freedesktop.portal.ScreenCast.CreateSession",
            [createOptions],
            timeoutMs: 5_000);

        string? createRequestPath = ExtractObjectPath(createResult);
        if (string.IsNullOrWhiteSpace(createRequestPath))
            return null;

        string? createPayload = WaitForPortalRequestResponse(
            createRequestPath,
            TimeSpan.FromMilliseconds(10_000),
            out uint createResponseCode);
        if (createResponseCode != uint.MaxValue && createResponseCode != 0)
            return null;

        sessionPath = ExtractSessionHandle(createPayload);
        if (string.IsNullOrWhiteSpace(sessionPath))
        {
            string? sender = ExtractRequestSender(createRequestPath);
            if (string.IsNullOrWhiteSpace(sender))
                return null;
            sessionPath = $"/org/freedesktop/portal/desktop/session/{sender}/{sessionToken}";
        }

        string selectOptions = $"{{'types': <uint32 2>, 'multiple': <false>, 'handle_token': <'{selectToken}'>}}";
        string selectResult = RunPortalDesktopMethod(
            "org.freedesktop.portal.ScreenCast.SelectSources",
            [sessionPath, selectOptions],
            timeoutMs: 5_000);
        string? selectRequestPath = ExtractObjectPath(selectResult);
        if (string.IsNullOrWhiteSpace(selectRequestPath))
        {
            ClosePortalSession(sessionPath);
            sessionPath = null;
            return null;
        }

        _ = WaitForPortalRequestResponse(
            selectRequestPath,
            TimeSpan.FromMilliseconds(10_000),
            out uint selectResponseCode);
        if (selectResponseCode != uint.MaxValue && selectResponseCode != 0)
        {
            ClosePortalSession(sessionPath);
            sessionPath = null;
            return null;
        }

        string startOptions = $"{{'handle_token': <'{startToken}'>}}";
        string startResult = RunPortalDesktopMethod(
            "org.freedesktop.portal.ScreenCast.Start",
            [sessionPath, string.Empty, startOptions],
            timeoutMs: 5_000);
        string? startRequestPath = ExtractObjectPath(startResult);
        if (string.IsNullOrWhiteSpace(startRequestPath))
        {
            ClosePortalSession(sessionPath);
            sessionPath = null;
            return null;
        }

        string? startPayload = WaitForPortalRequestResponse(
            startRequestPath,
            TimeSpan.FromMilliseconds(PipeWireNodeDiscoveryTimeoutMs),
            out uint startResponseCode);
        if (startResponseCode != uint.MaxValue && startResponseCode != 0)
        {
            ClosePortalSession(sessionPath);
            sessionPath = null;
            return null;
        }

        IReadOnlyList<string> portalStreamNodeIds = ExtractStreamNodeIds(startPayload);
        for (int index = 0; index < portalStreamNodeIds.Count; index++)
        {
            string streamNodeId = portalStreamNodeIds[index];
            if (!excludedNodeIds.Contains(streamNodeId))
                return streamNodeId;
        }

        string? nodeId = WaitForNewPipeWireNodeId(
            baselineIds,
            TimeSpan.FromMilliseconds(PipeWireNodeDiscoveryTimeoutMs),
            windowId,
            excludedNodeIds);
        if (!string.IsNullOrWhiteSpace(nodeId))
            return nodeId;

        ClosePortalSession(sessionPath);
        sessionPath = null;
        return null;
    }

    private string? ResolveNodeIdFromPwDump(
        string windowId,
        IReadOnlyCollection<string> excludedNodeIds,
        bool allowBestCandidate)
    {
        if (!LinuxDependencies.IsPwDumpAvailable)
            return null;

        IReadOnlyList<NodeCandidate> nodes = GetPipeWireNodeCandidates();
        IReadOnlyList<NodeCandidate> availableNodes = FilterExcludedNodes(nodes, excludedNodeIds);
        if (availableNodes.Count == 0)
            return null;

        string? windowTitle = TryGetWindowTitleById(windowId);
        IReadOnlyCollection<string> normalizedWindowIds = BuildWindowMatchPatterns(windowId, windowTitle);
        NodeCandidate? matching = FindBestMatchingNode(availableNodes, normalizedWindowIds);
        if (matching is null)
            return allowBestCandidate
                ? FindBestNode(availableNodes)?.Id
                : null;

        return matching.Value.Id;
    }

    private string? WaitForNewPipeWireNodeId(
        HashSet<string> baselineIds,
        TimeSpan timeout,
        string windowId,
        IReadOnlyCollection<string> excludedNodeIds,
        bool allowUnmatchedFallback = true)
    {
        string? windowTitle = TryGetWindowTitleById(windowId);
        IReadOnlyCollection<string> normalizedWindowIds = BuildWindowMatchPatterns(windowId, windowTitle);
        DateTime deadline = DateTime.UtcNow + timeout;
        NodeCandidate? bestNewCandidate = null;

        while (DateTime.UtcNow < deadline)
        {
            IReadOnlyList<NodeCandidate> current = GetPipeWireNodeCandidates(forceRefresh: true);
            NodeCandidate? matchingNew = FindBestNewMatchingNode(
                current,
                baselineIds,
                normalizedWindowIds,
                excludedNodeIds);
            if (matchingNew is not null)
                return matchingNew.Value.Id;

            NodeCandidate? bestThisRound = FindBestNewNode(current, baselineIds, excludedNodeIds);
            if (bestThisRound is not null && (bestNewCandidate is null || bestThisRound.Value.Score > bestNewCandidate.Value.Score))
                bestNewCandidate = bestThisRound;

            Thread.Sleep(PipeWireNodePollIntervalMs);
        }

        if (bestNewCandidate is not null && allowUnmatchedFallback)
            return bestNewCandidate.Value.Id;

        if (bestNewCandidate is not null && !allowUnmatchedFallback)
        {
            PipeWireTrace.Write(
                $"WaylandPortal new node discovered but rejected (no title match) windowId={windowId} nodeId={bestNewCandidate.Value.Id}");
        }

        return null;
    }

    private static string? WaitForPortalRequestResponse(string requestPath, TimeSpan timeout, out uint responseCode)
    {
        responseCode = uint.MaxValue;
        if (string.IsNullOrWhiteSpace(requestPath) || !LinuxDependencies.IsGdbusAvailable)
            return null;

        using Process? monitor = StartPortalRequestMonitor(requestPath);
        if (monitor is null)
            return null;

        var buffer = new StringBuilder(capacity: 256);
        bool capturing = false;
        DateTime deadline = DateTime.UtcNow + timeout;

        try
        {
            while (DateTime.UtcNow < deadline)
            {
                TimeSpan remaining = deadline - DateTime.UtcNow;
                string? line = ReadLineWithTimeout(monitor, remaining);
                if (line is null)
                    break;

                if (!capturing)
                {
                    if (!line.Contains("org.freedesktop.portal.Request.Response", StringComparison.Ordinal))
                        continue;

                    capturing = true;
                    buffer.Clear();
                    buffer.Append(line);
                }
                else
                {
                    buffer.Append('\n');
                    buffer.Append(line);
                }

                if (TryParsePortalResponse(buffer.ToString(), out responseCode, out string? payload))
                    return payload;

                if (line.TrimEnd().EndsWith(")", StringComparison.Ordinal))
                    capturing = false;
            }
        }
        finally
        {
            try
            {
                if (!monitor.HasExited)
                    monitor.Kill(entireProcessTree: true);
            }
            catch
            {
            }
        }

        return null;
    }

    private static Process? StartPortalRequestMonitor(string requestPath)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "gdbus",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.StartInfo.ArgumentList.Add("monitor");
        process.StartInfo.ArgumentList.Add("--session");
        process.StartInfo.ArgumentList.Add("--dest");
        process.StartInfo.ArgumentList.Add("org.freedesktop.portal.Desktop");
        process.StartInfo.ArgumentList.Add("--object-path");
        process.StartInfo.ArgumentList.Add(requestPath);

        try
        {
            process.Start();
            _ = Task.Run(async () =>
            {
                try
                {
                    _ = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
                }
                catch
                {
                }
            });
            return process;
        }
        catch
        {
            process.Dispose();
            return null;
        }
    }

    private static string? ReadLineWithTimeout(Process process, TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
            return null;

        try
        {
            Task<string?> readTask = Task.Run(() => process.StandardOutput.ReadLine());
            if (!readTask.Wait(timeout))
                return null;

            return readTask.Result;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryParsePortalResponse(string text, out uint responseCode, out string? payload)
    {
        responseCode = uint.MaxValue;
        payload = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        Match match = PortalResponseRegex.Match(text);
        if (!match.Success)
            return false;

        if (!uint.TryParse(match.Groups["code"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out responseCode))
            return false;

        payload = match.Groups["payload"].Value;
        return true;
    }

    private static bool IsPortalCallSuccessful(string output)
    {
        return TryExtractPortalCallResponseCode(output, out uint responseCode) && responseCode == 0;
    }

    private static bool TryExtractPortalCallResponseCode(string output, out uint responseCode)
    {
        responseCode = uint.MaxValue;
        if (string.IsNullOrWhiteSpace(output))
            return false;

        Match match = PortalCallReplyCodeRegex.Match(output);
        if (!match.Success)
            return false;

        return uint.TryParse(match.Groups["code"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out responseCode);
    }

    private static string? ExtractSessionHandle(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        Match match = PortalSessionHandleRegex.Match(payload);
        if (!match.Success)
            return null;

        return match.Groups["path"].Value;
    }

    private static IReadOnlyList<string> ExtractStreamNodeIds(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return Array.Empty<string>();

        Match streamsMatch = PortalStreamsRegex.Match(payload);
        string source = streamsMatch.Success
            ? streamsMatch.Groups["streams"].Value
            : payload;

        var ids = new List<string>(capacity: 4);
        MatchCollection matches = PortalStreamNodeIdRegex.Matches(source);
        for (int index = 0; index < matches.Count; index++)
        {
            string candidate = matches[index].Groups["id"].Value;
            if (string.IsNullOrWhiteSpace(candidate))
                continue;
            if (!ids.Contains(candidate, StringComparer.Ordinal))
                ids.Add(candidate);
        }

        return ids;
    }

    private static bool MatchesWindowId(NodeCandidate candidate, IReadOnlyCollection<string> normalizedWindowIds)
    {
        if (normalizedWindowIds.Count == 0)
            return false;

        foreach (string normalizedWindowId in normalizedWindowIds)
        {
            if (candidate.NormalizedSearchText.Contains(normalizedWindowId, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static int ComputePatternScore(string? normalizedSearchText, IReadOnlyCollection<string> patterns)
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
        IReadOnlyCollection<string> requestedWindowTitlePatterns)
    {
        if (requestedWindowTitlePatterns.Count == 0 || restoreData.Bytes.Length == 0)
            return false;

        string normalizedSearchText = BuildRestoreDataSearchText(restoreData);
        return ComputePatternScore(normalizedSearchText, requestedWindowTitlePatterns) > 0;
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
        return NormalizeForSearch(builder.ToString());
    }

    private static void AppendDecodedRestoreData(StringBuilder builder, byte[] bytes, Encoding encoding)
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

    private static PortalStreamDescriptor? FindBestMatchingPortalStream(
        IReadOnlyList<PortalStreamDescriptor> streams,
        IReadOnlyCollection<string> excludedNodeIds,
        IReadOnlyCollection<string> normalizedWindowIds)
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

            if (!hasBest || score > bestScore || (score == bestScore && CompareNodeId(stream.NodeId, best.NodeId) < 0))
            {
                hasBest = true;
                best = stream;
                bestScore = score;
            }
        }

        return hasBest ? best : null;
    }

    private static IReadOnlyList<PortalStreamDescriptor> OrderPortalStreamsDeterministically(
        IReadOnlyList<PortalStreamDescriptor> streams)
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
            bool isAsciiAlphaNumeric = (normalized >= 'a' && normalized <= 'z') || (normalized >= '0' && normalized <= '9');
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

    private static IReadOnlyCollection<string> BuildWindowMatchPatterns(string? windowId, string? windowTitle)
    {
        HashSet<string> patterns = BuildWindowIdPatterns(windowId).ToHashSet(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(windowTitle))
            return patterns;

        AddNormalizedPattern(patterns, windowTitle);
        foreach (string token in ExtractWindowTitleTokens(windowTitle))
            AddNormalizedPattern(patterns, token);

        return patterns;
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
                out value);
        }

        return ulong.TryParse(
            normalized,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value);
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

        return tokens
            .OrderByDescending(token => token.Length)
            .Take(4)
            .ToArray();
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
            if (ulong.TryParse(trimmed[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong parsedHex))
            {
                AddNormalizedPattern(patterns, $"0x{parsedHex:x}");
                AddNormalizedPattern(patterns, parsedHex.ToString(CultureInfo.InvariantCulture));
            }
        }
        else if (ulong.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong parsedDec))
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

    private static string ComputeFrameFingerprint(byte[] bytes)
    {
        if (bytes.Length == 0)
            return "empty";

        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }

    private async IAsyncEnumerable<Bitmap> StreamFallbackAsync(
        string windowId,
        ScreenshotRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (_isWaylandSession)
            yield break;

        while (!cancellationToken.IsCancellationRequested)
        {
            Bitmap? fallbackFrame = await RequestFallbackAsync(windowId, request, cancellationToken).ConfigureAwait(false);
            if (fallbackFrame is not null)
                yield return fallbackFrame;

            try
            {
                int delayMs = PreviewRefreshRateSettings.GetDelayMs(GetConfiguredLinuxPreviewRefreshRateFps());
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
        }
    }

    private async Task<Bitmap?> RequestFallbackAsync(string windowId, ScreenshotRequest request, CancellationToken cancellationToken)
    {
        if (_isWaylandSession)
            return null;

        var safeRequest = new ScreenshotRequest(
            MaxWidthPx: request.MaxWidthPx,
            MaxHeightPx: request.MaxHeightPx,
            TimeoutMs: Math.Clamp(Math.Max(request.TimeoutMs, 1_500), 100, 10_000));

        return await _fallbackProvider.RequestAsync(windowId, safeRequest, cancellationToken).ConfigureAwait(false);
    }

    private static NodeCandidate? FindBestMatchingNode(
        IReadOnlyList<NodeCandidate> candidates,
        IReadOnlyCollection<string> normalizedWindowIds)
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
        IReadOnlyCollection<string> excludedNodeIds)
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
        IReadOnlyCollection<string> excludedNodeIds)
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
        if (!forceRefresh)
        {
            lock (_nodeCacheSync)
            {
                if (_cachedNodeCandidates.Count > 0 &&
                    DateTime.UtcNow - _nodeCandidatesCachedAtUtc < TimeSpan.FromMilliseconds(PipeWireNodeCacheTtlMs))
                {
                    return _cachedNodeCandidates;
                }
            }
        }

        IReadOnlyList<NodeCandidate> freshCandidates = LoadPipeWireNodeCandidates();
        lock (_nodeCacheSync)
        {
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
                if (!string.Equals(typeElement.GetString(), "PipeWire:Interface:Node", StringComparison.Ordinal))
                    continue;
                if (!element.TryGetProperty("id", out JsonElement idElement))
                    continue;

                string nodeId = idElement.ToString();
                if (string.IsNullOrWhiteSpace(nodeId))
                    continue;

                int score = 0;
                string searchText = nodeId;
                if (element.TryGetProperty("info", out JsonElement infoElement) &&
                    infoElement.TryGetProperty("props", out JsonElement propsElement))
                {
                    string mediaClass = GetPropertyValue(propsElement, "media.class");
                    string nodeName = GetPropertyValue(propsElement, "node.name");
                    string nodeDescription = GetPropertyValue(propsElement, "node.description");

                    if (mediaClass.StartsWith("Video/Source", StringComparison.OrdinalIgnoreCase))
                        score += 6;
                    if (mediaClass.StartsWith("Stream/Output/Video", StringComparison.OrdinalIgnoreCase))
                        score += 5;
                    if (mediaClass.Contains("video", StringComparison.OrdinalIgnoreCase))
                        score += 2;
                    if (nodeName.Contains("portal", StringComparison.OrdinalIgnoreCase) ||
                        nodeName.Contains("screencast", StringComparison.OrdinalIgnoreCase) ||
                        nodeName.Contains("screen", StringComparison.OrdinalIgnoreCase) ||
                        nodeName.Contains("monitor", StringComparison.OrdinalIgnoreCase))
                        score += 4;
                    if (nodeName.Contains("kwin", StringComparison.OrdinalIgnoreCase))
                        score += 2;
                    if (nodeDescription.Contains("screencast", StringComparison.OrdinalIgnoreCase) ||
                        nodeDescription.Contains("window", StringComparison.OrdinalIgnoreCase) ||
                        nodeDescription.Contains("screen", StringComparison.OrdinalIgnoreCase) ||
                        nodeDescription.Contains("monitor", StringComparison.OrdinalIgnoreCase))
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
        IReadOnlyCollection<string> excludedNodeIds)
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

            return _captures.Values
                .Select(capture => capture.NodeId)
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
            if (!_excludedNodesByWindow.TryGetValue(windowId, out HashSet<string>? excludedNodeIds) || excludedNodeIds.Count == 0)
                return Array.Empty<string>();

            return excludedNodeIds.ToArray();
        }
    }

    private void MarkNodeAsExcluded(string windowId, string nodeId)
    {
        if (string.IsNullOrWhiteSpace(windowId) || string.IsNullOrWhiteSpace(nodeId))
            return;

        lock (_capturesSync)
        {
            if (!_excludedNodesByWindow.TryGetValue(windowId, out HashSet<string>? excludedNodeIds))
            {
                excludedNodeIds = new HashSet<string>(StringComparer.Ordinal);
                _excludedNodesByWindow[windowId] = excludedNodeIds;
            }

            if (excludedNodeIds.Count >= 24)
                excludedNodeIds.Clear();

            _ = excludedNodeIds.Add(nodeId);
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

    private static string BuildSearchText(JsonElement propsElement, string nodeId, string nodeName, string nodeDescription)
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

    private string RunPortalDesktopMethod(
        string method,
        IReadOnlyList<string> methodArguments,
        int timeoutMs,
        string destination = PortalDesktopDestination)
    {
        var args = new List<string>
        {
            "call",
            "--session",
            "--dest",
            destination,
            "--object-path",
            "/org/freedesktop/portal/desktop",
            "--method",
            method
        };
        args.AddRange(methodArguments);

        return _gdbus.Execute(args, timeoutMs);
    }

    private static string? ExtractObjectPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        Match match = ObjectPathRegex.Match(value);
        if (!match.Success)
            return null;

        return match.Groups[1].Value;
    }

    private static string? ExtractRequestSender(string requestPath)
    {
        if (string.IsNullOrWhiteSpace(requestPath))
            return null;

        string[] parts = requestPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return null;

        return parts[^2];
    }

    private void ClosePortalSession(string sessionPath, string? sessionDestination = null)
    {
        if (string.IsNullOrWhiteSpace(sessionPath))
            return;

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            ClosePortalSessionAsync(sessionPath, sessionDestination, timeoutCts.Token).GetAwaiter().GetResult();
        }
        catch
        {
            // Best effort close.
        }
    }

    private async Task ClosePortalSessionAsync(string sessionPath, CancellationToken cancellationToken)
    {
        await ClosePortalSessionAsync(sessionPath, PortalDesktopDestination, cancellationToken).ConfigureAwait(false);
    }

    private async Task ClosePortalSessionAsync(
        string sessionPath,
        string? sessionDestination,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionPath))
            return;

        string destination = string.IsNullOrWhiteSpace(sessionDestination)
            ? PortalDesktopDestination
            : sessionDestination;

        Connection? connection = await EnsureSessionBusConnectionAsync(cancellationToken).ConfigureAwait(false);
        if (connection is null)
            return;

        try
        {
            if (!string.Equals(destination, PortalDesktopDestination, StringComparison.Ordinal))
            {
                IKdePortalSession kdeSession = connection.CreateProxy<IKdePortalSession>(
                    destination,
                    new ObjectPath(sessionPath));

                await kdeSession.CloseAsync()
                    .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            IPipeWirePortalSession session = connection.CreateProxy<IPipeWirePortalSession>(
                PortalDesktopDestination,
                new ObjectPath(sessionPath));

            await session.CloseAsync()
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
            Environment.GetEnvironmentVariable("DESKTOP_SESSION")
        ];

        for (int index = 0; index < candidates.Length; index++)
        {
            string candidate = candidates[index] ?? string.Empty;
            if (candidate.Contains("kde", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("plasma", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        string kdeFullSession = Environment.GetEnvironmentVariable("KDE_FULL_SESSION") ?? string.Empty;
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
                    "/org/freedesktop/portal/desktop"
                ],
                timeoutMs: 1_500);
            return output.Contains("org.freedesktop.impl.portal.ScreenCast", StringComparison.Ordinal);
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
            _ => string.Empty
        };
    }

    private readonly record struct PortalCaptureBootstrap(
        string NodeId,
        string SessionPath,
        string SessionDestination,
        CloseSafeHandle? PipeWireRemoteHandle);

    private readonly record struct PortalRequestResponse(
        uint ResponseCode,
        IDictionary<string, object> Results);

    private readonly record struct PortalRestoreData(string Provider, uint Version, byte[] Bytes);

    private readonly record struct WindowIdentity(
        string WindowTitle,
        string ProcessName,
        int TitleOrdinal,
        int ProcessTitleOrdinal,
        int TitleCount,
        int ProcessTitleCount);

    private readonly record struct PortalStreamDescriptor(string NodeId, string? StableId, string NormalizedSearchText);

    private readonly record struct NodeCandidate(string Id, int Score, string NormalizedSearchText);

    private enum MatchState
    {
        Unknown = 0,
        Match = 1,
        Mismatch = 2
    }

    private sealed class WindowCaptureContext(
        string windowId,
        string nodeId,
        string? portalSessionPath,
        string? portalSessionDestination,
        PipeWireWindowStream stream)
    {
        public string WindowId { get; } = windowId;
        public string NodeId { get; } = nodeId;
        public string? PortalSessionPath { get; } = portalSessionPath;
        public string? PortalSessionDestination { get; } = portalSessionDestination;
        public PipeWireWindowStream Stream { get; } = stream;
        public int ConsecutiveFailures { get; set; }
        public int ConsecutiveNoFrameTimeouts { get; set; }
        public bool ForceFallback { get; set; }
        public bool FirstDeliveredFrameLogged { get; set; }
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

        public readonly record struct FrameSnapshot(long Sequence, byte[] Bytes);

        private readonly string _nodeId;
        private readonly int _fpsNumerator;
        private readonly int _fpsDenominator;
        private readonly IGstLaunchWrapper _gstLaunch;
        private readonly CloseSafeHandle? _pipeWireRemoteHandle;
        private readonly object _syncRoot = new();
        private readonly SemaphoreSlim _frameReadySignal = new(initialCount: 0, maxCount: 1);

        private Process? _process;
        private CancellationTokenSource? _cts;
        private Task? _readerTask;
        private byte[]? _latestFrameBytes;
        private long _latestFrameSequence;
        private bool _hasReceivedFrame;
        private bool _disposed;
        private bool _faulted;
        private bool _restartInProgress;

        public PipeWireWindowStream(
            string nodeId,
            int fpsNumerator,
            int fpsDenominator,
            IGstLaunchWrapper gstLaunch,
            CloseSafeHandle? pipeWireRemoteHandle)
        {
            ArgumentNullException.ThrowIfNull(gstLaunch);
            _nodeId = nodeId;
            _fpsNumerator = fpsNumerator;
            _fpsDenominator = fpsDenominator;
            _gstLaunch = gstLaunch;
            _pipeWireRemoteHandle = pipeWireRemoteHandle;
        }

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
            Process? process = _gstLaunch.StartPipeWireJpegStream(
                _nodeId,
                _fpsNumerator,
                _fpsDenominator,
                remoteFd);
            var cts = new CancellationTokenSource();
            if (process is null)
            {
                lock (_syncRoot)
                {
                    _faulted = true;
                    _restartInProgress = false;
                }

                cts.Dispose();
                PipeWireTrace.Write($"PipeWireStream restart failed nodeId={_nodeId} remoteFd={(remoteFd?.ToString(CultureInfo.InvariantCulture) ?? "null")}");
                return;
            }

            lock (_syncRoot)
            {
                _faulted = false;
                _hasReceivedFrame = false;
                _process = process;
                _cts = cts;
                _readerTask = Task.Run(() => ReadLoop(process, cts.Token));
                _ = Task.Run(() => DrainErrors(process, cts.Token));
                _restartInProgress = false;
            }

            PipeWireTrace.Write($"PipeWireStream started nodeId={_nodeId} remoteFd={(remoteFd?.ToString(CultureInfo.InvariantCulture) ?? "null")} pid={process.Id}");

            DrainFrameSignal();
        }

        public async Task<Bitmap?> GetFrameAsync(int timeoutMs, CancellationToken cancellationToken)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(Math.Clamp(timeoutMs, 100, 10_000));
            CancellationToken token = linkedCts.Token;

            while (!token.IsCancellationRequested)
            {
                FrameSnapshot? snapshot = GetFrameSnapshotAfter(-1);
                if (snapshot is not null)
                {
                    return CreateBitmap(snapshot.Value.Bytes);
                }

                if (IsFaulted())
                    return null;

                try
                {
                    await _frameReadySignal.WaitAsync(token).ConfigureAwait(false);
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

        public async Task<FrameSnapshot?> WaitForNextFrameAsync(
            long afterSequence,
            int timeoutMs,
            CancellationToken cancellationToken)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(Math.Clamp(timeoutMs, 100, 10_000));
            CancellationToken token = linkedCts.Token;

            while (!token.IsCancellationRequested)
            {
                FrameSnapshot? snapshot = GetFrameSnapshotAfter(afterSequence);
                if (snapshot is not null)
                    return snapshot;

                if (IsFaulted())
                    return null;

                try
                {
                    await _frameReadySignal.WaitAsync(token).ConfigureAwait(false);
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

                return new FrameSnapshot(_latestFrameSequence, _latestFrameBytes);
            }
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
                string stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    string reduced = stderr.Length > 4_000 ? stderr[..4_000] : stderr;
                    PipeWireTrace.Write($"PipeWireStream stderr nodeId={_nodeId}: {reduced.Replace('\n', ' ').Replace('\r', ' ')}");
                }
            }
            catch
            {
            }
        }

        private void ReadLoop(Process process, CancellationToken cancellationToken)
        {
            try
            {
                var frameBuffer = new List<byte>(256 * 1024);
                byte[] readBuffer = new byte[16 * 1024];
                Stream output = process.StandardOutput.BaseStream;

                bool inFrame = false;
                byte previous = 0;

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
                                frameBuffer.Clear();
                                frameBuffer.Add(0xFF);
                                frameBuffer.Add(0xD8);
                            }

                            previous = current;
                            continue;
                        }

                        frameBuffer.Add(current);

                        if (frameBuffer.Count > MaxFrameBytes)
                        {
                            inFrame = false;
                            frameBuffer.Clear();
                            previous = current;
                            continue;
                        }

                        if (previous == 0xFF && current == 0xD9)
                        {
                            byte[] frame = frameBuffer.ToArray();
                            lock (_syncRoot)
                            {
                                _latestFrameBytes = frame;
                                _latestFrameSequence++;
                                _hasReceivedFrame = true;
                            }
                            if (_latestFrameSequence == 1)
                            {
                                string fingerprint = ComputeFrameFingerprint(frame);
                                PipeWireTrace.Write($"PipeWireStream first frame nodeId={_nodeId} bytes={frame.Length} sha256={fingerprint}");
                            }
                            SignalFrameReady();

                            inFrame = false;
                            frameBuffer.Clear();
                        }

                        previous = current;
                    }
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                lock (_syncRoot)
                    _faulted = true;
                SignalFrameReady();
            }
        }

        private void Stop()
        {
            Process? process;
            CancellationTokenSource? cts;
            Task? readerTask;

            lock (_syncRoot)
            {
                process = _process;
                cts = _cts;
                readerTask = _readerTask;
                _process = null;
                _cts = null;
                _readerTask = null;
                _latestFrameBytes = null;
                _hasReceivedFrame = false;
                _faulted = true;
            }

            SignalFrameReady();
            DrainFrameSignal();

            if (cts is not null)
            {
                try { cts.Cancel(); } catch { }
                cts.Dispose();
            }

            if (process is not null)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch
                {
                }
                process.Dispose();
            }

            if (readerTask is not null)
            {
                try { readerTask.Wait(250); } catch { }
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
            if (_pipeWireRemoteHandle is null || _pipeWireRemoteHandle.IsClosed || _pipeWireRemoteHandle.IsInvalid)
                return null;

            long value = _pipeWireRemoteHandle.DangerousGetHandle().ToInt64();
            return value is >= 0 and <= int.MaxValue
                ? (int)value
                : null;
        }

        private static string ComputeFrameFingerprint(byte[] bytes)
        {
            if (bytes.Length == 0)
                return "empty";

            byte[] hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
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
                while (_frameReadySignal.Wait(0))
                {
                }
            }
            catch
            {
                // Dispose/shutdown path.
            }
        }
    }
}
