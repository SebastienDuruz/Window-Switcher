using WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Commands.Dependencies;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Factories.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.NoOp;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Pipewire;
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
    private readonly Func<bool> _supportsPipeWire = LibPipeWireNative.IsAvailable;
    private readonly Func<string?> _sessionTypeResolver = LinuxSessionDetector.GetSessionType;

    internal LinuxPreviewFrameProviderFactory(
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

    internal LinuxPreviewFrameProviderFactory(
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

        string sessionType = NormalizeSessionType(_sessionTypeResolver());
        if (!string.Equals(sessionType, "wayland", StringComparison.Ordinal))
            return _supportsX11PreviewCapture()
                ? new X11PreviewFrameProvider(accessorBase)
                : new NoOpPreviewFrameProvider();

        if (
            !LinuxPreviewDependencyEvaluator.SupportsPipeWire(_linuxDependencies, _supportsPipeWire)
        )
            return new NoOpPreviewFrameProvider();

        return new PipeWireFrameProvider(accessorBase);
    }

    private static string NormalizeSessionType(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
    }
}
