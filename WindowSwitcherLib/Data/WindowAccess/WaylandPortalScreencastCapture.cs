using System.Diagnostics;
using System.Text.RegularExpressions;
using WindowSwitcherLib.Data.Commands;
using WindowSwitcherLib.Data.FileAccess;

namespace WindowSwitcherLib.Data.WindowAccess;

public sealed class WaylandPortalScreencastCapture : IDisposable
{
    private static readonly Lazy<WaylandPortalScreencastCapture> Instance = new(() => new WaylandPortalScreencastCapture());
    public static WaylandPortalScreencastCapture GetInstance() => Instance.Value;

    private readonly object _syncRoot = new();
    private readonly GdbusWrapper _gdbus = new();
    private readonly GstLaunchWrapper _gst = new();

    private Process? _gstProcess;
    private CancellationTokenSource? _cts;
    private Task? _readerTask;

    private byte[]? _latestFrame;
    private int _width;
    private int _height;
    private uint _nodeId;
    private bool _isRunning;

    public bool IsRunning
    {
        get { lock (_syncRoot) return _isRunning; }
    }

    public int Width
    {
        get { lock (_syncRoot) return _width; }
    }

    public int Height
    {
        get { lock (_syncRoot) return _height; }
    }

    public bool TryGetLatestFrame(out byte[]? frame, out int width, out int height)
    {
        lock (_syncRoot)
        {
            frame = _latestFrame;
            width = _width;
            height = _height;
            return _isRunning && frame is not null && width > 0 && height > 0;
        }
    }

    public bool EnsureStarted()
    {
        lock (_syncRoot)
        {
            if (_isRunning)
                return true;
        }

        // Start outside the lock (dbus calls can block for user consent).
        return StartInternal();
    }

    private bool StartInternal()
    {
        string? createSessionOutput = _gdbus.CallSession(
            "org.freedesktop.portal.Desktop",
            "/org/freedesktop/portal/desktop",
            "org.freedesktop.portal.ScreenCast.CreateSession",
            "\"{}\"");

        if (!TryExtractObjectPath(createSessionOutput, out string? sessionPath))
            return false;

        // Select sources: Monitor only (type=1), single selection.
        // This will show a portal consent dialog.
        string? selectOutput = _gdbus.CallSession(
            "org.freedesktop.portal.Desktop",
            "/org/freedesktop/portal/desktop",
            "org.freedesktop.portal.ScreenCast.SelectSources",
            $"\"{sessionPath}\" \"{{'types': <uint32 1>, 'multiple': <false>}}\"");

        if (!TryExtractObjectPath(selectOutput, out string? selectRequestPath) || string.IsNullOrWhiteSpace(selectRequestPath))
            return false;

        string selectRequestPathNonNull = selectRequestPath;
        if (!WaitForPortalRequest(selectRequestPathNonNull, timeoutMs: 120_000, out uint selectResponseCode, out _))
            return false;
        if (selectResponseCode != 0)
            return false;

        string? startOutput = _gdbus.CallSession(
            "org.freedesktop.portal.Desktop",
            "/org/freedesktop/portal/desktop",
            "org.freedesktop.portal.ScreenCast.Start",
            $"\"{sessionPath}\" \"\" \"{{}}\"");

        if (!TryExtractObjectPath(startOutput, out string? startRequestPath) || string.IsNullOrWhiteSpace(startRequestPath))
            return false;

        string startRequestPathNonNull = startRequestPath;
        if (!WaitForPortalRequest(startRequestPathNonNull, timeoutMs: 120_000, out uint startResponseCode, out string? startDetails))
            return false;
        if (startResponseCode != 0)
            return false;

        if (!TryExtractNodeIdAndSize(startDetails, out uint nodeId, out int width, out int height))
            return false;

        // Start GStreamer PipeWire capture.
        // IMPORTANT: this assumes the node is visible on the default PipeWire remote.
        // On some setups/sandboxes, OpenPipeWireRemote FD forwarding is required; we fall back to screenshots in that case.
        int frameSize = checked(width * height * 4);
        string pipeline =
            $"pipewiresrc target-object={nodeId} do-timestamp=true ! " +
            "videoconvert ! " +
            $"video/x-raw,format=BGRA,width={width},height={height},framerate=30/1 ! " +
            "queue leaky=downstream max-size-buffers=1 ! " +
            "fdsink fd=1 sync=false";

        Process? gstProcess = _gst.StartPipelineToStdout(pipeline);
        if (gstProcess is null)
            return false;

        var cts = new CancellationTokenSource();
        Task readerTask = Task.Run(() => ReadFramesLoop(gstProcess, frameSize, width, height, nodeId, cts.Token), cts.Token);

        lock (_syncRoot)
        {
            if (_isRunning)
            {
                cts.Cancel();
                gstProcess.Kill(entireProcessTree: true);
                gstProcess.Dispose();
                return true;
            }

            _gstProcess = gstProcess;
            _cts = cts;
            _readerTask = readerTask;
            _width = width;
            _height = height;
            _nodeId = nodeId;
            _isRunning = true;
        }

        return true;
    }

