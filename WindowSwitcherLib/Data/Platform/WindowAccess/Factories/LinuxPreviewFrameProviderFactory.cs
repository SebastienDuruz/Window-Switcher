using WindowSwitcherLib.Data.Platform.Commands.Abstractions;
using WindowSwitcherLib.Data.Platform.Commands.Dependencies;
using WindowSwitcherLib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcherLib.Data.Platform.WindowAccess.Factories.Abstractions;
using WindowSwitcherLib.Data.Platform.WindowAccess.PreviewFrames;
using WindowSwitcherLib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;

namespace WindowSwitcherLib.Data.Platform.WindowAccess.Factories;

/// <summary>
/// Linux preview provider factory selected from Linux runtime capabilities.
/// </summary>
public sealed class LinuxPreviewFrameProviderFactory(ILinuxDependencyRegistry? linuxDependencies = null)
    : IPreviewFrameProviderFactory
{
    private readonly ILinuxDependencyRegistry _linuxDependencies = linuxDependencies ?? LinuxDependencies.Instance;

    /// <inheritdoc />
    public IPreviewFrameProvider Create(WinAccessorBase accessorBase)
    {
        ArgumentNullException.ThrowIfNull(accessorBase);

        if (
            !EnsureLinuxDependency( dependencyName: "gst-launch-1.0", isAvailable: _linuxDependencies.IsGstLaunchAvailable,
                logWhenMissing: "PipeWire backend unavailable because `gst-launch-1.0` is missing. Falling back to screenshot backend.") 
            || !EnsureLinuxDependency( dependencyName: "gstreamer-pipewire", isAvailable: _linuxDependencies.IsGstPipeWireSrcAvailable,
                logWhenMissing: "PipeWire backend unavailable because GStreamer `pipewiresrc` plugin is missing. Falling back to screenshot backend.") 
            || !EnsureLinuxDependency( dependencyName: "pw-dump", isAvailable: _linuxDependencies.IsPwDumpAvailable,
                logWhenMissing: "PipeWire backend unavailable because `pw-dump` is missing. Falling back to screenshot backend."))
            return new ScreenshotPreviewFrameProvider(accessorBase);

        StaticData.LogIfEnabled("Using PipeWire preview backend (wmctrl window-id to PipeWire node matching).");
        return new PipeWireFrameProvider(accessorBase);
    }

    private bool EnsureLinuxDependency(string dependencyName, bool isAvailable, string logWhenMissing)
    {
        if (isAvailable)
            return true;

        _linuxDependencies.ReportMissingOnce(dependencyName);
        StaticData.LogIfEnabled(logWhenMissing);
        return false;
    }
}
