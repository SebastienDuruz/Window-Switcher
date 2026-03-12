using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Media.Imaging;
using WindowSwitcher.Lib.Data.Platform.Commands.Dependencies;
using WindowSwitcher.Lib.Data.Platform.Commands.Wrappers;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors;

internal abstract class LinuxWindowAccessorBase : WinAccessorBase
{
    private WmctrlWrapper WmctrlWrapper { get; } = new();
    private ImportWrapper ImportWrapper { get; } = new();

    public override ObservableCollection<WindowConfig> GetWindows()
    {
        if (!LinuxDependencies.IsWmctrlAvailable)
        {
            LinuxDependencies.ReportMissingOnce("wmctrl");
            return new ObservableCollection<WindowConfig>();
        }

        int currentPid = Process.GetCurrentProcess().Id;

        string wmctrlOutput = WmctrlWrapper.Execute(["-l", "-p"], timeoutMs: 2_000);
        if (string.IsNullOrWhiteSpace(wmctrlOutput))
            return new ObservableCollection<WindowConfig>();

        ObservableCollection<WindowConfig> windows = new();
        var processNameByPid = new Dictionary<int, string>();
        string[] lines = wmctrlOutput.Split('\n');
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (!TryParseWmctrlLine(line, out string? windowId, out int pid, out string windowName))
                continue;

            if (pid == currentPid)
                continue;

            if (!processNameByPid.TryGetValue(pid, out string? processName))
            {
                processName = TryReadProcessName(pid);
                processNameByPid[pid] = processName;
            }

            windows.Add(
                new WindowConfig
                {
                    WindowId = windowId!,
                    WindowTitle = windowName,
                    ShortWindowTitle =
                        windowName.Length > 40 ? $"{windowName[..40]}..." : windowName,
                    ProcessName = processName,
                }
            );
        }

        return windows;
    }

    public override void RaiseWindow(string windowId)
    {
        WmctrlWrapper.Execute(["-i", "-a", windowId], timeoutMs: 2_000);
    }

    public override Bitmap? TakeScreenshot(string windowId)
    {
        return TakeScreenshot(windowId, new ScreenshotRequest());
    }

    public override Bitmap? TakeScreenshot(string windowId, ScreenshotRequest request)
    {
        try
        {
            using var stream = ImportWrapper.CaptureScreenshotStream(windowId, request);
            if (stream is null)
                return null;

            return new Bitmap(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public override async Task<Bitmap?> TakeScreenshotAsync(
        string windowId,
        ScreenshotRequest request,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            using var stream = await ImportWrapper
                .CaptureScreenshotStreamAsync(windowId, request, cancellationToken)
                .ConfigureAwait(false);

            if (stream is null)
                return null;

            return new Bitmap(stream);
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

    public override void RenameWindowTitle(string windowId, string windowTitle)
    {
        WmctrlWrapper.Execute(["-i", "-r", windowId, "-T", windowTitle], timeoutMs: 2_000);
    }

    private static bool TryParseWmctrlLine(
        string windowInfo,
        out string? windowId,
        out int pid,
        out string windowTitle
    )
    {
        windowId = null;
        pid = -1;
        windowTitle = string.Empty;

        try
        {
            string[] parts = windowInfo.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5)
                return false;

            windowId = parts[0];
            _ = int.TryParse(parts[2], out pid);

            int titleStartIndex = windowInfo.IndexOf(parts[4], StringComparison.Ordinal);
            windowTitle =
                titleStartIndex >= 0
                    ? windowInfo[titleStartIndex..].Trim()
                    : string.Join(' ', parts.Skip(4));
        }
        catch (Exception)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(windowId);
    }

    private static string TryReadProcessName(int pid)
    {
        if (pid <= 0)
            return string.Empty;

        try
        {
            string commPath = $"/proc/{pid}/comm";
            if (File.Exists(commPath))
                return (File.ReadAllText(commPath) ?? string.Empty).Trim();
        }
        catch
        {
            // Best effort only.
        }

        return string.Empty;
    }
}
