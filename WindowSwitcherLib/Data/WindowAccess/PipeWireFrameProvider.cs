using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Media.Imaging;
using WindowSwitcherLib.Data;
using WindowSwitcherLib.Data.Commands;
using WindowSwitcherLib.Data.FileAccess;
using WindowSwitcherLib.Models;

namespace WindowSwitcherLib.Data.WindowAccess;

public sealed class PipeWireFrameProvider : IPreviewFrameProvider
{
    private readonly ScreenshotPreviewFrameProvider _fallbackProvider;
    private readonly object _capturesSync = new();
    private readonly Dictionary<string, WindowCaptureContext> _captures = new(StringComparer.Ordinal);
    private readonly HashSet<string> _failedWindows = new(StringComparer.Ordinal);
    private readonly bool _activateLogs;
    private readonly int _reconnectDelayMs;
    private readonly int _fps;
    private readonly bool _isWaylandSession;
    private readonly string? _configuredNodeId;
    private readonly bool _allowPortalFallback;
    private bool _disposed;

    public PipeWireFrameProvider(WinAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);

        _fallbackProvider = new ScreenshotPreviewFrameProvider(accessor);

        var config = ConfigFileAccessor.GetInstance().ReadConfig(value => new
        {
            value.ActivateLogs,
            value.LinuxPipeWireFps,
            value.LinuxPipeWireReconnectDelayMs,
            value.LinuxPipeWireNodeId
        });

        _activateLogs = config.ActivateLogs;
        _fps = Math.Clamp(config.LinuxPipeWireFps, 1, 60);
        _reconnectDelayMs = Math.Clamp(config.LinuxPipeWireReconnectDelayMs, 100, 30_000);
        _configuredNodeId = GetConfiguredOrEnvironmentNodeId(config.LinuxPipeWireNodeId);
        _isWaylandSession = IsWaylandSession();
        _allowPortalFallback = ParseBooleanEnvironment("WINDOW_SWITCHER_PIPEWIRE_ALLOW_PORTAL");