    private void ReadFramesLoop(Process process, int frameSize, int width, int height, uint nodeId, CancellationToken cancellationToken)
    {
        var buffer = new byte[frameSize];
        try
        {
            Stream stdout = process.StandardOutput.BaseStream;
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!ReadExact(stdout, buffer, cancellationToken))
                    break;

                byte[] frameCopy = new byte[frameSize];
                Buffer.BlockCopy(buffer, 0, frameCopy, 0, frameSize);
                lock (_syncRoot)
                {
                    _latestFrame = frameCopy;
                    _width = width;
                    _height = height;
                    _nodeId = nodeId;
                }
            }
        }
        catch (Exception ex)
        {
            if (ConfigFileAccessor.GetInstance().ReadConfig(config => config.ActivateLogs))
                AppLogger.Log($"Wayland portal capture failed: {ex.Message}", StaticData.LogSeverity.WARN);
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Ignore.
            }

            lock (_syncRoot)
            {
                _isRunning = false;
            }
        }
    }

    private static bool ReadExact(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < buffer.Length && !cancellationToken.IsCancellationRequested)
        {
            int read = stream.Read(buffer, offset, buffer.Length - offset);
            if (read <= 0)
                return false;
            offset += read;
        }
        return offset == buffer.Length;
    }

    private bool WaitForPortalRequest(string requestPath, int timeoutMs, out uint responseCode, out string? detailsLine)
    {
        responseCode = 1;
        detailsLine = null;

        using Process? monitor = _gdbus.StartMonitorSession("org.freedesktop.portal.Desktop", requestPath);
        if (monitor is null)
            return false;

        var sw = Stopwatch.StartNew();
        try
        {
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                string? line = monitor.StandardOutput.ReadLine();
                if (line is null)
                    break;

                if (!line.Contains("Response", StringComparison.Ordinal))
                    continue;

                detailsLine = line;
                if (TryExtractResponseCode(line, out responseCode))
                    return true;
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
                // Ignore.
            }
        }

        return false;
    }

    private static bool TryExtractObjectPath(string? gdbusOutput, out string? path)
    {
        path = null;
        if (string.IsNullOrWhiteSpace(gdbusOutput))
            return false;

        // Example: "(objectpath '/org/freedesktop/portal/desktop/session/..',)"
        Match match = Regex.Match(gdbusOutput, "objectpath\\s+'([^']+)'", RegexOptions.CultureInvariant);
        if (!match.Success)
            return false;
        path = match.Groups[1].Value;
        return !string.IsNullOrWhiteSpace(path);
    }

    private static bool TryExtractResponseCode(string line, out uint code)
    {
        code = 1;
        Match match = Regex.Match(line, @"uint32\s+(\d+)", RegexOptions.CultureInvariant);
        if (!match.Success)
            return false;
        return uint.TryParse(match.Groups[1].Value, out code);
    }

    private static bool TryExtractNodeIdAndSize(string? line, out uint nodeId, out int width, out int height)
    {
        nodeId = 0;
        width = 0;
        height = 0;

        if (string.IsNullOrWhiteSpace(line))
            return false;

        // Find first stream tuple: (uint32 <nodeId>, { ... 'size': <(int32 <w>, int32 <h>)> ... })
        Match nodeMatch = Regex.Match(line, @"\(\s*uint32\s+(\d+)\s*,", RegexOptions.CultureInvariant);
        if (!nodeMatch.Success || !uint.TryParse(nodeMatch.Groups[1].Value, out nodeId) || nodeId == 0)
            return false;

        Match sizeMatch = Regex.Match(line, @"\(\s*int32\s+(\d+)\s*,\s*int32\s+(\d+)\s*\)", RegexOptions.CultureInvariant);
        if (!sizeMatch.Success)
            return false;

        if (!int.TryParse(sizeMatch.Groups[1].Value, out width) || !int.TryParse(sizeMatch.Groups[2].Value, out height))
            return false;

        return width > 0 && height > 0;
    }

    public void Dispose()
    {
        CancellationTokenSource? cts;
        Process? proc;
        Task? reader;
        lock (_syncRoot)
        {
            cts = _cts;
            proc = _gstProcess;
            reader = _readerTask;
            _cts = null;
            _gstProcess = null;
            _readerTask = null;
            _isRunning = false;
        }

        try
        {
            cts?.Cancel();
        }
        catch
        {
            // Ignore.
        }

        try
        {
            if (proc is not null && !proc.HasExited)
                proc.Kill(entireProcessTree: true);
        }
        catch
        {
            // Ignore.
        }

        try
        {
            proc?.Dispose();
        }
        catch
        {
            // Ignore.
        }

        try
        {
            reader?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // Ignore.
        }
    }
}
