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
public sealed class LinuxPreviewFrameProviderFactory(
    ILinuxDependencyRegistry? linuxDependencies = null
) : IPreviewFrameProviderFactory
{
    private readonly ILinuxDependencyRegistry _linuxDependencies =
        linuxDependencies ?? LinuxDependencies.Instance;

    /// <inheritdoc />
    public IPreviewFrameProvider Create(WinAccessorBase accessorBase)
    {
        ArgumentNullException.ThrowIfNull(accessorBase);

        if (
            !EnsureLinuxDependency(
                dependencyName: "gst-launch-1.0",
                isAvailable: _linuxDependencies.IsGstLaunchAvailable
            )
            || !EnsureLinuxDependency(
                dependencyName: "gstreamer-pipewire",
                isAvailable: _linuxDependencies.IsGstPipeWireSrcAvailable
            )
            || !EnsureLinuxDependency(
                dependencyName: "pw-dump",
                isAvailable: _linuxDependencies.IsPwDumpAvailable
            )
        )
            return new ScreenshotPreviewFrameProvider(accessorBase);

        return new PipeWireFrameProvider(accessorBase);
    }

    private bool EnsureLinuxDependency(string dependencyName, bool isAvailable)
    {
        if (isAvailable)
            return true;

        _linuxDependencies.ReportMissingOnce(dependencyName);
        return false;
    }
}
