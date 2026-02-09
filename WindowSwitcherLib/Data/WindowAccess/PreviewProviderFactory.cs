using System.Runtime.InteropServices;
using WindowSwitcherLib.Data;
using WindowSwitcherLib.Data.Commands;
using WindowSwitcherLib.Data.FileAccess;

namespace WindowSwitcherLib.Data.WindowAccess;

public static class PreviewProviderFactory
{
    public static IPreviewFrameProvider Create(WinAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return new ScreenshotPreviewFrameProvider(accessor);

        if (!EnsureLinuxDependency("gst-launch-1.0", LinuxDependencies.IsGstLaunchAvailable,
                "PipeWire backend unavailable because `gst-launch-1.0` is missing. Falling back to screenshot backend."))
            return new ScreenshotPreviewFrameProvider(accessor);
        if (!EnsureLinuxDependency("gstreamer-pipewire", LinuxDependencies.IsGstPipeWireSrcAvailable,
                "PipeWire backend unavailable because GStreamer `pipewiresrc` plugin is missing. Falling back to screenshot backend."))
            return new ScreenshotPreviewFrameProvider(accessor);
        if (!EnsureLinuxDependency("pw-dump", LinuxDependencies.IsPwDumpAvailable,
                "PipeWire backend unavailable because `pw-dump` is missing. Falling back to screenshot backend."))
            return new ScreenshotPreviewFrameProvider(accessor);

        LogIfEnabled("Using PipeWire preview backend (wmctrl window-id to PipeWire node matching).");
        return new PipeWireFrameProvider(accessor);
    }

    private static bool EnsureLinuxDependency(string dependencyName, bool isAvailable, string logWhenMissing)
    {
        if (isAvailable)
            return true;

        LinuxDependencies.ReportMissingOnce(dependencyName);
        LogIfEnabled(logWhenMissing);
        return false;
    }

    private static void LogIfEnabled(string message)
    {
        if (!ConfigFileAccessor.GetInstance().ReadConfig(config => config.ActivateLogs))
            return;

        AppLogger.Log($"[PreviewProviderFactory] {message}", StaticData.LogSeverity.WARN);
    }

}
