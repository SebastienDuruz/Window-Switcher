using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using WindowSwitcherLib.Data;
using WindowSwitcherLib.Data.Commands;
using WindowSwitcherLib.Data.FileAccess;
using WindowSwitcherLib.Models;

namespace WindowSwitcherLib.Data.WindowAccess;

public class LinuxX11WindowAccessor : WindowAccessor
{
    private WmctrlWrapper WmctrlWrapper { get; set; } = new();
    private ImportWrapper ImportWrapper { get; set; } = new();
    
    public override ObservableCollection<WindowConfig> GetWindows()
    {
        if (!LinuxDependencies.IsWmctrlAvailable)
        {
            LinuxDependencies.ReportMissingOnce("wmctrl");
            return new ObservableCollection<WindowConfig>();
        }

        int currentPid = Process.GetCurrentProcess().Id;

        // -l : list windows
        // -p : include PID (allows filtering our own windows)
        string wmctrlOutput = WmctrlWrapper.Execute(" -lp");
        if (string.IsNullOrWhiteSpace(wmctrlOutput))
            return new ObservableCollection<WindowConfig>();
        
        ObservableCollection<WindowConfig> windows = new ObservableCollection<WindowConfig>();
        string[] lines = wmctrlOutput.Split('\n');
        foreach (string line in lines)
            if (!String.IsNullOrWhiteSpace(line))
            {
                if (!TryParseWmctrlLine(line, out string? windowId, out int pid, out string windowName))
                    continue;

                // Never list WindowSwitcher windows (Main/Settings/About/Floating previews).
                if (pid == currentPid)
                    continue;

                windows.Add(new WindowConfig()
                {
                    WindowId = windowId!,
                    WindowTitle = windowName,
                    ShortWindowTitle = windowName.Length > 40 ? $"{windowName[..40]}..." : windowName
                });
            }
                
        return windows;
    }

    public override void RaiseWindow(string windowId)
    {
        WmctrlWrapper.Execute($" -i -a \"{windowId}\"");
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
        catch (Exception ex)
        {
            if (ConfigFileAccessor.GetInstance().ReadConfig(config => config.ActivateLogs))
                AppLogger.Log($"Linux screenshot failed: {ex.Message}", StaticData.LogSeverity.WARN);
            return null;
        }
    }

    public override async Task<Bitmap?> TakeScreenshotAsync(
        string windowId,
        ScreenshotRequest request,
        CancellationToken cancellationToken = default)
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
        catch (Exception ex)
        {
            if (ConfigFileAccessor.GetInstance().ReadConfig(config => config.ActivateLogs))
                AppLogger.Log($"Linux screenshot failed: {ex.Message}", StaticData.LogSeverity.WARN);
            return null;
        }
    }

    public override void RenameWindowTitle(string windowId, string windowTitle)
    {
        string escapedTitle = windowTitle.Replace("\"", "\\\"");
        WmctrlWrapper.Execute($" -i -r {windowId} -T \"{escapedTitle}\"");
    }

    private static bool TryParseWmctrlLine(string windowInfo, out string? windowId, out int pid, out string windowTitle)
    {
        windowId = null;
        pid = -1;
        windowTitle = string.Empty;

        try
        {
            // Expected format for `wmctrl -lp`:
            // <0xid> <desktop> <pid> <host> <title...>
            string[] parts = windowInfo.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5)
                return false;

            windowId = parts[0];
            _ = int.TryParse(parts[2], out pid);

            int titleStartIndex = windowInfo.IndexOf(parts[4], StringComparison.Ordinal);
            windowTitle = titleStartIndex >= 0 ? windowInfo[titleStartIndex..].Trim() : string.Join(' ', parts.Skip(4));
        }
        catch (Exception ex)
        {
            // TODO : Log
            return false;
        }
        
        return !string.IsNullOrWhiteSpace(windowId);
    }
}
