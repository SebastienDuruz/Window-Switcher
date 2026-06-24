using WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Commands.Dependencies;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Factories.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Pipewire;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Screenshots;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.X11;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.Factories;

/// <summary>
/// Linux preview provider factory selected from Linux runtime capabilities.
/// </summary>
public sealed class LinuxPreviewFrameProviderFactory(
    ILinuxDependencyRegistry? linuxDependencies = null
) : IPreviewFrameProviderFactory
{
    private readonly ILinuxDependencyRegistry _linuxDependencies =
        linuxDependencies ?? LinuxDependencies.Instance;
    private readonly Func<bool> _supportsX11PreviewCapture = X11PreviewFrameProvider.IsSupported;

    internal LinuxPreviewFrameProviderFactory(
        ILinuxDependencyRegistry? linuxDependencies,
        Func<bool> supportsX11PreviewCapture
    )
        : this(linuxDependencies)
    {
        ArgumentNullException.ThrowIfNull(supportsX11PreviewCapture);
        _supportsX11PreviewCapture = supportsX11PreviewCapture;
    }

    /// <inheritdoc />
    public IPreviewFrameProvider Create(WinAccessorBase accessorBase)
    {
        ArgumentNullException.ThrowIfNull(accessorBase);

        if (accessorBase is X11WinAccessor && _supportsX11PreviewCapture())
            return new X11PreviewFrameProvider(
                accessorBase,
                () => CreateFallbackProvider(accessorBase)
            );

        return CreateFallbackProvider(accessorBase);
    }

    private IPreviewFrameProvider CreateFallbackProvider(WinAccessorBase accessorBase)
    {
        if (!LinuxPreviewDependencyEvaluator.SupportsPipeWire(_linuxDependencies))
            return new ScreenshotPreviewFrameProvider(accessorBase);

        return new PipeWireFrameProvider(accessorBase);
    }
}
