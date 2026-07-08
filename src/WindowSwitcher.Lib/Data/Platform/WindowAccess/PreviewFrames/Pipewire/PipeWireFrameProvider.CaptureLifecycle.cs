using System.Globalization;
using Avalonia.Media.Imaging;
using Tmds.DBus;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Pipewire;

public sealed partial class PipeWireFrameProvider
{
    private async Task<WindowCaptureContext?> EnsureCaptureAsync(
        string windowId,
        ScreenshotRequest request,
        CancellationToken cancellationToken
    )
    {
        WindowCaptureContext? reusableCapture = null;
        Task<WindowCaptureContext?>? createTask = null;
        lock (_capturesSync)
        {
            if (_captures.TryGetValue(windowId, out WindowCaptureContext? existing))
            {
                reusableCapture = existing;
            }
            else if (!_captureCreationTasks.TryGetValue(windowId, out createTask!))
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

        if (reusableCapture is not null)
        {
            reusableCapture.ApplyRequest(request);
            return reusableCapture;
        }

        try
        {
            WindowCaptureContext? capture = await createTask!
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            capture?.ApplyRequest(request);
            return capture;
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
                    created.Dispose(ClosePortalSessionSynchronously);
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
            return null;

        int? targetWidthPx = NormalizeTargetDimension(request.MaxWidthPx);
        int? targetHeightPx = NormalizeTargetDimension(request.MaxHeightPx);

        string? nodeId = null;
        string? portalSessionPath = null;
        string? portalSessionDestination = null;
        CloseSafeHandle? pipeWireRemoteHandle = null;

        if (_isWaylandSession)
        {
            PortalCaptureBootstrap? started = null;

            if (_isKdeDesktopSession)
            {
                started = await TryStartKdeBackendWindowScreencastAsync(
                        windowId,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
            else
            {
                started = await TryStartWaylandPortalWindowScreencastAsync(
                        windowId,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }

            if (started is null)
                return null;

            nodeId = started.Value.NodeId;
            portalSessionPath = started.Value.SessionPath;
            portalSessionDestination = started.Value.SessionDestination;
            pipeWireRemoteHandle = started.Value.PipeWireRemoteHandle;
        }
        else
        {
            nodeId = ResolveNodeIdFromPwDump(windowId, allowBestCandidate: false);
        }

        if (string.IsNullOrWhiteSpace(nodeId))
            return null;

        var stream = new PipeWireWindowStream(
            nodeId,
            _gstLaunch,
            pipeWireRemoteHandle,
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

        return _waylandScreenCastMemoryCache.GetRestoreToken(restoreKeys);
    }

    private void SaveStoredWaylandScreenCastRestoreToken(string windowId, string restoreToken)
    {
        IReadOnlyList<string> restoreKeys = BuildWaylandRestoreKeys(windowId);
        if (restoreKeys.Count == 0)
            return;

        ArgumentException.ThrowIfNullOrWhiteSpace(restoreToken);
        _waylandScreenCastMemoryCache.SetRestoreToken(restoreKeys, restoreToken);
    }

    private string? GetStoredWaylandScreenCastRestoreData(string windowId)
    {
        IReadOnlyList<string> restoreKeys = BuildWaylandRestoreKeys(windowId);
        if (restoreKeys.Count == 0)
            return null;

        string? windowTitle = TryGetWindowTitleById(windowId);
        IReadOnlyList<string> preferredValues = _waylandScreenCastMemoryCache
            .GetRestoreDataCandidates(restoreKeys);

        if (preferredValues.Count == 0)
            return null;

        if (string.IsNullOrWhiteSpace(windowTitle))
            return preferredValues[0];

        for (int index = 0; index < preferredValues.Count; index++)
        {
            string candidate = preferredValues[index];
            if (SerializedRestoreDataMatchesTitle(candidate, windowTitle))
                return candidate;
        }

        return null;
    }

    private void SaveStoredWaylandScreenCastRestoreData(
        string windowId,
        string serializedRestoreData
    )
    {
        IReadOnlyList<string> restoreKeys = BuildWaylandRestoreKeys(windowId);
        if (restoreKeys.Count == 0 || string.IsNullOrWhiteSpace(serializedRestoreData))
            return;

        _waylandScreenCastMemoryCache.SetRestoreData(restoreKeys, serializedRestoreData);
    }

    private string? GetStoredWaylandScreenCastStreamId(string windowId)
    {
        IReadOnlyList<string> restoreKeys = BuildWaylandRestoreKeys(windowId);
        if (restoreKeys.Count == 0)
            return null;

        return _waylandScreenCastMemoryCache.GetStreamId(restoreKeys);
    }

    private void SaveStoredWaylandScreenCastStreamId(string windowId, string streamId)
    {
        IReadOnlyList<string> restoreKeys = BuildWaylandRestoreKeys(windowId);
        if (restoreKeys.Count == 0 || string.IsNullOrWhiteSpace(streamId))
            return;

        _waylandScreenCastMemoryCache.SetStreamId(restoreKeys, streamId);
    }

    private void ClearStoredWaylandScreenCastArtifacts(string windowId)
    {
        IReadOnlyList<string> restoreKeys = BuildWaylandRestoreKeys(windowId);
        if (restoreKeys.Count == 0)
            return;

        _waylandScreenCastMemoryCache.ClearArtifacts(restoreKeys);
    }

    private void ClearStoredWaylandScreenCastStreamId(string windowId)
    {
        IReadOnlyList<string> restoreKeys = BuildWaylandRestoreKeys(windowId);
        if (restoreKeys.Count == 0)
            return;

        _waylandScreenCastMemoryCache.ClearStreamId(restoreKeys);
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
                    AddRestoreKey($"process_title:{normalizedProcess}|{normalizedTitle}");
            }

            if (!string.IsNullOrWhiteSpace(normalizedTitle))
            {
                if (identity.TitleOrdinal > 0)
                {
                    AddRestoreKey(
                        $"title:{normalizedTitle}#{identity.TitleOrdinal.ToString(CultureInfo.InvariantCulture)}"
                    );
                }

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

    private async Task<Bitmap?> TryRequestCaptureFrameAsync(
        WindowCaptureContext capture,
        ScreenshotRequest request,
        CancellationToken cancellationToken
    )
    {
        if (capture.IsDisposed)
            return null;

        int timeoutMs = request.TimeoutMs;
        using NativeBgraPreviewFrame? nativeFrame = capture.Stream.PullNativeFrame(
            timeoutMs,
            cancellationToken
        );
        Bitmap? frame = nativeFrame is null ? null : CreateBitmap(nativeFrame);
        if (frame is not null)
        {
            capture.ConsecutiveFailures = 0;
            capture.ConsecutiveNoFrameTimeouts = 0;
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
        using NativeBgraPreviewFrame? restartedNativeFrame = capture.Stream.PullNativeFrame(
            timeoutMs,
            cancellationToken
        );
        frame = restartedNativeFrame is null ? null : CreateBitmap(restartedNativeFrame);
        if (frame is not null)
        {
            capture.ConsecutiveFailures = 0;
            capture.ConsecutiveNoFrameTimeouts = 0;
            return frame;
        }

        if (_isWaylandSession && !capture.Stream.HasReceivedFrame())
        {
            int bootstrapTimeoutMs = Math.Max(timeoutMs, 1_200);
            using NativeBgraPreviewFrame? bootstrapNativeFrame = capture.Stream.PullNativeFrame(
                bootstrapTimeoutMs,
                cancellationToken
            );
            frame = bootstrapNativeFrame is null ? null : CreateBitmap(bootstrapNativeFrame);
            if (frame is not null)
            {
                capture.ConsecutiveFailures = 0;
                capture.ConsecutiveNoFrameTimeouts = 0;
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
}
