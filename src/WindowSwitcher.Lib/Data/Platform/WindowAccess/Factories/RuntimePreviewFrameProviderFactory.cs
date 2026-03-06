using System.Runtime.InteropServices;
using WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Commands.Dependencies;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Factories.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Pipewire;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Screenshots;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.Factories;

/// <summary>
/// Default preview provider factory selected from runtime capabilities.
/// </summary>
public sealed class RuntimePreviewFrameProviderFactory(
    ILinuxDependencyRegistry? linuxDependencies = null
) : IPreviewFrameProviderFactory
{
    private readonly ILinuxDependencyRegistry _linuxDependencies =
        linuxDependencies ?? LinuxDependencies.Instance;

    /// <inheritdoc />
    public IPreviewFrameProvider Create(WinAccessorBase accessorBase)
    {
        ArgumentNullException.ThrowIfNull(accessorBase);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return new ScreenshotPreviewFrameProvider(accessorBase);

        if (!LinuxPreviewDependencyEvaluator.SupportsPipeWire(_linuxDependencies))
            return new ScreenshotPreviewFrameProvider(accessorBase);

        return new PipeWireFrameProvider(accessorBase);
    }
}