        if (!string.IsNullOrWhiteSpace(_configuredNodeId))
            LogWarn($"Using configured PipeWire node `{_configuredNodeId}`.");
        if (_allowPortalFallback)
            LogWarn("PipeWire portal fallback is enabled by environment variable.");
    }

    public async Task<Bitmap?> RequestAsync(string windowId, ScreenshotRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_disposed)
                return null;

            if (string.IsNullOrWhiteSpace(windowId) || !RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return await RequestFallbackAsync(windowId, request, cancellationToken).ConfigureAwait(false);

            WindowCaptureContext? capture = EnsureCapture(windowId);
            if (capture is null || capture.ForceFallback)
                return await RequestFallbackAsync(windowId, request, cancellationToken).ConfigureAwait(false);

            Bitmap? frame = await TryRequestCaptureFrameAsync(capture, request, cancellationToken).ConfigureAwait(false);
            if (frame is not null)
                return frame;

            return await RequestFallbackAsync(windowId, request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            LogWarn($"PipeWire request failed unexpectedly: {ex.Message}");
            return await RequestFallbackAsync(windowId, request, cancellationToken).ConfigureAwait(false);
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
            _failedWindows.Clear();
        }

        foreach (WindowCaptureContext capture in captures)
            capture.Dispose(ClosePortalSession);

        _fallbackProvider.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        await _fallbackProvider.DisposeAsync().ConfigureAwait(false);
    }

    private WindowCaptureContext? EnsureCapture(string windowId)
    {
        lock (_capturesSync)
        {
            if (_failedWindows.Contains(windowId))
                return null;

            if (_captures.TryGetValue(windowId, out WindowCaptureContext? existing))
                return existing;
        }

        WindowCaptureContext? created = CreateCapture(windowId);
        if (created is null)
        {
            lock (_capturesSync)
                _failedWindows.Add(windowId);
            return null;
        }

        lock (_capturesSync)
        {
            if (_captures.TryGetValue(windowId, out WindowCaptureContext? existing))
            {
                created.Dispose(ClosePortalSession);
                return existing;
            }

            _failedWindows.Remove(windowId);
            _captures[windowId] = created;
            return created;
        }
    }

    private WindowCaptureContext? CreateCapture(string windowId)
    {
        string? nodeId = null;
        string? portalSessionPath = null;

        if (!string.IsNullOrWhiteSpace(_configuredNodeId))
        {
            nodeId = _configuredNodeId;
        }
        else
        {
            nodeId = ResolveNodeIdFromPwDump(windowId);
            if (string.IsNullOrWhiteSpace(nodeId) && _isWaylandSession && _allowPortalFallback)
            {
                LogWarn($"No wmctrl-matching PipeWire node found for `{windowId}`. Trying portal fallback.");
                nodeId = TryStartPortalWindowScreencast(windowId, out portalSessionPath);
            }
        }

        if (string.IsNullOrWhiteSpace(nodeId))
        {
            LogWarn($"PipeWire node not found for window `{windowId}` (wmctrl id matching).");
            return null;
        }

        LogWarn($"PipeWire node `{nodeId}` assigned to window `{windowId}`.");
        var stream = new PipeWireWindowStream(nodeId, _fps, _activateLogs, LogWarn);
        return new WindowCaptureContext(windowId, nodeId, portalSessionPath, stream);
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
            return frame;
        }

        bool needsRestart = capture.Stream.NeedsRestart() || !capture.Stream.HasReceivedFrame();
        if (!needsRestart)
            return null;

        if (_reconnectDelayMs > 0)
        {
            try
            {
                await Task.Delay(Math.Min(_reconnectDelayMs, timeoutMs), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        capture.Stream.Restart();
        frame = await capture.Stream.GetFrameAsync(timeoutMs, cancellationToken).ConfigureAwait(false);
        if (frame is not null)
        {
            capture.ConsecutiveFailures = 0;
            return frame;
        }

        capture.ConsecutiveFailures++;
        if (capture.ConsecutiveFailures < 3)
            return null;

        capture.ForceFallback = true;
        capture.Stream.Dispose();
        if (!string.IsNullOrWhiteSpace(capture.PortalSessionPath))
            ClosePortalSession(capture.PortalSessionPath);

        LogWarn($"PipeWire stream failed repeatedly for window `{capture.WindowId}`. Switching this window to screenshot fallback.");
        return null;
    }

    private string? TryStartPortalWindowScreencast(string windowId, out string? sessionPath)
    {
        sessionPath = null;

        if (!LinuxDependencies.IsGdbusAvailable || !LinuxDependencies.IsPwDumpAvailable)
            return null;

        List<NodeCandidate> baseline = GetPipeWireNodeCandidates();
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
        {
            LogWarn("Portal CreateSession did not return a request path.");
            return null;
        }

        string? sender = ExtractRequestSender(createRequestPath);
        if (string.IsNullOrWhiteSpace(sender))
        {
            LogWarn("Unable to resolve portal sender id from request path.");
            return null;
        }

        sessionPath = $"/org/freedesktop/portal/desktop/session/{sender}/{sessionToken}";

        string selectOptions = $"{{'types': <uint32 2>, 'multiple': <false>, 'handle_token': <'{selectToken}'>}}";
        _ = RunPortalDesktopMethod(
            "org.freedesktop.portal.ScreenCast.SelectSources",
            [sessionPath, selectOptions],
            timeoutMs: 5_000);

        string startOptions = $"{{'handle_token': <'{startToken}'>}}";
        _ = RunPortalDesktopMethod(
            "org.freedesktop.portal.ScreenCast.Start",
            [sessionPath, string.Empty, startOptions],
            timeoutMs: 5_000);

        string? nodeId = WaitForNewPipeWireNodeId(baselineIds, TimeSpan.FromSeconds(20), windowId);
        if (!string.IsNullOrWhiteSpace(nodeId))
            return nodeId;

        ClosePortalSession(sessionPath);
        sessionPath = null;
        return null;
    }

    private string? ResolveNodeIdFromPwDump(string windowId)
    {
        if (!LinuxDependencies.IsPwDumpAvailable)
            return null;

        List<NodeCandidate> nodes = GetPipeWireNodeCandidates();
        IReadOnlyCollection<string> normalizedWindowIds = BuildWindowIdPatterns(windowId);
        if (normalizedWindowIds.Count == 0)
            return null;

        NodeCandidate? matching = nodes
            .Where(candidate => MatchesWindowId(candidate, normalizedWindowIds))
            .OrderByDescending(candidate => candidate.Score)
            .FirstOrDefault();
        if (matching is not null)
            return matching.Value.Id;

        return null;
    }

    private static string? WaitForNewPipeWireNodeId(
        HashSet<string> baselineIds,
        TimeSpan timeout,
        string windowId)
    {
        IReadOnlyCollection<string> normalizedWindowIds = BuildWindowIdPatterns(windowId);
        DateTime deadline = DateTime.UtcNow + timeout;
        NodeCandidate? bestNewCandidate = null;

        while (DateTime.UtcNow < deadline)
        {
            List<NodeCandidate> current = GetPipeWireNodeCandidates();
            List<NodeCandidate> newCandidates = current
                .Where(candidate => !baselineIds.Contains(candidate.Id))
                .ToList();

            NodeCandidate? matchingNew = newCandidates
                .Where(candidate => MatchesWindowId(candidate, normalizedWindowIds))
                .OrderByDescending(candidate => candidate.Score)
                .FirstOrDefault();
            if (matchingNew is not null)
                return matchingNew.Value.Id;

            NodeCandidate? bestThisRound = newCandidates
                .OrderByDescending(candidate => candidate.Score)
                .FirstOrDefault();

            if (bestThisRound is not null && (bestNewCandidate is null || bestThisRound.Value.Score > bestNewCandidate.Value.Score))
                bestNewCandidate = bestThisRound;

            Thread.Sleep(300);
        }

        if (bestNewCandidate is not null)
            return bestNewCandidate.Value.Id;

        List<NodeCandidate> fallback = GetPipeWireNodeCandidates();
        NodeCandidate? matchingFallback = fallback
            .Where(candidate => MatchesWindowId(candidate, normalizedWindowIds))
            .OrderByDescending(candidate => candidate.Score)
            .FirstOrDefault();
        if (matchingFallback is not null)
            return matchingFallback.Value.Id;

        return fallback.OrderByDescending(candidate => candidate.Score).Select(candidate => candidate.Id).FirstOrDefault();
    }

    private static bool MatchesWindowId(NodeCandidate candidate, IReadOnlyCollection<string> normalizedWindowIds)
    {
        if (normalizedWindowIds.Count == 0)
            return false;

        string normalizedSearch = NormalizeForSearch(candidate.SearchText);
        foreach (string normalizedWindowId in normalizedWindowIds)
        {
            if (normalizedSearch.Contains(normalizedWindowId, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static string NormalizeForSearch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9x]+", string.Empty);
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

    private string? GetConfiguredOrEnvironmentNodeId(string? configuredNodeId)
    {
        if (!string.IsNullOrWhiteSpace(configuredNodeId))
            return configuredNodeId.Trim();

        string? envNodeId = Environment.GetEnvironmentVariable("WINDOW_SWITCHER_PIPEWIRE_NODE_ID");
        if (!string.IsNullOrWhiteSpace(envNodeId))
            return envNodeId.Trim();

        return null;
    }

    private static bool ParseBooleanEnvironment(string variableName)
    {
        string? raw = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        return raw.Trim() switch
        {
            "1" => true,
            "true" => true,
            "TRUE" => true,
            "yes" => true,
            "YES" => true,
            "on" => true,
            "ON" => true,
            _ => false
        };
    }

    private async Task<Bitmap?> RequestFallbackAsync(string windowId, ScreenshotRequest request, CancellationToken cancellationToken)
    {
        var safeRequest = new ScreenshotRequest(
            MaxWidthPx: request.MaxWidthPx,
            MaxHeightPx: request.MaxHeightPx,
            TimeoutMs: Math.Clamp(Math.Max(request.TimeoutMs, 1_500), 100, 10_000));

        return await _fallbackProvider.RequestAsync(windowId, safeRequest, cancellationToken).ConfigureAwait(false);
    }

    private static List<NodeCandidate> GetPipeWireNodeCandidates()
    {
        string output = RunCommand("pw-dump", string.Empty, 2_500);
        if (string.IsNullOrWhiteSpace(output))
            return new List<NodeCandidate>();

        var candidates = new List<NodeCandidate>();

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
                    if (nodeName.Contains("portal", StringComparison.OrdinalIgnoreCase) ||
                        nodeName.Contains("screen", StringComparison.OrdinalIgnoreCase) ||
                        nodeName.Contains("monitor", StringComparison.OrdinalIgnoreCase))
                        score += 4;
                    if (nodeDescription.Contains("screencast", StringComparison.OrdinalIgnoreCase) ||
                        nodeDescription.Contains("screen", StringComparison.OrdinalIgnoreCase) ||
                        nodeDescription.Contains("monitor", StringComparison.OrdinalIgnoreCase))
                        score += 3;

                    searchText = BuildSearchText(propsElement, nodeId, nodeName, nodeDescription);
                }

                if (score > 0)
                    candidates.Add(new NodeCandidate(nodeId, score, searchText));
            }
        }
        catch
        {
            return new List<NodeCandidate>();
        }

        return candidates;
    }

    private static string BuildSearchText(JsonElement propsElement, string nodeId, string nodeName, string nodeDescription)
    {
        var parts = new List<string> { nodeId, nodeName, nodeDescription };
        foreach (JsonProperty property in propsElement.EnumerateObject())
        {
            string value = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                JsonValueKind.Number => property.Value.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => string.Empty
            };

            if (!string.IsNullOrWhiteSpace(value))
                parts.Add(value);
        }

        return string.Join('|', parts);
    }

    private static string GetPropertyValue(JsonElement propsElement, string propertyName)
    {
        if (!propsElement.TryGetProperty(propertyName, out JsonElement value))
            return string.Empty;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
    }

    private static string RunPortalDesktopMethod(string method, IReadOnlyList<string> methodArguments, int timeoutMs)
    {
        var args = new List<string>
        {
            "call",
            "--session",
            "--dest",
            "org.freedesktop.portal.Desktop",
            "--object-path",
            "/org/freedesktop/portal/desktop",
            "--method",
            method
        };
        args.AddRange(methodArguments);

        return RunCommand("gdbus", args, timeoutMs);
    }

    private static string RunPortalSessionMethod(string sessionPath, string method, IReadOnlyList<string> methodArguments, int timeoutMs)
    {
        var args = new List<string>
        {
            "call",
            "--session",
            "--dest",
            "org.freedesktop.portal.Desktop",
            "--object-path",
            sessionPath,
            "--method",
            method
        };
        args.AddRange(methodArguments);

        return RunCommand("gdbus", args, timeoutMs);
    }

    private static string? ExtractObjectPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        Match match = Regex.Match(value, @"'(/org/[^']+)'");
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

    private static void ClosePortalSession(string sessionPath)
    {
        if (string.IsNullOrWhiteSpace(sessionPath))
            return;

        _ = RunPortalSessionMethod(sessionPath, "org.freedesktop.portal.Session.Close", Array.Empty<string>(), timeoutMs: 2_000);
    }

    private static string RunCommand(string fileName, string arguments, int timeoutMs)
    {
        using Process process = new();
        process.StartInfo.FileName = fileName;
        process.StartInfo.Arguments = arguments;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = true;

        try
        {
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            _ = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return string.Empty;
            }

            return process.ExitCode == 0 ? output : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string RunCommand(string fileName, IReadOnlyList<string> arguments, int timeoutMs)
    {
        using Process process = new();
        process.StartInfo.FileName = fileName;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.Arguments = string.Empty;
        process.StartInfo.ArgumentList.Clear();
        foreach (string argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        try
        {
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            _ = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return string.Empty;
            }

            return process.ExitCode == 0 ? output : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsWaylandSession()
    {
        string? sessionType = LinuxSessionDetector.GetSessionType();
        return string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase);
    }

    private void LogWarn(string message)
    {
        if (!_activateLogs)
            return;

        AppLogger.Log($"[PipeWireProvider] {message}", StaticData.LogSeverity.WARN);
    }

    private readonly record struct NodeCandidate(string Id, int Score, string SearchText);

    private sealed class WindowCaptureContext
    {
        public string WindowId { get; }
        public string NodeId { get; }
        public string? PortalSessionPath { get; }
        public PipeWireWindowStream Stream { get; }
        public int ConsecutiveFailures { get; set; }
        public bool ForceFallback { get; set; }
        public bool IsDisposed { get; private set; }

        public WindowCaptureContext(string windowId, string nodeId, string? portalSessionPath, PipeWireWindowStream stream)
        {
            WindowId = windowId;
            NodeId = nodeId;
            PortalSessionPath = portalSessionPath;
            Stream = stream;
        }

        public void Dispose(Action<string> closePortalSession)
        {
            if (IsDisposed)
                return;

            IsDisposed = true;
            Stream.Dispose();
            if (!string.IsNullOrWhiteSpace(PortalSessionPath))
                closePortalSession(PortalSessionPath);
        }
    }

    private sealed class PipeWireWindowStream : IDisposable
    {
        private const int MaxFrameBytes = 16 * 1024 * 1024;

        private readonly string _nodeId;
        private readonly int _fps;
        private readonly bool _activateLogs;
        private readonly Action<string> _logWarn;
        private readonly object _syncRoot = new();

        private Process? _process;
        private CancellationTokenSource? _cts;
        private Task? _readerTask;
        private byte[]? _latestFrameBytes;
        private bool _hasReceivedFrame;
        private bool _disposed;
        private bool _faulted;
        private bool _restartInProgress;

        public PipeWireWindowStream(string nodeId, int fps, bool activateLogs, Action<string> logWarn)
        {
            _nodeId = nodeId;
            _fps = fps;
            _activateLogs = activateLogs;
            _logWarn = logWarn;
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

            var process = new Process();
            process.StartInfo.FileName = "gst-launch-1.0";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.ArgumentList.Add("-q");
            process.StartInfo.ArgumentList.Add("pipewiresrc");
            process.StartInfo.ArgumentList.Add($"path={_nodeId}");
            process.StartInfo.ArgumentList.Add("do-timestamp=true");
            process.StartInfo.ArgumentList.Add("!");
            process.StartInfo.ArgumentList.Add("videorate");
            process.StartInfo.ArgumentList.Add("!");
            process.StartInfo.ArgumentList.Add($"video/x-raw,framerate={_fps}/1");
            process.StartInfo.ArgumentList.Add("!");
            process.StartInfo.ArgumentList.Add("videoconvert");
            process.StartInfo.ArgumentList.Add("!");
            process.StartInfo.ArgumentList.Add("jpegenc");
            process.StartInfo.ArgumentList.Add("quality=80");
            process.StartInfo.ArgumentList.Add("!");
            process.StartInfo.ArgumentList.Add("fdsink");
            process.StartInfo.ArgumentList.Add("fd=1");
            process.StartInfo.ArgumentList.Add("sync=false");

            var cts = new CancellationTokenSource();
            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                _faulted = true;
                lock (_syncRoot)
                {
                    _restartInProgress = false;
                }

                if (_activateLogs)
                    _logWarn($"Failed to start GStreamer PipeWire stream: {ex.Message}");

                process.Dispose();
                cts.Dispose();
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
        }

        public async Task<Bitmap?> GetFrameAsync(int timeoutMs, CancellationToken cancellationToken)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(Math.Clamp(timeoutMs, 100, 10_000));
            CancellationToken token = linkedCts.Token;

            while (!token.IsCancellationRequested)
            {
                byte[]? bytes = GetLatestFrameBytes();
                if (bytes is not null)
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

                if (_faulted)
                    return null;

                try
                {
                    await Task.Delay(40, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
            }

            return null;
        }

        private byte[]? GetLatestFrameBytes()
        {
            lock (_syncRoot)
            {
                if (_latestFrameBytes is null)
                    return null;

                byte[] clone = new byte[_latestFrameBytes.Length];
                Buffer.BlockCopy(_latestFrameBytes, 0, clone, 0, _latestFrameBytes.Length);
                return clone;
            }
        }

        private async Task DrainErrors(Process process, CancellationToken cancellationToken)
        {
            try
            {
                string errorText = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                if (_activateLogs && !string.IsNullOrWhiteSpace(errorText))
                    _logWarn($"GStreamer stderr: {errorText.Trim()}");
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
                                _hasReceivedFrame = true;
                            }

                            inFrame = false;
                            frameBuffer.Clear();
                        }

                        previous = current;
                    }
                }
            }
            catch (Exception ex)
            {
                if (_activateLogs)
                    _logWarn($"PipeWire stream read failed: {ex.Message}");
            }
            finally
            {
                _faulted = true;
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
            }

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
        }
    }
}
