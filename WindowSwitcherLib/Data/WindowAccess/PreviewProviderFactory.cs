using System.Runtime.InteropServices;
using WindowSwitcherLib.Data;
using WindowSwitcherLib.Data.Commands;
using WindowSwitcherLib.Data.FileAccess;
using WindowSwitcherLib.Models;

namespace WindowSwitcherLib.Data.WindowAccess;

public static class PreviewProviderFactory
{
    public static IPreviewFrameProvider Create(WinAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return new ScreenshotPreviewFrameProvider(accessor);

        bool isWayland = IsWaylandSession();
        bool wantsPipeWire = true;

        if (!wantsPipeWire)
            return new ScreenshotPreviewFrameProvider(accessor);

        if (!LinuxDependencies.IsGstLaunchAvailable)
        {
            LinuxDependencies.ReportMissingOnce("gst-launch-1.0");
            LogIfEnabled("PipeWire backend unavailable because `gst-launch-1.0` is missing. Falling back to screenshot backend.");
            return new ScreenshotPreviewFrameProvider(accessor);
        }

        if (!LinuxDependencies.IsGstPipeWireSrcAvailable)
        {
            LinuxDependencies.ReportMissingOnce("gstreamer-pipewire");
            LogIfEnabled("PipeWire backend unavailable because GStreamer `pipewiresrc` plugin is missing. Falling back to screenshot backend.");
            return new ScreenshotPreviewFrameProvider(accessor);
        }

        if (!LinuxDependencies.IsPwDumpAvailable)
        {
            LinuxDependencies.ReportMissingOnce("pw-dump");
            LogIfEnabled("PipeWire backend unavailable because `pw-dump` is missing. Falling back to screenshot backend.");
            return new ScreenshotPreviewFrameProvider(accessor);
        }

        LogIfEnabled("Using PipeWire preview backend (wmctrl window-id to PipeWire node matching).");
        return new PipeWireFrameProvider(accessor);
    }

    private static void LogIfEnabled(string message)
    {
        if (!ConfigFileAccessor.GetInstance().ReadConfig(config => config.ActivateLogs))
            return;

        AppLogger.Log($"[PreviewProviderFactory] {message}", StaticData.LogSeverity.WARN);
    }

    private static bool IsWaylandSession()
    {
        string? sessionType = LinuxSessionDetector.GetSessionType();
        return string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase);
    }
}
