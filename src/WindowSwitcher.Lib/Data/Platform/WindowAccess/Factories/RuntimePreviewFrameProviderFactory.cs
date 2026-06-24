using System.Runtime.InteropServices;
using WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Commands.Dependencies;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Factories.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.NoOp;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.X11;

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
    private readonly Func<bool> _supportsX11PreviewCapture = X11PreviewFrameProviderSupport;

    internal RuntimePreviewFrameProviderFactory(
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

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return new NoOpPreviewFrameProvider();

        return new LinuxPreviewFrameProviderFactory(
            _linuxDependencies,
            _supportsX11PreviewCapture
        ).Create(accessorBase);
    }

    private static bool X11PreviewFrameProviderSupport()
    {
        return X11PreviewFrameProvider.IsSupported();
    }
}
