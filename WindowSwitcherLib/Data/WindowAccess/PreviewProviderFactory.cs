using System.Runtime.InteropServices;
using WindowSwitcherLib.Data;
using WindowSwitcherLib.Data.Commands;
using WindowSwitcherLib.Data.FileAccess;
using WindowSwitcherLib.Data.WindowAccess.Accessors;
using WindowSwitcherLib.Data.WindowAccess.PreviewFrames;

namespace WindowSwitcherLib.Data.WindowAccess;

public static class PreviewProviderFactory
{
    public static IPreviewFrameProvider Create(WinAccessorBase accessorBase)
    {
        ArgumentNullException.ThrowIfNull(accessorBase);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return new ScreenshotPreviewFrameProvider(accessorBase);

        if (!StaticData.EnsureLinuxDependency("gst-launch-1.0", LinuxDependencies.IsGstLaunchAvailable,
                "PipeWire backend unavailable because `gst-launch-1.0` is missing. Falling back to screenshot backend."))
            return new ScreenshotPreviewFrameProvider(accessorBase);
        if (!StaticData.EnsureLinuxDependency("gstreamer-pipewire", LinuxDependencies.IsGstPipeWireSrcAvailable,
                "PipeWire backend unavailable because GStreamer `pipewiresrc` plugin is missing. Falling back to screenshot backend."))
            return new ScreenshotPreviewFrameProvider(accessorBase);
        if (!StaticData.EnsureLinuxDependency("pw-dump", LinuxDependencies.IsPwDumpAvailable,
                "PipeWire backend unavailable because `pw-dump` is missing. Falling back to screenshot backend."))
            return new ScreenshotPreviewFrameProvider(accessorBase);

        StaticData.LogIfEnabled("Using PipeWire preview backend (wmctrl window-id to PipeWire node matching).");
        return new PipeWireFrameProvider(accessorBase);
    }
}
