using System.Runtime.InteropServices;
using WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Commands.Dependencies;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Factories.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.NoOp;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Pipewire;
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
    private readonly Func<bool> _supportsPipeWire = LibPipeWireNative.IsAvailable;
    private readonly Func<string?> _sessionTypeResolver = LinuxSessionDetector.GetSessionType;

    internal RuntimePreviewFrameProviderFactory(
        ILinuxDependencyRegistry? linuxDependencies,
        Func<bool> supportsX11PreviewCapture,
        Func<string?>? sessionTypeResolver = null
    )
        : this(linuxDependencies)
    {
        ArgumentNullException.ThrowIfNull(supportsX11PreviewCapture);
        _supportsX11PreviewCapture = supportsX11PreviewCapture;
        if (sessionTypeResolver is not null)
            _sessionTypeResolver = sessionTypeResolver;
    }

    internal RuntimePreviewFrameProviderFactory(
        ILinuxDependencyRegistry? linuxDependencies,
        Func<bool> supportsX11PreviewCapture,
        Func<bool> supportsPipeWire,
        Func<string?>? sessionTypeResolver = null
    )
        : this(linuxDependencies, supportsX11PreviewCapture, sessionTypeResolver)
    {
        ArgumentNullException.ThrowIfNull(supportsPipeWire);
        _supportsPipeWire = supportsPipeWire;
    }

    /// <inheritdoc />
    public IPreviewFrameProvider Create(WinAccessorBase accessorBase)
    {
        ArgumentNullException.ThrowIfNull(accessorBase);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return new NoOpPreviewFrameProvider();

        return new LinuxPreviewFrameProviderFactory(
            _linuxDependencies,
            _supportsX11PreviewCapture,
            _supportsPipeWire,
            _sessionTypeResolver
        ).Create(accessorBase);
    }

    private static bool X11PreviewFrameProviderSupport()
    {
        return X11PreviewFrameProvider.IsSupported();
    }
}
