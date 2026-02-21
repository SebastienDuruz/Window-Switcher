using WindowSwitcherLib.Data.Platform.Commands.Abstractions;
using WindowSwitcherLib.Data.Platform.Commands.Dependencies;
using WindowSwitcherLib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcherLib.Data.Platform.WindowAccess.Factories.Abstractions;
using WindowSwitcherLib.Data.Platform.WindowAccess.PreviewFrames;
using WindowSwitcherLib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcherLib.Data.Platform.WindowAccess.PreviewFrames.Pipewire;
using WindowSwitcherLib.Data.Platform.WindowAccess.PreviewFrames.Screenshots;

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

        if (!LinuxPreviewDependencyEvaluator.SupportsPipeWire(_linuxDependencies))
            return new ScreenshotPreviewFrameProvider(accessorBase);

        return new PipeWireFrameProvider(accessorBase);
    }
}
